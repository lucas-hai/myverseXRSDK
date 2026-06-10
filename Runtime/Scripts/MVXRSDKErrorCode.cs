namespace MyVerseXRSDK
{
    /// <summary>
    /// SDK 统一错误码（v2）。按业务域分段，每段保留 100 号余量便于扩展。
    /// 数值序号本身是稳定契约——业务方上报埋点/服务端日志可直接用 (int) cast。
    /// </summary>
    public enum MVXRSDKErrorCode
    {
        /// <summary>成功（仅在结构化结果对象中使用，事件不会带此值）。</summary>
        Ok = 0,

        // === 1xxx 通用 / 状态 ===
        NotInitialized = 1001,
        AlreadyInitialized = 1002,
        InvalidArgument = 1003,
        InvalidState = 1004,
        Unknown = 1099,

        // === 2xxx 网络 / Socket ===
        SocketNotConnected = 2001,
        SocketConnectFailed = 2002,
        SocketReconnectExhausted = 2003,
        SocketKickedOut = 2004,
        ProtobufParseFailed = 2005,
        HttpRequestFailed = 2006,
        HttpResponseInvalid = 2007,
        RequestTimeout = 2008,

        // === 3xxx 房间 / 中控 ===
        RoomAllocateFailed = 3001,
        RoomDisbanded = 3002,
        LoginFailed = 3003,
        RoomNotAllocated = 3004,

        // === 4xxx 推流（覆盖旧 StreamErrorCode）===
        // PushStreamModule 不再触发（无源启动推黑帧）；WebRTCSystem 对 null RT 的预检仍使用。
        NoStreamSource = 4001,
        InvalidStreamUrl = 4002,
        /// <summary>RT 尺寸低于 com.unity.webrtc 编码器最小（minWidth=145/minHeight=49），
        /// 通常源于 Editor Game View 窗口过小或 PICO XR fallback 到 cam.pixelWidth 异常路径。</summary>
        InvalidStreamSourceSize = 4003,
        WebRTCInitFailed = 4101,
        IceGatheringTimeout = 4102,
        WhipPostFailed = 4103,
        WhipAuthFailed = 4104,
        WhipStreamNotFound = 4105,
        SdpNegotiationFailed = 4106,
        DtlsHandshakeFailed = 4107,
        IceConnectionFailed = 4108,
        CodecNegotiationFailed = 4109,
        ConnectionLost = 4110,

        // === 5xxx 录屏 ===
        RecordInvalidOptions = 5001,
        RecordNotConnected = 5002,
        RecordAlreadyRecording = 5003,
        RecordRemoteRejected = 5004,
        RecordTimeout = 5005,
        RecordParseFailed = 5006,

        // === 6xxx 节点 / 空间 ===
        NodeNull = 6001,
        NodeAlreadyRegistered = 6002,
        XROffsetAfterInit = 6003,

        // === 7xxx 积分 ===
        TransactionFailed = 7001,
        TransactionInProgress = 7002,
        TransactionBaseUrlMissing = 7003,
    }
}
