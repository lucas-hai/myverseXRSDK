using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>当前Runtime的结构化错误出口；业务失败保留结果，不通过日志开关改变行为。</summary>
    public sealed class DiagnosticsService
    {
        private readonly long runtimeId;
        private readonly SdkLogger log;
        /// <summary>自定义sink队列超限丢弃的累计条目数；不包含等级过滤。</summary>
        public long DroppedLogCount => log.DroppedCount;
        public SdkError? LastError { get; private set; }
        public event Action<SdkError> Error;
        internal DiagnosticsService(long runtimeId, SdkLogger log = null)
        { this.runtimeId = runtimeId; this.log = log ?? new SdkLogger(runtimeId); }

        internal void Report(string domain, SdkResult result, long requestId = 0, long sessionEpoch = 0)
        {
            if (result.Success) return;
            var error = new SdkError(domain, result, runtimeId, requestId, sessionEpoch);
            LastError = error;
            string message = $"[MVXRSDK][{domain}] {result.Code}: {result.Message}";
            var level = result.Code == SdkErrorCode.InternalError || result.Code == SdkErrorCode.AudioDrainStalled
                ? SdkLogLevel.Error : SdkLogLevel.Warning;
            log.Write(level, domain, message, result.Code, requestId, sessionEpoch);
            var callbacks = Error;
            if (callbacks == null) return;
            foreach (Action<SdkError> callback in callbacks.GetInvocationList())
            {
                try { callback(error); }
                catch (Exception exception) { log.Exception("Diagnostics", exception); }
            }
        }

        internal void Close() => Error = null;
    }
}
