using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// MVXRSDK 对外 Facade（v2）。本文件只保留 Core 职责：
    /// - Bootstrap（EnsureBootstrap / RuntimeInitializeOnLoadMethod）
    /// - 生命周期（InitMVXRSDK / UnInitMVXRSDK / InitSystems / InitLocalManagers / StartNetwork*）
    /// - 状态机（State / IsReady / IsConnected / IsInitializing）
    /// - 通用静态字段（DeviceId / BaseUrl / RoomAllocationStatus）
    /// - 积分扣除入口（TransactionVerification）
    ///
    /// 拆分子文件：
    /// - <see cref="MVXRSDK.Events.cs"/>  所有 public event + Raise 入口
    /// - <see cref="MVXRSDK.Nodes.cs"/>   XR Offset / Self / Root 节点注册
    /// - <see cref="MVXRSDK.Stream.cs"/>  推流 / 录屏 / 切镜 / 音频 PCM
    /// - <see cref="MVXRSDKErrorCode"/> / <see cref="MVXRSDKState"/> / <see cref="StreamStopReason"/> 各自独立 enum 文件
    /// </summary>
    public static partial class MVXRSDK
    {
        // ============================== 基础静态字段 ==============================

        internal static Transform MVXRSDKManager { get; private set; }

        /// <summary>本机设备 SN（InitMVXRSDK 时传入）。业务侧做"中控切镜是否落在本机"判定要用。</summary>
        public static string DeviceId { get; private set; }

        internal static bool IsTransactionVerification { get; private set; }
        internal static string BaseUrl { get; private set; }

        private static bool m_IsBeDoingTransactionVerification;

        internal static RoomAllocationStatus RoomAllocationStatus { get; private set; }

        internal static void SetRoomAllocationStatus(RoomAllocationStatus status)
        {
            if (RoomAllocationStatus == status) return;
            MVXRSDKLog.Info($"MVXRSDK.RoomAllocationStatus: {RoomAllocationStatus} → {status}");
            RoomAllocationStatus = status;
        }

        // ============================== 状态机 ==============================

        private static MVXRSDKState s_State = MVXRSDKState.NotInitialized;

        /// <summary>当前 SDK 生命周期状态。</summary>
        public static MVXRSDKState State => s_State;

        /// <summary>本地是否已就绪（含 Offline / WsDirect / Production 三档）；可调本地能力 API。</summary>
        public static bool IsReady =>
            s_State == MVXRSDKState.LocalReady ||
            s_State == MVXRSDKState.Connecting ||
            s_State == MVXRSDKState.Connected ||
            s_State == MVXRSDKState.Disconnected;

        /// <summary>是否已成功连接中控（含 WS 已握手 + 登录成功 + 房间已分配）。</summary>
        public static bool IsConnected => s_State == MVXRSDKState.Connected;

        /// <summary>是否正在执行 InitMVXRSDK 的本地阶段（含 Initializing）。</summary>
        public static bool IsInitializing => s_State == MVXRSDKState.Initializing;

        /// <summary>状态变迁内部入口。每次成功转换都打 Info 日志便于观测。</summary>
        internal static void SetState(MVXRSDKState next)
        {
            if (s_State == next) return;
            MVXRSDKLog.Info($"MVXRSDK.State: {s_State} → {next}");
            s_State = next;
        }

        // ============================== Bootstrap ==============================

        /// <summary>
        /// 创建 MVXRSDKManager GO + MonoSystem MonoBehaviour 骨架。幂等。
        /// 由 RuntimeInitializeOnLoadMethod 启动钩子自动调用；EditMode 测试可在
        /// [OneTimeSetUp] 显式调用以避开 RuntimeInit 不在测试上下文触发的问题。
        /// 注意：本方法只搭骨架，不初始化 System 层——System 由 InitMVXRSDK 拉起。
        /// </summary>
        internal static void EnsureBootstrap()
        {
            if (MVXRSDKManager != null) return;
            GameObject root = new GameObject("MVXRSDKManager");
            // 顺序关键：MonoSystem 必须先于 MVXRSDKManager 添加。
            // AddComponent 同步触发组件 Awake；MVXRSDKManager.Awake 内会调 *Manager.Start()，
            // 任何 Start 内若 (未来) 调到 MonoSystem.AddUpdateListener，需要 instance 已绑定。
            root.AddComponent<MonoSystem>();
            root.AddComponent<MVXRSDKManager>();
            MVXRSDKManager = root.transform;
            MVXRSDKLog.Info("MVXRSDK bootstrap done");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            EnsureBootstrap();
        }

        // ============================== System 层初始化 ==============================

        private static void InitSystems()
        {
            // MonoSystem 的 instance 由 MonoBehaviour 自身 Awake 自绑定（见 MonoSystem.Awake），
            // 不需要也不应该在这里显式 Init——避免造成"业务可重入 Init"与"组件挂载"两条独立时序
            SocketSystem.Init();
            EventSystem.Init();
            PoolSystem.Init();
            SetRoomAllocationStatus(RoomAllocationStatus.Undistributed);
        }

        // ============================== InitMVXRSDK ==============================

        /// <summary>
        /// 初始化 SDK（兼容旧签名，等价 InitMVXRSDK(deviceId, InitMode.Production)）。
        /// </summary>
        public static void InitMVXRSDK(string deviceId)
        {
            InitMVXRSDK(deviceId, InitMode.Production, null);
        }

        /// <summary>
        /// 初始化 SDK，指定启动模式。
        /// - Production：本地 HTTP(localhost:8868) → 拉中控地址 → 轮询房间分配 → 连房间 WS → 登录（业务方默认）。
        /// - WsDirect：跳过 localhost:8868 这一步，外部直接传入 <paramref name="controlServerAddress"/>（中控服地址，例如 "http://192.168.1.50:7015"）；
        ///   仍走房间分配轮询 + 房间 WS 连接 + 登录完整链路。用于无 localhost 中控环境下测试。
        ///   注意：传入的是中控服地址，不是房间服 WS 地址；房间服 WS 地址由中控轮询响应中分配返回。
        /// - Offline：完全离线，只装配本地 Manager（测试推流/节点等本地能力）。
        ///
        /// 状态机：调用进入 Initializing，本地阶段完成后切 LocalReady；Offline 终态停在 LocalReady，
        /// Production/WsDirect 网络阶段成功后由 RoomManager 切到 Connecting → Connected。
        /// </summary>
        /// <exception cref="ArgumentException">deviceId 为空或非法字符。</exception>
        public static void InitMVXRSDK(string deviceId, InitMode mode, string controlServerAddress = null)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                throw new ArgumentException("deviceId 不能为空或纯空白字符", nameof(deviceId));
            if (deviceId.Length > 64 || deviceId.IndexOfAny(new[] { ' ', '/', '?', '#' }) >= 0)
                throw new ArgumentException($"deviceId 含非法字符或超长（>64）: '{deviceId}'", nameof(deviceId));

            if (s_State == MVXRSDKState.LocalReady || s_State == MVXRSDKState.Connecting ||
                s_State == MVXRSDKState.Connected || s_State == MVXRSDKState.Disconnected)
            {
                MVXRSDKLog.Warning($"SDK 已初始化（State={s_State}），无需重复初始化");
                return;
            }
            if (s_State == MVXRSDKState.Initializing)
            {
                MVXRSDKLog.Warning("SDK 初始化正在进行中，无需重复初始化");
                return;
            }

            DeviceId = deviceId;
            SetState(MVXRSDKState.Initializing);

            // System 层每次 Init 都重新拉起：bootstrap 只搭骨架，
            // 二次 Init（UnInit 后）也能从干净状态开始
            InitSystems();

            // 本地阶段：三种模式都执行，不依赖网络结果
            InitLocalManagers();
            SetState(MVXRSDKState.LocalReady);

            // 网络阶段：按 mode 分发
            switch (mode)
            {
                case InitMode.Production:
                    SetState(MVXRSDKState.Connecting);
                    StartNetworkProduction();
                    break;
                case InitMode.WsDirect:
                    SetState(MVXRSDKState.Connecting);
                    StartNetworkWsDirect(controlServerAddress);
                    break;
                case InitMode.Offline:
                    MVXRSDKLog.Info("MVXRSDK: Offline 模式启动，跳过网络阶段（停留 LocalReady）");
                    break;
            }
        }

        /// <summary>
        /// 本地阶段：装配所有不依赖网络的 Manager / HUD。
        /// 三种启动模式的公共本地阶段，由 InitMVXRSDK 入口同步调用，不依赖网络结果。
        /// </summary>
        private static void InitLocalManagers()
        {
            NetworkFailureHUD.Init();
            BusinessManager.InitSDK();
            NetworkTransformManager.InitSDK();
            SpaceManager.InitSDK();
            StreamManager.InitSDK();
        }

        /// <summary>
        /// Production 路径：本地 HTTP 拉 WS 地址 → 连 WS → 登录。
        /// 本地阶段已在 InitMVXRSDK 入口前移完成，这里只处理网络结果。
        /// </summary>
        private static void StartNetworkProduction()
        {
            RoomManager.StartByHttpDirectory((bool isResult, string baseUrl) =>
            {
                BaseUrl = baseUrl;
                if (!isResult)
                {
                    MVXRSDKLog.Warning("MVXRSDK: Production 网络阶段失败，本地 Manager 已就绪但未连 WS（回到 LocalReady）");
                    if (s_State == MVXRSDKState.Connecting) SetState(MVXRSDKState.LocalReady);
                }
                // 成功路径：onLoginResponse 中由 RoomManager 切 Connected，这里不动
            });
        }

        /// <summary>
        /// WsDirect 路径：跳过 localhost:8868 HTTP 拉中控地址那一步，外部直接传入中控服地址。
        /// 然后走与 Production 相同的房间分配轮询 + 房间 WS 连接 + 登录链路。
        /// </summary>
        private static void StartNetworkWsDirect(string controlServerAddress)
        {
            if (string.IsNullOrEmpty(controlServerAddress))
            {
                MVXRSDKLog.Error("MVXRSDK: WsDirect 模式必须传入中控服地址 controlServerAddress（例如 http://192.168.1.50:7015）");
                if (s_State == MVXRSDKState.Connecting) SetState(MVXRSDKState.LocalReady);
                return;
            }
            RoomManager.StartByWsAddress(controlServerAddress);
        }

        // ============================== UnInitMVXRSDK ==============================

        public static void UnInitMVXRSDK()
        {
            if (s_State == MVXRSDKState.NotInitialized || s_State == MVXRSDKState.Disposed)
            {
                MVXRSDKLog.Warning($"MVXRSDK.UnInitMVXRSDK: SDK 未初始化（State={s_State}），忽略");
                return;
            }

            // 1) 业务层 Manager 反向卸载（生命周期对称）
            RoomManager.UnInitSDK();
            BusinessManager.UnInitSDK();
            NetworkTransformManager.UnInitSDK();
            SpaceManager.UnInitSDK();
            StreamManager.UnInitSDK();

            // 2) MVXRSDK 持有的资源
            UnRegisterAllRootNodes();
            UnRegisterXROffsetNode();
            if (m_SelfTransform != null) UnRegisterSelfNode();
            PoolSystem.ClearAll();
            NetworkFailureHUD.UnInit();
            ClearTransactionVerificationSubscribers();
            ClearStreamEventSubscribers();

            // 3) 系统层 reset（必须在 Manager UnInit 之后——Manager 内部还要走最后一次订阅取消）
            // 严格对称：与 InitSystems 内 4 个 *.Init() 一一对应，全部释放后下次 Init 可干净重建
            PoolSystem.ResetForUnInit();
            EventSystem.ResetForUnInit();
            SocketSystem.ResetAfterUnInit();
            MonoSystem.ResetForUnInit();

            // 4) MVXRSDK 静态字段全部 reset 到初始态，避免二次 Init 残留
            DeviceId = null;
            BaseUrl = null;
            SetRoomAllocationStatus(RoomAllocationStatus.Undistributed);
            IsTransactionVerification = false;
            m_IsBeDoingTransactionVerification = false;

            // 5) State 终态：Disposed → 下次 InitMVXRSDK 走 NotInitialized 路径
            SetState(MVXRSDKState.Disposed);
            s_State = MVXRSDKState.NotInitialized;
        }

        // ============================== 积分扣除 ==============================

        /// <summary>
        /// 若当前项目不接入中控房间系统时，需要手动调用该方法进行积分交易验证。
        /// </summary>
        /// <param name="cb">验证结果回调，参数为验证是否通过，通过验证即可开始游戏</param>
        public static void TransactionVerification(Action<bool> cb)
        {
            if (IsTransactionVerification)
            {
                MVXRSDKLog.Warning($" 积分验证已通过，无需重复验证");
                return;
            }

            if (string.IsNullOrEmpty(BaseUrl))
            {
                MVXRSDKLog.Warning("TransactionVerification: BaseUrl 为空，HTTP 初始化未完成或当前为 WsDirect/Offline 模式，无法发起积分扣除请求");
                cb?.Invoke(false);
                return;
            }

            MVXRSDKLog.Info($"TransactionVerification->单机验证:开始积分验证");

            if (m_IsBeDoingTransactionVerification)
            {
                MVXRSDKLog.Warning($"积分验证正在进行中，无需重复验证");
                return;
            }
            m_IsBeDoingTransactionVerification = true;

            BusinessManager.TransactionVerification((bool isResult) =>
            {
                m_IsBeDoingTransactionVerification = false;
                IsTransactionVerification = isResult;
                cb?.Invoke(isResult);
            });
        }
    }
}
