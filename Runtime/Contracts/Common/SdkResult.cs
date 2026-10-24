namespace MyVerseXRSDK
{
    public enum SdkErrorCode
    {
        None = 0,
        InvalidArgument = 1001,
        InvalidState = 1002,
        Busy = 1003,
        Disposed = 1004,
        Unavailable = 1005,
        Cancelled = 1006,
        CapacityExceeded = 1007,
        NotConnected = 2001,
        Timeout = 2002,
        TransportFailed = 2003,
        InvalidResponse = 2004,
        RemoteRejected = 2005,
        InvalidBinding = 3001,
        InvalidCalibration = 3002,
        MissingRegionTile = 3003,
        StaleGeneration = 3004,
        ExternalCalibrationWrite = 3005,
        InvalidGeometry = 3006,
        MediaFailed = 4001,
        AudioDrainStalled = 4002,
        InternalError = 9001
    }

    /// <summary>同步操作结果；失败不会隐式降级为成功。</summary>
    public readonly struct SdkResult
    {
        public SdkErrorCode Code { get; }
        public string Message { get; }
        public bool Success => Code == SdkErrorCode.None;

        public SdkResult(SdkErrorCode code, string message = "")
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        public static SdkResult Ok => new SdkResult(SdkErrorCode.None);
        public static SdkResult Fail(SdkErrorCode code, string message) => new SdkResult(code, message);
    }

    /// <summary>实例生命周期；Running只表示本地服务可用，不代表已登录或校准。</summary>
    public enum SdkClientState { Created, Starting, Running, Stopping, Disposed, Faulted }

    /// <summary>房间连接阶段，与客户端本地生命周期独立。</summary>
    public enum SessionState
    {
        Disabled, Discovering, Allocating, Connecting, Authenticating,
        Joined, Reconnecting, Ended, Failed
    }
}
