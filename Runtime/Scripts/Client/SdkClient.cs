using System;

namespace MyVerseXRSDK
{
    /// <summary>拥有独立生命周期的 SDK 客户端。只能由工厂创建；释放后不能重启或复用句柄。</summary>
    public sealed class SdkClient : IDisposable
    {
        private readonly SdkRuntimeContext context;
        private readonly Action<SdkClient> releaseOwnership;
        private readonly PrefabPool proxies;
        private readonly NetworkFailurePresenter failureHud;
        public long RuntimeId => context.RuntimeId;
        public SdkClientState State { get; private set; } = SdkClientState.Created;
        public SessionService Session { get; }
        public SpatialService Space { get; }
        public DevicePresenceService Devices { get; }
        public AdmissionService Admission { get; }
        public BillingService Billing { get; }
        public DirectorService Director { get; }
        public RecordingService Recording { get; }
        public StreamingService Streaming { get; }
        public DiagnosticsService Diagnostics => context.Diagnostics;
        internal SdkLogger Log => context.Log;

        internal SdkClient(long runtimeId, SdkOptions options, Action<SdkClient> release,
            Func<SdkRuntimeContext, SessionService, DirectorService, StreamingService> createStreaming = null)
        {
            releaseOwnership = release;
            context = new SdkRuntimeContext(runtimeId, options, Tick, Dispose);
            try
            {
                Session = new SessionService(context);
                proxies = new PrefabPool(context.Host.transform);
                Space = new SpatialService(context, Session, proxies);
                Devices = new DevicePresenceService(context, Session, Space, proxies);
                Devices.LocalPoseSampled += Session.SendLocalPose;
                Admission = new AdmissionService(context, Session);
                Billing = new BillingService(context, () => Session.BillingBaseUrl,
                    new UnityHttpRequestRunner(context.Host, options.Session.RequestTimeoutSeconds));
                Director = new DirectorService(context, Session, () => Session.Commands);
                Recording = new RecordingService(context, () => Session.Commands);
                Streaming = createStreaming != null ? createStreaming(context, Session, Director) : new StreamingService(context, Session, Director);
                context.Host.LateTick = () => { if (State == SdkClientState.Running) Streaming.TickSource(); };
                Session.Error += OnSessionError;
                if (options.ShowNetworkFailureHud) failureHud = new NetworkFailurePresenter(Session, Space);
            }
            catch
            {
                RollbackConstruction();
                throw;
            }
        }

        private void RollbackConstruction()
        {
            context.BeginClosing();
            if (Session != null) Session.Error -= OnSessionError;
            Cleanup(() => failureHud?.Dispose());
            Cleanup(() => Streaming?.BeginShutdown());
            Cleanup(() => Director?.Dispose());
            Cleanup(() => Recording?.Dispose());
            Cleanup(() => Billing?.Dispose());
            Cleanup(() => Admission?.Dispose());
            Cleanup(() => Devices?.Dispose());
            Cleanup(() => Space?.Dispose());
            Cleanup(() => Session?.Stop());
            Cleanup(() => proxies?.Dispose());
            Cleanup(context.Close);
            context.Log.Close();
        }

        private void Cleanup(Action release)
        {
            try { release(); }
            catch (Exception error) { context.Log.Exception("Runtime", error); }
        }

        /// <summary>启动本地服务并发起配置的网络阶段；返回值不代表已经登录或校准。</summary>
        public SdkResult Start()
        {
            context.EnsureAlive();
            if (State != SdkClientState.Created)
                return SdkResult.Fail(SdkErrorCode.InvalidState, "客户端只能从 Created 状态启动一次");
            State = SdkClientState.Starting;
            var result = Session.Start();
            // 启动通知可同步重入关闭；较旧的Start调用栈不能复活终态或继续占用资源。
            if (context.IsClosing || State != SdkClientState.Starting)
                return SdkResult.Fail(context.IsDisposed ? SdkErrorCode.Disposed : SdkErrorCode.Cancelled, "启动期间客户端已关闭或正在关闭");
            if (!result.Success)
            {
                Session.Stop();
                State = SdkClientState.Faulted;
                return result;
            }
            context.IsRunning = true;
            State = SdkClientState.Running;
            context.Log.Write(SdkLogLevel.Info, "Runtime", "客户端本地运行已启动");
            return SdkResult.Ok;
        }

        private void Tick()
        {
            if (State != SdkClientState.Running) return;
            Session.Tick();
            Space.Tick();
            Devices.Tick(UnityEngine.Time.realtimeSinceStartup, UnityEngine.Time.unscaledDeltaTime);
            Director.Tick(UnityEngine.Time.realtimeSinceStartup);
            Streaming.Tick();
        }

        private void OnSessionError(SdkResult result) => Diagnostics.Report("Session", result, 0, Session.Current.Epoch);

        private Action<SdkResult> shutdownCompleted;
        private bool finishing;

        /// <summary>发起关闭，在后续主线程调度点完成；重复关闭共享同一释放过程。</summary>
        public void Shutdown(Action<SdkResult> completed = null)
        {
            context.EnsureAlive();
            shutdownCompleted += completed;
            if (State == SdkClientState.Stopping) return;
            State = SdkClientState.Stopping;
            context.BeginClosing();
            context.Host.Post(FinishShutdown);
        }

        /// <summary>发起本地关闭，不阻塞主线程、不承诺远端通知送达；native仍被使用时保持Stopping，安全释放后才Disposed。</summary>
        public void Dispose()
        {
            context.EnsureMainThread();
            if (State == SdkClientState.Disposed) return;
            State = SdkClientState.Stopping;
            context.BeginClosing();
            FinishShutdown();
        }

        private void FinishShutdown()
        {
            if (finishing || State == SdkClientState.Disposed) return;
            finishing = true;
            failureHud?.Dispose();
            Director.Dispose();
            Recording.Dispose();
            Billing.Dispose();
            Admission.Dispose();
            Streaming.BeginShutdown();
            Session.Stop();
            Session.Error -= OnSessionError;
            Devices.Dispose();
            Space.Dispose();
            proxies.Dispose();
            if (!TryCompleteShutdown()) context.Host.Run(WaitForMediaShutdown());
        }

        private System.Collections.IEnumerator WaitForMediaShutdown()
        {
            while (!TryCompleteShutdown()) yield return null;
        }

        internal bool TryCompleteShutdown()
        {
            if (State == SdkClientState.Disposed) return true;
            if (!finishing) return false;
            Streaming.Tick();
            if (!Streaming.IsReleased) return false;
            context.Close();
            State = SdkClientState.Disposed;
            releaseOwnership(this);
            var callbacks = shutdownCompleted;
            shutdownCompleted = null;
            if (callbacks != null)
                foreach (Action<SdkResult> callback in callbacks.GetInvocationList())
                {
                    try { callback(SdkResult.Ok); }
                    catch (Exception error) { context.Log.Exception("Runtime", error); }
                }
            context.Log.Write(SdkLogLevel.Info, "Runtime", "客户端已安全释放");
            context.Log.Close();
            return true;
        }
    }
}
