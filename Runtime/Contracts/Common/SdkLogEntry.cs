using System;

namespace MyVerseXRSDK
{
    public enum SdkLogLevel { Info, Warning, Error, None }

    /// <summary>脱敏后的实例日志值；不携带原始异常、网络载荷或资源引用。</summary>
    public readonly struct SdkLogEntry
    {
        public SdkLogLevel Level { get; }
        public string Domain { get; }
        public string Message { get; }
        public DateTime TimestampUtc { get; }
        public long RuntimeId { get; }
        public long RequestId { get; }
        public long SessionEpoch { get; }
        public SdkErrorCode Code { get; }

        internal SdkLogEntry(SdkLogLevel level, string domain, string message, long runtimeId,
            long requestId, long sessionEpoch, SdkErrorCode code)
        {
            Level = level; Domain = domain; Message = message; TimestampUtc = DateTime.UtcNow;
            RuntimeId = runtimeId; RequestId = requestId; SessionEpoch = sessionEpoch; Code = code;
        }
    }
}
