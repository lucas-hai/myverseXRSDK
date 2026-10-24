namespace MyVerseXRSDK
{
    /// <summary>结构化域错误，保留请求及实例归属，不包含凭据或原始网络载荷。</summary>
    public readonly struct SdkError
    {
        public string Domain { get; }
        public SdkResult Result { get; }
        public long RuntimeId { get; }
        public long RequestId { get; }
        public long SessionEpoch { get; }
        public SdkError(string domain, SdkResult result, long runtimeId, long requestId = 0, long sessionEpoch = 0)
        { Domain = domain; Result = result; RuntimeId = runtimeId; RequestId = requestId; SessionEpoch = sessionEpoch; }
    }
}
