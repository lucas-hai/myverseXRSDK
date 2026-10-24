namespace MyVerseXRSDK
{
    /// <summary>一次命令的本地完成结果；成功仅代表该命令应答成功，不代表远端长任务已完成。</summary>
    public readonly struct OperationResult
    {
        public long RequestId { get; }
        public string TargetId { get; }
        public SdkResult Result { get; }
        public bool Success => Result.Success;
        public bool WasCached { get; }
        public bool RemoteOutcomeUnknown { get; }

        public OperationResult(long requestId, string targetId, SdkResult result,
            bool wasCached = false, bool remoteOutcomeUnknown = false)
        {
            RequestId = requestId;
            TargetId = targetId ?? string.Empty;
            Result = result;
            WasCached = wasCached;
            RemoteOutcomeUnknown = remoteOutcomeUnknown;
        }
    }
}
