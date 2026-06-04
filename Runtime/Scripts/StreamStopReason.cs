namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流停止原因。替代旧 OnPushStreamStopped(bool active) 的歧义 bool 参数。
    /// </summary>
    public enum StreamStopReason
    {
        /// <summary>业务方主动调 Stop / 中控 NotifyLive(start=false)。</summary>
        ServerStop = 0,
        /// <summary>SDK 内部主动停止（如 NotifyLive 切 URL 时断旧重连）。</summary>
        UserStop = 1,
        /// <summary>网络异常断流 / WS 重连耗尽。</summary>
        NetworkLost = 2,
        /// <summary>URL 变更触发的断旧准备启新。</summary>
        ConfigChanged = 3,
        /// <summary>SDK 反初始化（UnInitMVXRSDK）触发的强制停流，用于区分用户主动 stop。</summary>
        SdkUnInit = 4,
    }
}
