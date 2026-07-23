using System;
using System.Collections.Generic;
using Google.Protobuf;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 录屏请求-应答聚合器。SDK 仅做参数转发，pb StartRecord.Response 只含 Success，
    /// 因此结果回调仅传 success + errMsg。无 StopRecord（限时模式由 Duration 控制）。
    ///
    /// 并发语义（按录制目标去重）：服务端支持多路并发录制，故守卫按"录制目标"键控——
    /// 不同目标（如 PICO 头显 vs 外部真实摄像机）可同时在途；仅同一目标在途时重复请求
    /// 才去重拒绝（防抖）。目标键见 <see cref="BuildTargetKey"/>。
    /// </summary>
    internal class RecordModule
    {
        private readonly Func<bool> m_IsConnected;
        private readonly Action<string, ByteString, Action<int, byte[]>> m_SendRequest;

        // 在途（已发出未应答）的录制目标键集合；应答/超时/断连回调回来时移除对应键。
        // 用集合而非单个 bool：不同目标可并发在途，各自独立收敛
        private readonly HashSet<string> m_PendingKeys = new();

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

            string targetKey = BuildTargetKey(opts);
            if (m_PendingKeys.Contains(targetKey))
            {
                MVXRSDKLog.Warning($"RecordModule: 该录制目标已有进行中的请求，拒绝重复 StartRecord key={targetKey}");
                OnResult?.Invoke(MVXRSDKErrorCode.RecordAlreadyRecording, $"another record in progress for target {targetKey}");
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

            m_PendingKeys.Add(targetKey);
            MVXRSDKLog.Info($"RecordModule: 发起 StartRecord fileName={req.FileName} duration={req.Duration}s realCamera={req.RealCamera}");

            // 闭包捕获 targetKey：多目标并发在途时，应答回来须精确移除对应目标（不能清全局标志）
            m_SendRequest(MessageType.CS_START_RECORD, req.ToByteString(),
                (code, buffer) => OnStartResponse(targetKey, code, buffer));
        }

        /// <summary>
        /// 录制目标唯一键：真实摄像机按 CameraId 区分，PICO 设备按 PicoDeviceId 区分。
        /// pico/cam 两类前缀天然不冲突，RealCamera 语义已隐含在前缀里。
        /// 不同目标可并发在途；同目标重复请求去重。
        /// </summary>
        private static string BuildTargetKey(StartRecordOptions opts)
            => opts.RealCamera
                ? $"cam:{opts.CameraId ?? string.Empty}"
                : $"pico:{opts.PicoDeviceId ?? string.Empty}";

        private void OnStartResponse(string targetKey, int code, byte[] buffer)
        {
            m_PendingKeys.Remove(targetKey);

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
