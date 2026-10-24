using System;
using System.Collections.Generic;
using Google.Protobuf;

namespace MyVerseXRSDK
{
    /// <summary>远端录制命令服务；只跟踪请求在途，不宣称远端文件已完成。</summary>
    public sealed class RecordingService : IDisposable
    {
        private readonly SdkRuntimeContext context;
        private readonly Func<ICommandTransport> channel;
        private readonly CommandScope scope;
        private readonly Dictionary<string, OperationHandle> pending = new Dictionary<string, OperationHandle>(StringComparer.Ordinal);
        private bool disposed;

        internal RecordingService(SdkRuntimeContext context, Func<ICommandTransport> channel)
        { this.context = context; this.channel = channel; scope = new CommandScope(context, "Recording"); }

        public OperationHandle Start(RecordingRequest request, Action<OperationResult> completed)
        {
            string key = request == null ? string.Empty : (request.TargetKind == RecordingTargetKind.Camera ? "camera:" : "device:") + request.TargetId;
            var operation = scope.Begin(key, completed);
            if (!context.IsRunning) return Reject(operation, SdkErrorCode.InvalidState, "客户端尚未启动");
            if (request == null || string.IsNullOrWhiteSpace(request.TargetId) || request.DurationSeconds <= 0 ||
                !Enum.IsDefined(typeof(RecordingTargetKind), request.TargetKind))
                return Reject(operation, SdkErrorCode.InvalidArgument, "录制目标或时长无效");
            if (pending.ContainsKey(key)) return Reject(operation, SdkErrorCode.Busy, "该目标已有录制请求在途");
            var transport = channel();
            if (transport == null || !transport.IsConnected) return Reject(operation, SdkErrorCode.NotConnected, "房间连接不可用");
            bool camera = request.TargetKind == RecordingTargetKind.Camera;
            var payload = new global::StartRecord.Types.Request
            {
                RealCamera = camera, CameraId = camera ? request.TargetId : string.Empty,
                PicoDeviceId = camera ? string.Empty : request.TargetId,
                Duration = request.DurationSeconds, FileName = request.FileName ?? string.Empty
            };
            pending.Add(key, operation);
            operation.Finished += finished => { if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, finished)) pending.Remove(key); };
            operation.MarkSent();
            var lease = transport.Send("logic.StartRecord", payload.ToByteArray(), (code, bytes) =>
            {
                if (disposed || operation.IsCompleted) return;
                if (code != 0)
                {
                    var failure = code == SocketMessageTransport.CapacityCode ? SdkErrorCode.CapacityExceeded :
                        code == SocketMessageTransport.TimeoutCode ? SdkErrorCode.Timeout : SdkErrorCode.RemoteRejected;
                    operation.TryComplete(SdkResult.Fail(failure, "录制请求未获成功应答"), false, code < 0);
                    return;
                }
                if (!ProtocolMapper.TryParse(global::StartRecord.Types.Response.Parser, bytes, out var response, context.Log))
                { operation.TryComplete(SdkResult.Fail(SdkErrorCode.InvalidResponse, "录制响应无法解析"), false, true); return; }
                operation.TryComplete(response.Success ? SdkResult.Ok : SdkResult.Fail(SdkErrorCode.RemoteRejected, "服务端拒绝录制"));
            });
            CommandScope.AttachLease(operation, lease);
            return operation;
        }

        private static OperationHandle Reject(OperationHandle operation, SdkErrorCode code, string message)
        { operation.TryComplete(SdkResult.Fail(code, message)); return operation; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            scope.Dispose();
            pending.Clear();
        }
    }
}
