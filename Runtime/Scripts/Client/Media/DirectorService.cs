using System;
using Google.Protobuf;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>导演仲裁与单一候选相机意图。受理、选中通知和本机开流是不同语义。</summary>
    public sealed class DirectorService : IDisposable
    {
        private readonly SdkRuntimeContext context;
        private readonly SessionService session;
        private readonly Func<ICommandTransport> channel;
        private readonly CommandScope scope;
        private OperationHandle pending;
        private Camera candidate;
        private long candidateRequest;
        private long candidateEpoch;
        private float deadline;
        private bool disposed;
        public event Action<DirectorSelection> Selected;

        internal DirectorService(SdkRuntimeContext context, SessionService session, Func<ICommandTransport> channel)
        {
            this.context = context;
            this.session = session;
            this.channel = channel;
            scope = new CommandScope(context, "Director");
            session.SelectionReceived += OnSelected;
            session.SessionChanged += OnSession;
        }

        public OperationHandle Request(DirectorRequest request, Action<OperationResult> completed)
        {
            var operation = scope.Begin(context.Options.DeviceId, completed);
            if (!context.IsRunning || !session.Current.IsJoined) return Reject(operation, SdkErrorCode.NotConnected, "房间尚未登录");
            if (request == null || request.DurationSeconds <= 0 || request.Lenses < 1)
                return Reject(operation, SdkErrorCode.InvalidArgument, "导演请求时长或镜头数无效");
            if (pending != null && !pending.IsCompleted || candidate != null)
                return Reject(operation, SdkErrorCode.Busy, "已有导演请求或相机候选等待仲裁");
            var transport = channel();
            if (transport == null || !transport.IsConnected) return Reject(operation, SdkErrorCode.NotConnected, "房间连接不可用");
            string source = request.Source ?? string.Empty;
            if (request.AutoCamera != null)
            {
                if (source.Length == 0) source = "unity";
                if (source != "unity") return Reject(operation, SdkErrorCode.InvalidArgument, "自动相机必须使用unity画面来源");
                candidate = request.AutoCamera;
                candidateRequest = operation.RequestId;
                candidateEpoch = session.Current.Epoch;
                deadline = Time.realtimeSinceStartup + context.Options.Session.RequestTimeoutSeconds;
            }
            pending = operation;
            operation.Finished += OnFinished;
            var payload = new DirectorInsert.Types.Request
            {
                Source = source, Lenses = request.Lenses, DurationSec = request.DurationSeconds,
                Record = request.Record, FileName = request.FileName ?? string.Empty
            };
            operation.MarkSent();
            var lease = transport.Send("logic.DirectorInsert", payload.ToByteArray(), (code, bytes) =>
            {
                if (disposed || operation.IsCompleted) return;
                if (code != 0 || !ProtocolMapper.TryParse(DirectorInsert.Types.Response.Parser, bytes, out var response, context.Log) || !response.Success)
                { operation.TryComplete(SdkResult.Fail(code == SocketMessageTransport.CapacityCode ? SdkErrorCode.CapacityExceeded :
                    code == SocketMessageTransport.TimeoutCode ? SdkErrorCode.Timeout : SdkErrorCode.RemoteRejected, "导演请求未受理"), false, code < 0); return; }
                operation.TryComplete(SdkResult.Ok);
            });
            CommandScope.AttachLease(operation, lease);
            return operation;
        }

        private void OnFinished(OperationHandle operation)
        {
            operation.Finished -= OnFinished;
            if (ReferenceEquals(pending, operation)) pending = null;
            if (candidateRequest == operation.RequestId && operation.Outcome.HasValue && !operation.Outcome.Value.Success) ClearIntent();
        }

        internal Camera TakeCamera()
        {
            Tick(Time.realtimeSinceStartup);
            var value = candidateEpoch == session.Current.Epoch ? candidate : null;
            ClearIntent();
            return value;
        }

        /// <summary>撤销尚未消费的本机候选；不能撤销服务端已经受理的仲裁或停止当前推流。</summary>
        public void CancelPendingCamera()
        {
            context.EnsureAlive();
            ClearIntent();
        }

        internal void Tick(float now)
        {
            if (candidateRequest != 0 && (candidate == null || now >= deadline)) ClearIntent();
        }

        private void ClearIntent() { candidate = null; candidateRequest = 0; candidateEpoch = 0; }
        private void OnSession(SessionSnapshot snapshot)
        {
            if (!snapshot.IsJoined || candidateEpoch != snapshot.Epoch) ClearIntent();
        }
        private void OnSelected(DirectorSelection selection)
        {
            var callbacks = Selected;
            if (callbacks == null) return;
            foreach (Action<DirectorSelection> callback in callbacks.GetInvocationList())
            {
                try { callback(selection); }
                catch (Exception error) { context.Log.Exception("Director", error); }
            }
        }
        private static OperationHandle Reject(OperationHandle operation, SdkErrorCode code, string message)
        { operation.TryComplete(SdkResult.Fail(code, message)); return operation; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.SelectionReceived -= OnSelected;
            session.SessionChanged -= OnSession;
            scope.Dispose();
            ClearIntent();
            Selected = null;
        }
    }
}
