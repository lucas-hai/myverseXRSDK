using System;
using System.Collections.Generic;

namespace MyVerseXRSDK
{
    /// <summary>客户端自己的房间会话；连接状态、接受代次与外部输入来源在此集中管理。</summary>
    public sealed class SessionService
    {
        private readonly SdkRuntimeContext context;
        private long nextEpoch;
        private long notificationRevision;
        private NativeSessionConnection native;
        public SessionState State => Current.State;
        public SdkResult LastError { get; private set; } = SdkResult.Ok;
        public event Action<SdkResult> Error;
        internal ICommandTransport Commands => native?.Commands;
        internal SdkLogger Log => context.Log;
        internal string BillingBaseUrl => native?.BillingBaseUrl ?? context.Options.Session.BillingBaseUrl;
        public SessionSnapshot Current { get; private set; } = new SessionSnapshot(0, SessionState.Disabled, "");
        public event Action<SessionSnapshot> Changed;

        internal event Action<SessionSnapshot> SessionChanged;
        internal event Action<long, long, RegionUpdate> RegionReceived;
        internal event Action<long, IReadOnlyList<ObstacleUpdate>> ObstaclesReceived;
        internal event Action<long, DevicePose> DeviceReceived;
        internal event Action<long, string> DeviceRemoved;
        internal event Action<long, string, bool> LiveReceived;
        internal event Action<GameStartRequest> AdmissionReceived;
        internal event Action<DirectorSelection> SelectionReceived;

        internal RemoteDeviceStore DeviceStore { get; }

        internal SessionService(SdkRuntimeContext context)
        {
            this.context = context;
            DeviceStore = new RemoteDeviceStore(context.Options.DeviceId, context.Options.Devices.MaximumDevices);
        }

        internal SdkResult CheckObstacleCapacity(int count) => count <= context.Options.Spatial.MaximumObstacles
            ? SdkResult.Ok : SdkResult.Fail(SdkErrorCode.CapacityExceeded, "障碍物快照数量超出实例容量");

        internal SdkResult Start()
        {
            context.EnsureAlive();
            if (context.Options.Session.Mode == SessionConnectionMode.External) return SdkResult.Ok;
            try
            {
                var commands = new SocketMessageTransport(context.Options.Session, () => UnityEngine.Time.realtimeSinceStartup,
                    () => new UnityNetworkSocket(context.Options.Session, ReportError, context.Log), ReportError, context.Log);
                var http = new UnityHttpRequestRunner(context.Host, context.Options.Session.RequestTimeoutSeconds);
                native = new NativeSessionConnection(context, this, commands, http, () => UnityEngine.Time.realtimeSinceStartup);
                native.Start();
                return SdkResult.Ok;
            }
            catch (Exception error)
            {
                native?.Dispose();
                native = null;
                return SdkResult.Fail(SdkErrorCode.TransportFailed, "启动原生接入失败：" + error.Message);
            }
        }

        /// <summary>建立新的外部数据代次；只在External模式且客户端Running时允许，旧写入句柄立即失效。</summary>
        public ExternalSessionWriter OpenExternalSession(string sessionKey)
        {
            context.EnsureAlive();
            if (!context.IsRunning || context.Options.Session.Mode != SessionConnectionMode.External)
                throw new InvalidOperationException("仅运行中的 External 客户端允许外部输入");
            if (string.IsNullOrWhiteSpace(sessionKey)) throw new ArgumentException("会话标识不能为空", nameof(sessionKey));
            long epoch = BeginSession(sessionKey);
            return new ExternalSessionWriter(this, epoch);
        }

        internal long BeginSession(string roomId)
        {
            long epoch = ++nextEpoch;
            SetCurrent(new SessionSnapshot(epoch, SessionState.Joined, roomId));
            return epoch;
        }

        internal SdkResult ValidateWriter(long epoch)
        {
            context.EnsureMainThread();
            if (context.IsDisposed || !context.IsRunning || !Current.IsJoined || Current.Epoch != epoch)
                return SdkResult.Fail(SdkErrorCode.StaleGeneration, "写入句柄不再属于当前有效会话");
            return SdkResult.Ok;
        }

        internal void CloseExternal(long epoch)
        {
            context.EnsureMainThread();
            if (context.IsDisposed || Current.Epoch != epoch || !Current.IsJoined) return;
            Stop();
        }

        internal void Stop()
        {
            var owned = native;
            native = null;
            owned?.Dispose();
            EndSession();
        }

        internal void EndSession()
        {
            if (State == SessionState.Ended) return;
            SetCurrent(new SessionSnapshot(++nextEpoch, SessionState.Ended, ""));
        }

        internal void Tick() => native?.Tick();
        internal void SendLocalPose(DevicePose pose) => native?.SendPose(pose);
        internal void UpdateConnection(SessionState state) => SetCurrent(new SessionSnapshot(Current.Epoch, state, Current.RoomId));
        internal void InvalidateReferenceFrame()
        {
            if (native != null) native.RestartReferenceFrame();
            else EndSession();
        }

        internal void ReportError(SdkResult error)
        {
            LastError = error;
            var callbacks = Error;
            if (callbacks == null)
            {
                context.Log.Write(SdkLogLevel.Warning, "Session", $"{error.Code}: {error.Message}", error.Code, sessionEpoch: Current.Epoch);
                return;
            }
            foreach (Action<SdkResult> callback in callbacks.GetInvocationList())
            {
                try { callback(error); }
                catch (Exception exception) { context.Log.Exception("Session", exception); }
            }
        }

        internal void ReceiveAdmission(GameStartRequest request) => AdmissionReceived?.Invoke(request);
        internal void ReceiveSelection(DirectorSelection selection) => SelectionReceived?.Invoke(selection);

        private void SetCurrent(SessionSnapshot snapshot)
        {
            long revision = ++notificationRevision;
            Current = snapshot;
            context.Log.Write(SdkLogLevel.Info, "Session", "会话状态：" + snapshot.State, sessionEpoch: snapshot.Epoch);
            if (snapshot.State != SessionState.Reconnecting) DeviceStore.Clear();
            var internalCallbacks = SessionChanged;
            if (internalCallbacks != null)
                foreach (Action<SessionSnapshot> callback in internalCallbacks.GetInvocationList())
                {
                    if (revision != notificationRevision) return;
                    callback(snapshot);
                }
            var callbacks = Changed;
            if (callbacks == null) return;
            foreach (Action<SessionSnapshot> callback in callbacks.GetInvocationList())
            {
                if (revision != notificationRevision) return;
                try { callback(snapshot); }
                catch (Exception error) { context.Log.Exception("Session", error); }
            }
        }

        internal void ReceiveRegion(long epoch, long revision, RegionUpdate region) => RegionReceived?.Invoke(epoch, revision, region);
        internal void ReceiveObstacles(long epoch, IReadOnlyList<ObstacleUpdate> obstacles)
        {
            if (epoch != Current.Epoch || !Current.IsJoined || context.IsClosing) return;
            var result = CheckObstacleCapacity(obstacles.Count);
            if (!result.Success) { ReportError(result); return; }
            ObstaclesReceived?.Invoke(epoch, obstacles);
        }
        internal void ReceiveDevice(long epoch, DevicePose device)
        {
            if (epoch != Current.Epoch || !Current.IsJoined || context.IsClosing) return;
            var result = DeviceStore.Upsert(device);
            if (!result.Success) { ReportError(result); return; }
            DeviceReceived?.Invoke(epoch, device);
        }
        internal void RemoveDevice(long epoch, string deviceId)
        {
            if (epoch != Current.Epoch || !Current.IsJoined || context.IsClosing) return;
            DeviceStore.Remove(deviceId);
            DeviceRemoved?.Invoke(epoch, deviceId);
        }
        internal void ReceiveLive(long epoch, string url, bool start) => LiveReceived?.Invoke(epoch, url, start);
    }
}
