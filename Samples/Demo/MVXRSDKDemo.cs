using System;
using MyVerseXRSDK;
using MyVerseXRSDK.Streaming;
using UnityEngine;

/// <summary>
/// MVXRSDK 全模块联合示例（对外测试场景）。
///
/// 覆盖功能（每个都在 ContextMenu / 按键里有对应入口）：
/// 1. 节点注册：XR Offset / Self（玩家相机）/ 多 Root —— Awake 自动注册
/// 2. SDK 启动：Production（走真实中控）/ WsDirect（跳过 localhost 中控，外部传中控服地址）
/// 3. 状态机查询：MVXRSDK.State / IsReady / IsConnected —— Update 周期打印
/// 4. 积分扣除：自助模式 TransactionVerification 主动调，订阅模式监听 OnTransactionVerification
/// 5. 推流（MVXRStreamRig 装配音频+配置）；
/// 6. 切镜：SendDirectorRequest(opts, camera) 自动接源
/// 7. 录屏：StartRecord 透传给中控；结果走 OnRecordResult；SDK 不做实际录制，server 到期自动停
/// 8. 全局错误监听：OnError 一个订阅接全部失败路径（推流/录屏/Socket/HTTP/积分）
/// 9. 反初始化：UnInitMVXRSDK 释放全部资源，可二次 Init
/// 10. Editor Debug：Debug_SimulateNotifyLive 在 Editor 内绕过 WS 仿真推流通知（Offline / WsDirect 都能跑）
///
/// 挂法：同 GameObject 上同时挂本组件 + <see cref="MVXRStreamRig"/>。
/// Inspector 字段：xrOffsetNode / selfNode / rootNodes 拖入场景对应节点；
/// 直播相机拖入本组件的 directorCamera（SendDirectorRequest 自动接源用）；
/// Rig 仅配音频源（gameAudioListener）+ StreamConfigAsset，不再持有任何相机。
/// </summary>
[RequireComponent(typeof(MVXRStreamRig))]
public sealed class MVXRSDKDemo : MonoBehaviour
{
    // ============================== Inspector 配置 ==============================

    [Header("SDK 启动配置")]
    [Tooltip("启动模式：\n" +
             "- Production：本地 localhost:8868 拉中控地址 → 房间分配 → WS 登录\n" +
             "- WsDirect：跳过 localhost:8868，外部直接传中控服地址（仍走房间分配 + WS 登录）")]
    public InitMode initMode = InitMode.WsDirect;

    [Tooltip("设备 SN。中控用这个识别本机；切镜回包 deviceId 必须等于本字段才会真切。\n" +
             "v2 入参校验：不可空、不可超 64 字符、不含 ' ' '/' '?' '#'。")]
    public string deviceId = "DEMO-DEVICE-001";

    [Tooltip("中控服地址（WsDirect 模式必填，例如 http://192.168.1.50:7015）。\n" +
             "传的是中控服地址，不是房间服 WS 地址——房间 WS 地址仍由中控轮询响应分配。\n" +
             "Production 模式忽略此字段。")]
    public string controlServerAddress = "http://192.168.1.50:7015";

    [Tooltip("勾选则 Start 自动 Init；否则手动按 I 键 / ContextMenu Init。")]
    public bool initOnStart = true;

    [Header("节点（Awake 自动注册；任意时机可调，未注册不报错只静默不启用功能）")]
    [Tooltip("XR 偏移节点（XR Origin / Camera Offset）。SDK 用它做坐标变换基准。")]
    public Transform xrOffsetNode;

    [Tooltip("自身节点（玩家相机 Transform，通常是 XR Rig 下的 Main Camera）。\n" +
             "v2 起 SDK 不再自动抓 Camera.main，必须主动注册。\n" +
             "未注册时本机位姿不上报、HUD 不显示、远近判定退化。")]
    public Transform selfNode;

    [Tooltip("场景根节点（支持多个），接收服务端推送的位置/旋转偏移。\n" +
             "重复同节点 / null 入参幂等忽略；外部 Destroy 后 SDK 下次推送时自动从列表移除。")]
    public Transform[] rootNodes;

    [Header("推流装配（拖同 GO 上的 MVXRStreamRig）")]
    [Tooltip("MVXRStreamRig 引用。游戏音 / StreamConfig 配在 Rig 自己的 Inspector；\n" +
             "画面源管理由业务侧直接调 MVXRSDK API（本组件演示 SendDirectorRequest 自动接源）。")]
    public MVXRStreamRig streamRig;

    [Header("录屏（按 R / ContextMenu）")]
    [Tooltip("录屏时长（秒），到时由 server 自动停止。当前 SDK 无 StopRecord 接口。")]
    public int recordDurationSec = 10;

    [Tooltip("录屏文件名前缀；实际文件名会拼时间戳保证唯一。")]
    public string recordFileNamePrefix = "mvxrsdk_demo";

    [Tooltip("true=真实摄像头(用 CameraId)；false=Pico 设备(用 deviceId)。一般留 false。")]
    public bool recordRealCamera = false;

    [Tooltip("RealCamera=true 时使用的 CameraId。")]
    public string recordCameraId = "";

    [Header("切镜（按 D 走中控真链路）")]
    [Tooltip("被选中后要推的相机。URP Render Type=Base；enabled 默认 false。")]
    public Camera directorCamera;

    [Tooltip("机位来源（DirectorRequestOptions.Source，原样透传给中控）：\n" +
             "- 留空：自动接源重载会自动填 \"unity\"（传了相机即明确本机机位）\n" +
             "- \"unity\"：本机 Unity 游戏内机位（推哪个相机是本地决策，中控不感知）\n" +
             "- \"mr\"：请求切回原直播（播控第一视角），此时本机不会被选中接源")]
    public string directorSource = "";

    [Tooltip("镜头数（透传给中控）：1=单镜头 / 2=双拼 / 3=品字 / 4=2x2。<1 按 1 处理。")]
    public int directorLenses = 1;

    [Tooltip("切镜持续秒数（透传给中控），必须 > 0；到期由服务端停流，客户端不做本地倒计时。")]
    public int directorDurationSec = 5;

    [Tooltip("是否录制这一段切镜画面（DirectorRequestOptions.Record，服务端执行录制）。")]
    public bool directorRecord = false;

    [Header("积分扣除（按 T / ContextMenu）")]
    [Tooltip("自助模式：主动调 TransactionVerification（需中控启动模式 + BaseUrl 非空，否则立即 fail）。\n" +
             "订阅模式：监听 OnTransactionVerification 等中控触发回调。\n" +
             "二选一即可——两条路径独立但都会触发同一份业务逻辑。")]
    public bool autoVerifyOnConnected = false;

    [Header("状态打印")]
    [Tooltip("0 表示不周期打；>0 则每 N 秒打一次 State / IsReady / IsConnected。")]
    public float printStatePeriodSec = 5f;

    [Header("按键映射")]
    public KeyCode initKey = KeyCode.I;
    public KeyCode uninitKey = KeyCode.U;
    public KeyCode transactionKey = KeyCode.T;
    public KeyCode recordKey = KeyCode.R;
    public KeyCode directorRealKey = KeyCode.D;
    public KeyCode simulateLiveStartKey = KeyCode.K;
    public KeyCode simulateLiveStopKey = KeyCode.S;
    public KeyCode hotSwapXROffsetKey = KeyCode.X;
    public KeyCode unRegisterSelfKey = KeyCode.Y;

    [Header("热替换/注销演示（用作 X / Y 键的目标）")]
    [Tooltip("按 X 时把 XR Offset Node 热替换为这个节点（演示 v2 任意时机注册 + 热替换）。")]
    public Transform xrOffsetNodeAlt;

    // ============================== 私有状态 ==============================

    private bool m_RecordRequestPending;
    private float m_NextStatePrintTime;
    private bool m_TransactionDoneOnce;

    // ============================== 生命周期 ==============================

    private void Awake()
    {
        // 节点注册：v2 任意时机可调，这里早早地把已知节点都注册上。
        // 未注册时 SDK 内部读取节点的代码（SpaceObstacles / NetworkTransform / NetworkFailureHUD）
        // 都做了 null 检查，对应功能静默不启用，不会报错。
        if (xrOffsetNode != null) MVXRSDK.RegisterXROffsetNode(xrOffsetNode);
        if (selfNode     != null) MVXRSDK.RegisterSelfNode(selfNode);
        if (rootNodes != null)
        {
            foreach (var n in rootNodes)
            {
                if (n != null) MVXRSDK.RegisterRootNode(n);
            }
        }
    }

    private void Start()
    {
        if (streamRig == null) streamRig = GetComponent<MVXRStreamRig>();

        // 订阅 SDK 事件（必须配对 OnDestroy 反订阅，否则 UnInit + 再 Init 会重复触发回调）
        MVXRSDK.OnTransactionVerification += OnTransactionVerification;
        MVXRSDK.OnPushStreamStarted       += OnPushStreamStarted;
        MVXRSDK.OnPushStreamStopped       += OnPushStreamStopped;
        MVXRSDK.OnPushStreamFailed        += OnPushStreamFailed;
        MVXRSDK.OnPushStreamStats         += OnPushStreamStats;
        MVXRSDK.OnRecordResult            += OnRecordResult;
        MVXRSDK.OnDirectorSelected        += OnDirectorSelected;
        MVXRSDK.OnError                   += OnError;

        // v3：Rig 不再暴露画面事件（OnSwitched/OnRestored/Ready 已移除）

        if (initOnStart) DoInit();
    }

    private void OnDestroy()
    {
        // 反订阅 SDK 事件
        MVXRSDK.OnTransactionVerification -= OnTransactionVerification;
        MVXRSDK.OnPushStreamStarted       -= OnPushStreamStarted;
        MVXRSDK.OnPushStreamStopped       -= OnPushStreamStopped;
        MVXRSDK.OnPushStreamFailed        -= OnPushStreamFailed;
        MVXRSDK.OnPushStreamStats         -= OnPushStreamStats;
        MVXRSDK.OnRecordResult            -= OnRecordResult;
        MVXRSDK.OnDirectorSelected        -= OnDirectorSelected;
        MVXRSDK.OnError                   -= OnError;

        // v3：Rig 不再暴露画面事件，无需反订阅
    }

    private void Update()
    {
        // 按键触发
        if (Input.GetKeyDown(initKey))               DoInit();
        if (Input.GetKeyDown(uninitKey))             DoUnInit();
        if (Input.GetKeyDown(transactionKey))        DoTransactionVerification();
        if (Input.GetKeyDown(recordKey))             DoRecord();
        if (Input.GetKeyDown(directorRealKey))       DoDirectorReal();
        if (Input.GetKeyDown(simulateLiveStartKey))  DoSimulateLive(true);
        if (Input.GetKeyDown(simulateLiveStopKey))   DoSimulateLive(false);
        if (Input.GetKeyDown(hotSwapXROffsetKey))    DoHotSwapXROffset();
        if (Input.GetKeyDown(unRegisterSelfKey))     DoUnRegisterSelf();

        // 状态机周期打印
        if (printStatePeriodSec > 0f && Time.unscaledTime >= m_NextStatePrintTime)
        {
            Debug.Log($"[MVXRSDKDemo] State={MVXRSDK.State} IsReady={MVXRSDK.IsReady} IsConnected={MVXRSDK.IsConnected} IsStreaming={MVXRSDK.IsStreaming}");
            m_NextStatePrintTime = Time.unscaledTime + printStatePeriodSec;
        }

        // 自助积分验证（仅 Production 路径有 BaseUrl，WsDirect/Offline 立即 fail）
        if (autoVerifyOnConnected && !m_TransactionDoneOnce && MVXRSDK.IsConnected)
        {
            m_TransactionDoneOnce = true;
            DoTransactionVerification();
        }
    }

    // ============================== SDK 启动 ==============================

    [ContextMenu("Init SDK")]
    public void DoInit()
    {
        if (MVXRSDK.IsInitializing || MVXRSDK.IsReady)
        {
            Debug.LogWarning($"[MVXRSDKDemo] DoInit: SDK 已 Init（State={MVXRSDK.State}），忽略");
            return;
        }

        switch (initMode)
        {
            case InitMode.Production:
                // 走本地中控 localhost:8868：业务方真实接入路径
                MVXRSDK.InitMVXRSDK(deviceId);
                Debug.Log($"[MVXRSDKDemo] InitMVXRSDK(Production) deviceId={deviceId}");
                break;

            case InitMode.WsDirect:
                // 跳过 localhost:8868，外部直接传中控服地址
                if (string.IsNullOrEmpty(controlServerAddress))
                {
                    Debug.LogError("[MVXRSDKDemo] WsDirect 必填 controlServerAddress（中控服地址），已中止");
                    return;
                }
                MVXRSDK.InitMVXRSDK(deviceId, InitMode.WsDirect, controlServerAddress);
                Debug.Log($"[MVXRSDKDemo] InitMVXRSDK(WsDirect) deviceId={deviceId} controlServer={controlServerAddress}");
                break;

            case InitMode.Offline:
                // 完全离线，仅本地模块跑通；本 Demo 默认不演示这一档（Inspector 默认 WsDirect）
                MVXRSDK.InitMVXRSDK(deviceId, InitMode.Offline);
                Debug.Log($"[MVXRSDKDemo] InitMVXRSDK(Offline) deviceId={deviceId}");
                break;
        }
    }

    [ContextMenu("UnInit SDK")]
    public void DoUnInit()
    {
        // 反初始化：Manager 反向卸载 → System reset → 静态字段全清；可再次 Init
        // 注意：UnInit 不会自动反订阅 OnPushStream* 等业务事件，由本组件 OnDestroy 做
        MVXRSDK.UnInitMVXRSDK();
        m_TransactionDoneOnce = false;
        Debug.Log("[MVXRSDKDemo] UnInitMVXRSDK 完成");
    }

    // ============================== 积分扣除 ==============================

    [ContextMenu("Transaction Verification (自助)")]
    public void DoTransactionVerification()
    {
        // 自助模式：主动调，cb 回调结果。
        // Production 模式有 BaseUrl，发起 HTTP 请求；WsDirect / Offline 模式 BaseUrl 空，立即 cb(false) + Warning。
        // 订阅模式（中控启动）由中控触发 OnTransactionVerification 事件——和本调用相互独立。
        MVXRSDK.TransactionVerification(success =>
        {
            if (success)
            {
                Debug.Log("[MVXRSDKDemo] 积分扣除验证成功（自助模式）—— 开始游戏");
            }
            else
            {
                Debug.LogError("[MVXRSDKDemo] 积分扣除验证失败（自助模式）—— 检查 BaseUrl / 包名 / 账户积分");
            }
        });
    }

    // 中控启动模式：积分由中控触发，订阅事件接结果
    private void OnTransactionVerification(bool success)
    {
        if (success) Debug.Log("[MVXRSDKDemo] 积分扣除验证成功（订阅模式，中控触发）");
        else         Debug.LogError("[MVXRSDKDemo] 积分扣除验证失败（订阅模式，中控触发）");
    }

    // ============================== 录屏 ==============================

    [ContextMenu("Start Record")]
    public void DoRecord()
    {
        if (m_RecordRequestPending)
        {
            Debug.LogWarning("[MVXRSDKDemo] 已有进行中的录屏请求未应答，忽略");
            return;
        }
        if (!MVXRSDK.IsConnected)
        {
            Debug.LogWarning("[MVXRSDKDemo] WS 未连通，StartRecord 会得到 RecordNotConnected");
        }

        string fileName = $"{recordFileNamePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}";
        var opts = new StartRecordOptions
        {
            RealCamera   = recordRealCamera,
            CameraId     = recordCameraId,
            DurationSec  = recordDurationSec,
            FileName     = fileName,
            PicoDeviceId = deviceId,   // pb 字段名沿用历史命名 PicoDeviceId，但传入 v2 后的 deviceId
        };
        m_RecordRequestPending = true;
        MVXRSDK.StartRecord(opts);
        Debug.Log($"[MVXRSDKDemo] StartRecord 已发起 fileName={fileName} duration={recordDurationSec}s realCamera={recordRealCamera}");
    }

    private void OnRecordResult(MVXRSDKErrorCode code, string errMsg)
    {
        m_RecordRequestPending = false;
        if (code == MVXRSDKErrorCode.Ok)
            Debug.Log("[MVXRSDKDemo] OnRecordResult: 已被服务端接受，到期自动停");
        else
            Debug.LogError($"[MVXRSDKDemo] OnRecordResult 失败 code={code}({(int)code}) msg={errMsg}");
    }

    // ============================== 切镜（真链路 & 本地直切） ==============================

    [ContextMenu("Switch Camera (真链路：发给中控仲裁，自动接源)")]
    public void DoDirectorReal()
    {
        // 自动接源重载：被选中（NotifyLive start）后 SDK 自动把 directorCamera 接为画面源，
        // 停流自动清；受理结果走 OnDirectorRequestResult
        MVXRSDK.SendDirectorRequest(new DirectorRequestOptions
        {
            Source      = directorSource,    // 留空时本重载自动填 DirectorSource.Unity
            Lenses      = directorLenses,
            DurationSec = directorDurationSec,
            Record      = directorRecord,
        }, directorCamera);
        Debug.Log($"[MVXRSDKDemo] SendDirectorRequest(auto) camera={directorCamera?.name} " +
                  $"source={(string.IsNullOrEmpty(directorSource) ? "(auto→unity)" : directorSource)} " +
                  $"lenses={directorLenses} duration={directorDurationSec}s record={directorRecord}");
    }

    private void OnDirectorSelected(string deviceId, bool isPrimary, int slot, int durationSec)
    {
        // v3：被选中信号 = NotifyLive（OnPushStreamStarting），本事件仅透传日志
        Debug.Log($"[MVXRSDKDemo] OnDirectorSelected deviceId={deviceId} isPrimary={isPrimary} slot={slot} duration={durationSec}s");
    }

    // ============================== 推流状态事件 ==============================

    private void OnPushStreamStarted(string streamServerIp)
    {
        Debug.Log($"[MVXRSDKDemo] >>> OnPushStreamStarted ip={streamServerIp} url={MVXRSDK.CurrentStreamUrl}");
    }

    private void OnPushStreamStopped(StreamStopReason reason)
    {
        // reason: ServerStop / UserStop / NetworkLost / ConfigChanged
        Debug.Log($"[MVXRSDKDemo] <<< OnPushStreamStopped reason={reason}");
    }

    private void OnPushStreamFailed(MVXRSDKErrorCode code, string msg)
    {
        Debug.LogError($"[MVXRSDKDemo] !!! OnPushStreamFailed code={code}({(int)code}) msg={msg}");
    }

    private void OnPushStreamStats(StreamStats stats)
    {
        // SDK 每 ~1s 推一次 StreamStats（含码率、丢包、RTT 等）；这里只在 DEBUG 下偶尔打
        // 业务侧可订阅做 HUD 可视化或上报埋点
    }

    // ============================== 全局错误监听 ==============================

    private void OnError(MVXRSDKErrorCode code, string msg, string sourceModule)
    {
        Debug.LogError($"[MVXRSDKDemo] [OnError] {sourceModule} → {code}({(int)code}) {msg}");
    }

    // ============================== Editor Debug 仿真 ==============================

    [ContextMenu("Debug Simulate NotifyLive (start)")]
    public void DoSimulateLiveStart() => DoSimulateLive(true);

    [ContextMenu("Debug Simulate NotifyLive (stop)")]
    public void DoSimulateLiveStop()  => DoSimulateLive(false);

    /// <summary>仿真服务端 NotifyLive，绕过 WS，Editor / Offline 模式专用。</summary>
    public void DoSimulateLive(bool start)
    {
        // Editor 内最快验证推流链路：无需 WS 真实推送
        MVXRSDK.Debug_SimulateNotifyLive(start ? "127.0.0.1" : "", start);
        Debug.Log($"[MVXRSDKDemo] Debug_SimulateNotifyLive(start={start}) 已触发");
    }

    // ============================== 节点热替换演示 ==============================

    [ContextMenu("Hot-swap XR Offset Node")]
    public void DoHotSwapXROffset()
    {
        // 演示 v2 任意时机注册 + 同 API 热替换：再次调 RegisterXROffsetNode 即生效
        // 已存在障碍物 / 远端玩家会被清空 + 在新 parent 下按 Store 缓存重建（数据/表现分层架构）
        if (xrOffsetNodeAlt == null)
        {
            Debug.LogWarning("[MVXRSDKDemo] xrOffsetNodeAlt 未配置，跳过热替换");
            return;
        }
        MVXRSDK.RegisterXROffsetNode(xrOffsetNodeAlt);
        Debug.Log($"[MVXRSDKDemo] RegisterXROffsetNode 热替换为 {xrOffsetNodeAlt.name}");
    }

    [ContextMenu("UnRegister Self Node")]
    public void DoUnRegisterSelf()
    {
        // 演示自身节点运行时注销：本机位姿停止上报（NetworkTransform.messageType → None），
        // NetworkFailureHUD / 远近判定 / 障碍物距离检测 全部退化为静默不启用
        MVXRSDK.UnRegisterSelfNode();
        Debug.Log("[MVXRSDKDemo] UnRegisterSelfNode 完成；本机不再上报位姿");
    }
}
