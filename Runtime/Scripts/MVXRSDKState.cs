namespace MyVerseXRSDK
{
    /// <summary>
    /// SDK 生命周期状态机。业务方通过 <see cref="MVXRSDK.State"/> 查询当前阶段，
    /// 配合 <see cref="MVXRSDK.IsReady"/> / <see cref="MVXRSDK.IsConnected"/> 判断业务 API 是否可调。
    /// </summary>
    public enum MVXRSDKState
    {
        /// <summary>进程启动 / UnInit 后的初始态；禁止调任何业务 API。</summary>
        NotInitialized = 0,
        /// <summary>InitMVXRSDK 调用中，本地阶段未完成。</summary>
        Initializing = 1,
        /// <summary>本地 Manager 装配完成，可调 SetStreamSource 等本地能力；Offline 模式终态。</summary>
        LocalReady = 2,
        /// <summary>Production/WsDirect 模式：HTTP 配置拉取 / 房间分配轮询 / WS 握手 / 登录中。</summary>
        Connecting = 3,
        /// <summary>WS 已连接且登录成功；房间已分配。可调所有业务 API。</summary>
        Connected = 4,
        /// <summary>曾 Connected 后掉线（含 reconnect 中）；StreamManager / 业务侧应等待自愈或调 UnInit。</summary>
        Disconnected = 5,
        /// <summary>UnInitMVXRSDK 完毕；下次 InitMVXRSDK 会回到 NotInitialized → Initializing 流程。</summary>
        Disposed = 6,
    }
}
