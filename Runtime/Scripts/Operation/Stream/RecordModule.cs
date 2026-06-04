using System;
using Google.Protobuf;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 录屏请求-应答聚合器。SDK 仅做参数转发，pb StartRecord.Response 只含 Success，
    /// 因此结果回调仅传 success + errMsg。无 StopRecord（限时模式由 Duration 控制）。
    /// </summary>
    internal class RecordModule
    {
        private readonly Func<bool> m_IsConnected;
        private readonly Action<string, ByteString, Action<int, byte[]>> m_SendRequest;

        // 是否有进行中的请求；同一时刻只允许一个 StartRecord 未应答
        private bool m_Pending;

        internal event Action<MVXRSDKErrorCode, string> OnResult;   // 成功时 code=Ok

        internal RecordModule(
            Func<bool> isConnected,
            Action<string, ByteString, Action<int, byte[]>> sendRequest)
        {
            m_IsConnected = isConnected ?? (() => false);
            m_SendRequest = sendRequest ?? throw new ArgumentNullException(nameof(sendRequest));
        }

        internal void StartRecord(StartRecordOptions opts)
        {
            if (opts == null)
            {
                MVXRSDKLog.Warning("RecordModule: StartRecord opts 为空，拒绝");
                OnResult?.Invoke(MVXRSDKErrorCode.RecordInvalidOptions, "opts is null");
                return;
            }

            if (m_Pending)
            {
                MVXRSDKLog.Warning("RecordModule: 已有进行中的录屏请求，拒绝 StartRecord");
                OnResult?.Invoke(MVXRSDKErrorCode.RecordAlreadyRecording, "another record in progress");
                return;
            }

            if (!m_IsConnected())
            {
                MVXRSDKLog.Warning("RecordModule: WS 未连接，拒绝 StartRecord");
                OnResult?.Invoke(MVXRSDKErrorCode.RecordNotConnected, "WS not connected");
                return;
            }

            // 构造 pb StartRecord.Request
            var req = new global::StartRecord.Types.Request
            {
                RealCamera   = opts.RealCamera,
                CameraId     = opts.CameraId ?? string.Empty,
                Duration     = opts.DurationSec,
                FileName     = opts.FileName ?? string.Empty,
                PicoDeviceId = opts.PicoDeviceId ?? string.Empty
            };

            m_Pending = true;
            MVXRSDKLog.Info($"RecordModule: 发起 StartRecord fileName={req.FileName} duration={req.Duration}s realCamera={req.RealCamera}");

            m_SendRequest(MessageType.CS_START_RECORD, req.ToByteString(), OnStartResponse);
        }

        private void OnStartResponse(int code, byte[] buffer)
        {
            m_Pending = false;

            if (code != 0)
            {
                bool isTimeout = code == SocketModule.RequestTimeoutCode;
                var ec = isTimeout ? MVXRSDKErrorCode.RecordTimeout : MVXRSDKErrorCode.RecordRemoteRejected;
                var msg = isTimeout ? "server response timeout" : $"server response code={code}";
                MVXRSDKLog.Warning($"RecordModule: StartRecord 应答失败 code={code} → {ec}");
                OnResult?.Invoke(ec, msg);
                return;
            }

            // 解析 pb StartRecord.Response
            if (!SocketSystem.TryParse<global::StartRecord.Types.Response>(buffer, out var resp, "Record.StartRecordResp"))
            {
                OnResult?.Invoke(MVXRSDKErrorCode.RecordParseFailed, "protobuf parse failed");
                return;
            }

            if (!resp.Success)
            {
                MVXRSDKLog.Warning("RecordModule: 服务端返回 Success=false");
                OnResult?.Invoke(MVXRSDKErrorCode.RecordRemoteRejected, "server returned Success=false");
                return;
            }

            MVXRSDKLog.Info("RecordModule: StartRecord 成功");
            OnResult?.Invoke(MVXRSDKErrorCode.Ok, string.Empty);
        }
    }
}
