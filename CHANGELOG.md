# Changelog

本文件记录 MyVerse XR SDK 所有版本的变更。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

---

## [3.0.3] - 2026-06-25

### Fixed
- 主工程导入时一批**资源**（角色模型 / 障碍 / shader / pbLib DLL）GUID 冲突被 Unity 忽略：这些资源同样是从主工程复制进包、沿用了相同 `.meta` GUID。其中角色 / 障碍 prefab 被忽略会让 SDK `Resources.LoadAsync<GameObject>`（`ResSystem.cs`）**运行时拿到 null → 虚影 / 障碍显示失败**（DLL/shader 仅 Warning）。修复：给包内 `Runtime/Resources` + `Runtime/Plugins` 全部资源换发全新唯一 GUID，并同步 remap 包内引用链（prefab→mat→texture/shader/fbx，零残留校验），让包资源 GUID 与主工程彻底独立、互不影响

---

## [3.0.2] - 2026-06-25

### Fixed
- 包导入到（从其复制源码的）主工程时全量 GUID 冲突、类型整体编译失败：`Pool` / `Http` / `Socket` / `Res` 等脚本当初是从主工程**复制**进包、沿用了相同 `.meta` GUID，与主工程残留的同名脚本撞 GUID，Unity 静默丢弃包内副本 → 整个 `MVXRSDK.asmdef` 缺类型、报一片 `CS0234/CS0246`。给这批脚本（`GameObjectPoolModule`/`GameObjectPoolData`/`ObjectPoolModule`/`ObjectPoolData`/`HttpCallBackArgs`/`HttpSystem`/`ResSystem`/`SocketSystem`）的 `.meta` 换发全新唯一 GUID

### Changed
- **边界收缩**：把不面向业务的内部基础设施类型从 `public` 收为 `internal`（兑现 3.0.0 文档已预告的收敛）——`PoolSystem` 及对象池族（`GameObjectPoolModule`/`GameObjectPoolData`/`ObjectPoolModule`/`ObjectPoolData`）、`SocketSystem`、`SocketModule`/`MessageSendData`/`MessageReciveData`、内置 WebSocket 协议栈（`IWebSocket`/`WebSocket`/`*EventArgs`/`CloseStatusCode`/`Opcode`/`WebSocketState`）、`GameAudioStreamCapture`。业务连接状态查询统一用 `MVXRSDK.IsConnected`（测试经 `InternalsVisibleTo` 不受影响；`Samples/Demo` 不引用这些类型）

---

## [3.0.1] - 2026-06-25

### Fixed
- 包导入到第三方工程编译失败（一片 `CS0234`/`CS0246`，遍布 Pool/Http/Event/Mono）：`GameObjectPoolModule.cs` / `PoolSystem.cs` 残留未使用的 `using Unity.Collections;`，而 `package.json` 未声明 `com.unity.collections`（本机靠 URP 14 传递依赖才让该命名空间可见）。第三方工程该命名空间不可见时，这两个文件 `using` 解析失败 → 文件编译失败 → 连带整个 `MVXRSDK.asmdef` 程序集垮掉，程序集内所有类型（含没有该 using 的模块）全部报"不存在"。删除冗余 using，SDK 对 `com.unity.collections` 零依赖

---

## [3.0.0] - 2026-06-25 - 切镜化推流重构

### Breaking Changes
- `SendDirectorRequest(int, int)` 删除，改为 `SendDirectorRequest(DirectorRequestOptions)` 与自动接源重载 `SendDirectorRequest(DirectorRequestOptions, Camera)`（DirectorInsert 新增 source/record 字段）
- `MVXRStreamRig` 不再承载画面推流：`mainCamera`/`directorCameras`/`SwitchCameraTemporary`/`RestoreOriginalCamera`/`Ready`/`OnSwitched`/`OnRestored`/`StreamTexture` 删除，仅保留音频采集与 StreamConfigAsset 应用
- `CameraStreamSource(Camera)` / `RenderTextureStreamSource(RenderTexture)` 构造去宽高参数（InternalRT 固定尺寸）
- 删除 `CameraStreamCapture`（主相机/第一视角推流由播控承担）
- 推流不再包含麦克风语音：删除 `MVXRSDK.PushMicPcm`、`MicrophoneStreamCapture`、`MVXRStreamRig` 的 `captureMicrophone`/`micSampleRate`/`micDevice` 字段；`AudioMixingSystem` 去掉 mic 一路（推流音频仅游戏音）

### Added
- `DirectorSource` 常量（`"unity"`=本机 Unity 游戏内机位；空/`"mr"`=原直播）与 `DirectorRequestOptions`（Source/Lenses/DurationSec/Record/FileName 五字段；`FileName` 录制文件名，仅 `Record=true` 时有意义，SDK 原样透传不校验）
- `OnDirectorRequestResult`：DirectorInsert 受理结果（受理 ≠ 被选中，被选中以 NotifyLive 为准，即 `OnPushStreamStarting`）
- `OnPushStreamStarting`：会话开始建立（被选中信号），业务在此接源
- 推流无源启动（推黑帧等待接源）；一相机推流保护（推流中新 `SetStreamSource` 请求丢弃，Warning 日志）
- 相机销毁自保护：attach 中相机被销毁自动清源推黑帧 + Warning
- 接入 `logic.UpdateDeviceOnlineStatus` 协议：登录成功（含重连重登录）上报 online=true；退出房间（UnInit / 房间解散 / 应用退出兜底）上报 online=false
- `MVXRSDK.SetSyncSameRoomAvatar(bool enable, float displayDistanceMeters = 2f)` / `IsSyncSameRoomAvatar` / `SameRoomAvatarDistance`：开关是否同步"同房间（本房间）其他玩家虚影"，默认 false（不同步）。虚影显示距离规则：本机自己一律不显示（按 `DeviceId` 过滤）、其他房间虚影固定 2m、同房间虚影显示距离由 `displayDistanceMeters` 外部传参（默认 2m、仅开启时生效）。开启后本房间位置推送创建虚影，关闭立即回收本房间虚影且不影响非本房间虚影（`RoleSnapshot.RoomId` 维度支撑按房间精确回收，`NetworkTransform` 按实例持有显示距离）

### Changed
- InternalRT 固定尺寸（`StreamConfig.StreamMaxLongSide` 按 16:9，默认 1280×720），任意源可热切；`ClearStreamSource` 清黑保留 RT，`Dispose` 才释放
- `StreamConfig.StreamMaxLongSide` 语义更新为 InternalRT 长边（原语义：推流画面长边上限）
- `PushGameAudioPcm` 采样率放宽：8000–192000 Hz（原 {48000, 44100} 白名单其余抛 `ArgumentException`）；音频工作采样率改为跟随设备输出率（`AudioSettings.outputSampleRate`，Init 时锁定），输入等率直通、异率线性重采样

### Fixed
- WHIP DELETE 必失败：mediamtx 201 响应的 `Location` 头是相对路径，SDK 未拼 base URL 直接发 DELETE（缺 host 连接层失败），重试耗尽后误报"mediamtx 不可达"。现以请求 URL 为 base 按 RFC 3986 解析为绝对 URL（Editor WsDirect 真链路日志实测发现）
- 服务端主动停流（`NotifyLive start=false` → `ServerStop`）后 WHIP DELETE 不再重试：session 已由服务端清理，改为单次 best-effort 通知，未达降为 Info（原先重试 3 次 + Error 告警）
- 设备音频输出非 48kHz 时游戏音推流不可用/失真（PICO 4U 实测输出 24000Hz 必现）：
  - Rig 游戏音采集被采样率白名单拒绝，音频线程每个回调抛 `ArgumentException`（游戏音整体推不出去）
  - 混音缓冲固定 48k 重采样与 `AudioStreamFeeder.SetData` 按设备率声明不一致 → 音调/速度失真；且 ring 写入快于消费 → 周期性丢音。现 feeder 声明率恒等于混音工作率
- `AudioMixingSystem.PushInternal` 在音频线程打 Warning 日志的隐患移除（校验与报错统一收口到 facade `ValidatePcmArgs`）

---

## [2.0.1] - 2026-06-05

### Changed
- `UnRegisterXROffsetNode` 注销时连带注销挂在 XR 子树下的自身节点，清掉内部悬挂引用（Self 独立挂载不受影响）；配合既有的障碍物 / 远端玩家虚影自动清理，适配"切场景销毁整个 XR Origin"用法
- 场景根节点偏移应用时打印更新前后坐标（节点名 + pos/rot 前后值），覆盖服务端推送（`OnOffsetChanged`）与注册时缓存回放（`RegisterSceneRootNode`）两条路径

### Fixed
- `NetworkTransform` 远端角色距离检测读 `MVXRSDK.SelfTransform` 缺 null 检查：自身节点未注册 / 已随 XR 销毁时抛 MissingReferenceException，补判空跳过（对齐 `SpaceObstacles`）
- `SpaceObstaclesModule.ClearAll` 入池前判空，避免障碍物 GO 已随 XR 子树销毁时把已销毁对象推入对象池

---

## [2.0.0] - 2026-05-21

### BREAKING

#### API 重命名
- `MVXRSDK.PicoId` → `MVXRSDK.DeviceId`（SDK 不再绑死 PICO 设备，字段名去 PICO 化）
- `MVXRSDK.InitMVXRSDK(string picoId, ...)` 参数名 → `string deviceId`（按值传，不影响调用）
- 内部 `RoomModule.SetPicoId` → `SetDeviceId`

#### 状态查询接入面重整
- **删除** `internal MVXRSDK.IsInitSdk` / `private m_IsBeDoingInitSDK` —— 由新 `MVXRSDKState` 状态机完全替代
- **删除** `public SocketSystem.IsConnect`（改为 `internal`）—— 业务方走 `MVXRSDK.IsConnected`
- **新增** `public enum MVXRSDKState`：7 态 `NotInitialized / Initializing / LocalReady / Connecting / Connected / Disconnected / Disposed`
- **新增** `public MVXRSDK.State` / `IsReady` / `IsConnected` / `IsInitializing` 公开属性

#### 事件签名重整
- `OnPushStreamFailed(int code, string msg)` → `(MVXRSDKErrorCode code, string msg)`
- `OnPushStreamStopped(bool active)` → `(StreamStopReason reason)` — 4 种原因枚举
- `OnRecordResult(bool success, string errMsg)` → `(MVXRSDKErrorCode code, string errMsg)` —— 成功时 `code == MVXRSDKErrorCode.Ok`
- 内部 `IWebRTCSession.OnFailed` / `PushStreamModule.OnFailed` / `OnStopped` / `RecordModule.OnResult` 跟随
- 删除 `StreamErrorCode static class`（合并到 `MVXRSDKErrorCode` 4xxx 段，数值不变）

#### Init 入参校验
- `MVXRSDK.InitMVXRSDK` 在 `deviceId` 为空、超长 64、含 ` ` `/` `?` `#` 时抛 `ArgumentException`
- `MVXRSDK.PushGameAudioPcm` / `PushMicPcm` 在 `pcm == null` / `sampleRate ∉ {48000, 44100}` / `channels ∉ {1, 2}` 时抛 `ArgumentException`

#### 节点注册顺序约束放宽（推翻旧规则）
- `RegisterXROffsetNode` 任何时机可调用（Init 前/中/后），SDK 实时读取最新值
- 重复传入不同节点视为**热替换**，传入同一节点幂等忽略
- 旧约定「必须在 Init 前调用」**作废**——SDK 内部读取该节点的代码（SpaceObstacles / NetworkTransform）都做了 null 检查，未注册时对应功能静默不启用

#### HTTP/Json 后端切换
- 删除整个 LitJson 库（9 文件 ~50KB），改用 `Newtonsoft.Json`（`com.unity.nuget.newtonsoft-json` 3.0.2+）
- `HttpSystem.SendData` 的 `body` 入参类型由 `Dictionary<string, object>` 改为 `byte[]`——调用方自己负责 POCO 序列化
- 业务方若直接用过 SDK 包内的 `JsonMapper` / `JsonData`（不应该但理论可能），需自行迁移

### Added

#### 状态机 + 可观测性
- `public event Action<MVXRSDKErrorCode, string, string> OnError`：全局错误聚合（code, msg, sourceModule）

#### 启动模式
- `InitMode.Production` / `WsDirect` / `Offline` 三档启动模式
- `MVXRSDK.InitMVXRSDK(deviceId, InitMode, controlServerAddress)` 重载

#### 错误体系
- `public enum MVXRSDKErrorCode`：按域分段 1xxx-7xxx，覆盖通用/网络/房间/推流/录屏/节点/积分
- `public enum StreamStopReason`：4 种推流停止原因（ServerStop / UserStop / NetworkLost / ConfigChanged）

#### API 表面
- `MVXRSDK.SetStreamSource(IStreamSource)`：业务方可自实现 IStreamSource，订阅 OnAttached/OnDetached 生命周期

### Changed
- **生命周期对称重构**：每个 Manager 加 `s_Initialized` 防重入；UnInit 严格四件套清理（Socket 订阅 → EventSystem → MonoSystem Update → 静态缓存）；handler 入口加 `if (!s_Initialized) return` 守卫
- **MVXRSDK.UnInitMVXRSDK 集中清理钩子**：Manager 反向卸载 → MVXRSDK 资源释放 → 系统层 reset（`EventSystem.Clear` / `SocketSystem.ResetAfterUnInit` / `MonoSystem.ResetForUnInit`）→ 静态字段全部 reset
- **Protobuf 反序列化统一兜底**：新增 `SocketSystem.TryParse<T>(buffer, out msg, contextTag)`，10 个调用点全部转。空 buffer 内化为空对象 + 返回 true，调用方不必手写 null 检查
- **MVXRSDKLog 格式化重载短路**：`Debug(format, args)` 等改为 `ShouldLog` 前置短路，避免被级别过滤后仍执行 `string.Format` + params 数组分配
- **SocketModule 协议常量化**：踢线字节 `3` 和消息前缀字节 `2` 抽 `internal const`；重连成功 vs 首次连接日志区分
- **MonoSystem 主线程队列**：加高水位预警（深度 ≥ 128）+ 无锁 CAS 峰值追踪，Diagnostics 可观测
- **去 PICO 绑死（文案）**：README / Documentation / 注释中"必须 PICO"措辞缓和为"推荐 PICO 设备，兼容 OpenXR"。SDK Runtime 不引用任何 PICO 程序集（asmdef 验证）

### Removed
- `Runtime/Scripts/Tool/LitJson/` 整个目录（9 个 .cs，~50KB）
- `Runtime/Scripts/Utils/GZipUtils.cs`（仅 SocketModule.OnMessage 一处调用，内联为私有 `GZipDecompress`）
- `Runtime/Scripts/Tool/CoroutineTool.cs`（全工程零调用）
- `Tests/Runtime/SmokeTest.cs`（占位测试，被 ProductionFlow / WsDirectRecordSwitch 真机场景取代）

### Fixed
- `MVXRSDK.TransactionVerification`：BaseUrl 为空时显式 `cb(false)` + Warning，避免拼出残缺 URL
- `WebRTCSystem.OnFailed` / `ProductionFlowTester` / `StreamManager.StartRecord` 早返回路径跟随 PR-4 错误码签名

### 空间模块重构 (2026-05-20)

#### Changed
- `Operation/Space/` 模块拆分为数据/表现两层：新增 `SpaceStateStore` 持有最近一次推送快照，`SpaceObstaclesModule`/`SpatialAlternationModule` 改为订阅 Store + 监听 XR Offset Node 注册/注销事件，自动按"最新值缓存 + 注册时回放"模式收敛
- XR Offset Node 注册早晚于 WS 推送均能正确收敛；热替换会清空旧节点下障碍物并在新节点重建；注销会清空所有障碍物
- Scene Root Node 注册时立即按 Store 缓存回放偏移（仅作用于新注册节点）

#### Removed
- 内部事件常量 `MVXRSDKEventType.GAME_SCENE_DATA_UPDATE`（已被 `SpaceStateStore` 强类型 event 替代）

#### Fixed
- 修复 XR Offset Node 未注册时，`SpaceObstaclesModule` 错误地 `Instantiate(parent: null)` 导致障碍物落到根场景的退化行为（现改为不创建 GO，等节点注册后回放）

### MVXRSDK.cs partial 拆分 (2026-05-21) [PR-C]

#### Changed (零行为变化，纯结构重组)
- `MVXRSDK.cs` 780 → 308 行；新增 4 个 partial 文件 + 3 个独立 enum 文件：
  - `MVXRSDK.cs` —— Core: Bootstrap / State machine / InitMVXRSDK / UnInitMVXRSDK / TransactionVerification
  - `MVXRSDK.Events.cs` —— 全部 public events（推流/录屏/切镜/积分/全局错误）+ internal Raise 入口 + Clear*Subscribers
  - `MVXRSDK.Nodes.cs` —— XR Offset / Self / Root 三类节点的 Register/UnRegister + 内部事件
  - `MVXRSDK.Stream.cs` —— SetStreamSource / SetStreamConfig / StartRecord / PushXxxPcm / SendDirectorRequest / Debug_SimulateNotifyLive
  - `MVXRSDKErrorCode.cs` / `MVXRSDKState.cs` / `StreamStopReason.cs` —— 三个 enum 各自独立文件

#### 收益
- 每个文件 100-310 行，单一职责，新人能从文件名快速定位到目标 API
- 修改一项时不再与其它修改 conflict（events / nodes / stream API 各自一个文件）
- Editor "Solution Explorer" 树形结构能直接看到 MVXRSDK 类被拆成几块
- `MVXRSDK.cs` Core 文件聚焦 lifecycle，读起来不再被 ~600 行事件/节点/流式 API 噪音冲淡

### EventSystem 4-15 参数死代码删除 (2026-05-21) [PR-B]

#### Removed
- `EventSystem.cs` 删 4-15 参数版 `AddEventListener` / `EventTrigger` / `RemoveEventListener` 共 36 个方法
- `EventModule.cs` 删 4-15 参数版 `EventTrigger` 实现 12 个

#### 保留
- 0-3 参数变体（覆盖现有所有真实使用 + 1 档余量）
- `AddEventListener<TAction>` / `RemoveEventListener<TAction>` 多参泛型路径（基于 `MulticastDelegate` 约束，参数数量无关）

#### 收益
- `EventSystem.cs` 462 → 158 行（-304 行）
- `EventModule.cs` 271 → 155 行（-116 行）
- 顺手解决 P0 #14 line 125 typo 在死代码段里也不再出现
- 文件总行数 ~420 行 → 真正核心 ~310 行；新人 onboarding 时不再被"为什么有 16 路重载"刷屏

#### 实际使用范围审计
- `AddEventListener / RemoveEventListener` 最高 1 泛型参数（仅 `<string>`，1 处）
- `EventTrigger` 最高 2 个参数（推流错误 `(code, msg)`，多处）
- 全 SDK runtime + Tests 零个 4+ 参数调用

### MonoSystem 自绑定 + ResetForUnInit 收敛 (2026-05-21) [PR-A + 时序补丁]

#### Fixed (补丁)
- **EnsureBootstrap AddComponent 顺序：MonoSystem 必须先于 MVXRSDKManager 添加**——
  AddComponent 同步触发 Awake，MVXRSDKManager.Awake 调 *Manager.Start()；当前所有 Start()
  都没调 MonoSystem.AddUpdateListener 所以恰好不踩坑，但顺序反了的话未来在 Start() 加任何
  监听订阅就 NRE。调换顺序后 Manager.Start 阶段 MonoSystem.instance 已稳定绑定
- 新增防御测试 `Bootstrap_OrderEnsuresMonoSystemBoundBeforeManagers` 钉死这个顺序约束



#### Changed
- `MonoSystem.Awake` 自绑定 `static instance`：组件挂载即可用，不再依赖 `MonoSystem.Init()` 调用时机；删除 `EnsureInstance` 懒绑定补丁
- `MonoSystem.ResetForUnInit` 收敛职责：只停 SDK 自家协程，**不再**清 `updateEvent / lateUpdateEvent / fixedUpdateEvent` 监听
- `MonoSystem.Init` 方法删除（不再需要）；`MVXRSDK.InitSystems` 同步去掉对应调用
- `MonoSystem.OnDestroy` 清 `instance == this` 时的 static 引用，避免悬空

#### Fixed
- **跨 Init/UnInit 长生命周期外部订阅丢失**：玩家相机上的 `NetworkTransform`、障碍物上的 `SpaceObstacles` 等 MonoBehaviour 在 UnInit 后保留订阅，二次 Init 后继续 tick（之前 ResetForUnInit 一刀切清空导致失效）
- `EventSystem.cs:125` AddEventListener<T0..T12> 泛型签名最后一个参数 typo（`T2` → `T12`）

#### Added (regression tests)
- `Assets/Tests/Runtime/MonoSystemLifecycleTests.cs` 4 条：
  - `AddUpdateListener_BeforeInit_NoNRE` — Awake 阶段订阅不 NRE
  - `InitMVXRSDK_DoesNotClearExistingSubscriptions` — Init 不清外部订阅
  - `ResetForUnInit_DoesNotClearExistingSubscriptions` — UnInit 不清外部订阅
  - `InitUnInitInit_ExternalSubscription_Preserved` — 二次 Init 后订阅 invoke 正常

### Tests 移出 SDK 包 (2026-05-21)

#### Changed
- `Assets/com.myverse.xrsdk/Tests/` → `Assets/Tests/`：测试代码与场景脱离 SDK 包，不再随 UPM 分发
- SDK 包对外只剩 `Runtime/` + `Samples/Demo/` + 文档
- asmdef 引用未改（仍依赖 `MVXRSDK`），EditMode/PlayMode 测试入口不变
- CLAUDE.md / ONBOARDING.md 路径引用同步

### Demo 场景修复缺失引用 + 视觉完善 (2026-05-21)

#### Changed
- `Samples~/Demo` → `Samples/Demo`：脚本不再被 `~` 排除，dev workspace 直接编译，场景 MVXRSDKDemo 组件不再 Missing Script
- `package.json` samples path 同步指向 `Samples/Demo`
- 文档（index.md / CLAUDE.md）路径引用同步

#### Added (场景视觉)
- `Directional Light`（rotX=50, rotY=-30，避免场景全黑）
- `RootNode/Marker_Red` / `Marker_Green` / `Marker_Blue`（3 个彩色立方体标记点）+ `Marker_Yellow`（黄色球体，场景中心高度参考物）
- 5 份 URP Lit 材质 `Samples/Demo/Materials/Mat_*.mat`（Red / Green / Blue / Yellow / Floor）
- 3 台相机 ClearFlags=SolidColor + 不同背景色（主蓝灰 / Director1 紫色 / Director2 青色），切镜时画面变化肉眼可辨
- DirectorCamera1 / 2 转向场景中心（rotY=-110 / +110，rotX=15 向下俯视）

### Samples Demo 全模块联合示例 (2026-05-21)

#### Added
- `Samples~/Demo/MVXRSDKDemo.cs` —— 总控脚本（覆盖启动模式 Production/WsDirect、节点注册、积分扣除、推流装配、切镜真链路/本地直切、录屏、全局错误、热替换/注销）
- `Samples~/Demo/MVXRSDKDemo.unity` —— 测试场景（XR Rig + MainCamera + DirectorCamera×2 + RootNode + Floor + Demo 总控 GO 全部装配完毕）
- Documentation~/index.md §2.3 增补按键说明表

#### Removed
- `Samples~/Demo/TestSDK.cs` —— 被新 MVXRSDKDemo 完全覆盖

#### Changed
- `package.json` samples 描述更新为"全模块联合示例"

### 推流工具完整文档化 (2026-05-21)

#### Added (Documentation~/index.md)
- §8.2 画面源（IStreamSource）：`RenderTextureStreamSource` 与 `CameraStreamSource` 两种用法，各自的性能/约束/生命周期事件
- §8.3 推流装配组件 `MVXRStreamRig`：Inspector 字段、Ready/OnSwitched/OnRestored 事件、状态查询、与"业务自管"路径的对比
- §8.4 切镜（运行时切换画面源）：Rig 的 `SwitchCameraTemporary`/`RestoreOriginalCamera` + 底层 `SetStreamSource` 切源约定
- §8.5 音频推流（PCM 推送）：`PushGameAudioPcm` / `PushMicPcm` API 用法 + 采样率/通道约束 + Rig 自动调用关系

#### Changed
- §8 推流与录屏 章节顺序调整：原 8.2 录屏 / 8.3 Editor Debug / 8.4 推流配置 顺延为 8.6 / 8.7 / 8.8

### 文档收敛 (2026-05-21)

#### Removed
- 删除 `README.md`：内容（安装/依赖/快速开始/功能模块/FAQ）合并到 `Documentation~/index.md`
- 从 `Third Party Notices.md` 移除已删除的 LitJson 条目（v2 已删除该库）

#### Changed
- `Documentation~/index.md` 章节号顺延：新增 `2. 安装与依赖`（吸收原 README 的安装/依赖/示例/功能模块表）；原 2-10 节顺延为 3-11 节
- 全 SDK 用户文档收敛到两份：
  - **说明文档** = `Documentation~/index.md`
  - **更新文档** = `CHANGELOG.md`
- `LICENSE.md` / `Third Party Notices.md` 作为法律合规文件保留（BSD-3-Clause / zlib 许可要求保留版权声明，不可删）

### 自身节点改为主动注册 (2026-05-21)

#### BREAKING
- **删除** `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 自动抓取 `Camera.main` 的逻辑——业务侧改为主动调 `MVXRSDK.RegisterSelfNode(Transform)`
- 旧依赖 `Camera.main` 抓取，在测试场景 / 多相机 / 延迟构造场景下会失效；改为主动注册避免意外

#### Added
- **新增** `public MVXRSDK.RegisterSelfNode(Transform node)`：注册（或热替换）自身节点；自动挂 `NetworkTransform` 组件并切到 Reporter 角色
- **新增** `public MVXRSDK.UnRegisterSelfNode()`：注销自身节点；停止本机位姿上报（节点上的 NetworkTransform 组件保留，messageType 置 None）
- **新增** internal events `OnSelfNodeRegistered` / `OnSelfNodeUnregistered`（仅 SDK 内部使用）

#### Changed
- `SpaceObstacles` 改为每帧实时读 `MVXRSDK.SelfTransform`（去掉 OnEnable 缓存），支持自身节点运行时注册/热替换/注销即时生效
- `UnInitMVXRSDK` 资源释放阶段追加 `UnRegisterSelfNode()` 调用

### NetworkTransform 模块重构 (2026-05-21)

#### Changed
- `Operation/NetworkTransform/` 模块拆分为数据/表现两层：新增 `NetworkTransformStateStore` 持有每个远端玩家最新位姿快照，`NetworldTransformModule` 改为订阅 Store + 监听 XR Offset Node 注册/注销事件
- XR Offset Node 未注册时只缓存远端玩家位姿，不实例化角色 GO；XR Node 注册（或热替换）时按最新快照回放角色 GO；XR Node 注销时清空所有角色 GO 但保留快照
- 房间解散事件改为 `Store.ClearAndBroadcast()` 广播每个角色移除，表现层同步释放 GO，订阅关系保留

#### Fixed
- 修复 XR Offset Node 未注册时，`NetworldTransformModule.AcquireRole` 错误地 `Instantiate(parent: null)` 导致远端玩家落到根场景的退化行为
- `ApplyToScene` 对 `SelfTransform` 为空（如测试场景无 Main Camera）做容错：跳过距离判定而非 NRE

### SDK 生命周期对称修复 (2026-05-21)

#### Fixed
- **二次 Init NRE**：`InitSystems()` 调用从 `RuntimeInitializeOnLoadMethod` bootstrap 搬到 `InitMVXRSDK`，让 `UnInit → 再 Init` 路径下 `SocketSystem` 等 System 正确重建（修 PR-3 遗留 bug）
- **PoolRoot GO 泄漏**：`PoolSystem` 新增 `ResetForUnInit` 销毁 PoolRoot GO 并 null 模块字段，反复 Init/UnInit 不再累积 GO
- **OnApplicationQuit 未 Init 状态 NRE**：`SocketSystem.Clear` 加 null 守卫，未 Init 或 UnInit 后退出应用不再抛 NRE

#### Changed
- 抽取 `MVXRSDK.EnsureBootstrap()` 幂等方法承载 bootstrap 主体，启动钩子与 EditMode 测试都调它
- `RuntimeInitializeOnLoadMethod Init()` bootstrap 只创建 `MVXRSDKManager` GO + `MonoSystem` MonoBehaviour，不再触发 System 层初始化
- `EventSystem` 新增 `ResetForUnInit` 替代 `UnInitMVXRSDK` 内的 `EventSystem.Clear()` 调用——既清监听又 null 模块，符合对称释放语义

### Migration Guide (v1.x → v2.x)

```csharp
// 1. 字段重命名
MVXRSDK.PicoId            → MVXRSDK.DeviceId

// 2. 状态查询
MVXRSDK.IsInitSdk          → MVXRSDK.IsReady           (State >= LocalReady)
SocketSystem.IsConnect     → MVXRSDK.IsConnected
                            或 MVXRSDK.State == MVXRSDKState.Connected

// 3. 事件签名
OnPushStreamFailed += (int c, string m) => ...
                            → (MVXRSDKErrorCode c, string m) => ...
OnPushStreamStopped += (bool active) => ...
                            → (StreamStopReason reason) => ...
OnRecordResult += (bool ok, string err) => ...
                            → (MVXRSDKErrorCode code, string err) => {
                                bool ok = code == MVXRSDKErrorCode.Ok;
                                ...
                              }

// 4. 推流录屏的 PicoDeviceId pb 字段（pb 协议字段名不变，但传入用 DeviceId）
new StartRecordOptions {
    PicoDeviceId = MVXRSDK.DeviceId    // 旧：MVXRSDK.PicoId
}

// 5. 节点注册顺序约束放宽
// 旧版本：必须 Init 前调；v2 任何时机可调，无需调整代码顺序
```

---

## [1.1.0] - 2026-05-14

### BREAKING
- `MVXRSDK.OnTransactionVerification` 从 `Action<bool>` 升级为 `event Action<bool>`
  - **影响**：使用 `= handler`（赋值）或 `?.Invoke(...)`（外部触发）的代码将编译失败
  - **迁移**：把 `MVXRSDK.OnTransactionVerification = h;` 改为 `MVXRSDK.OnTransactionVerification += h;`；外部不能再触发该事件
- 推流/录屏对外 API 全部接入 pb 真实协议，签名变化：
  - `MVXRSDK.StartRecord(string tag)` → `MVXRSDK.StartRecord(StartRecordOptions opts)`：游戏必须填写 5 个 pb 字段（RealCamera / CameraId / DurationSec / FileName / PicoDeviceId）
  - 移除 `MVXRSDK.StopRecord()`：pb 协议无 StopRecord，限时由 `DurationSec` 控制，服务端到时自动停
  - `OnPushStreamStarted (string sessionId)` → `(string streamServerIp)`：从来自 NotifyLive 的服务器 IP
  - `OnPushStreamStopped (string sessionId, bool active)` → `(bool active)`：去 sessionId
  - `OnPushStreamFailed (string sessionId, int code, string msg)` → `(int code, string msg)`：去 sessionId
  - `OnRecordResult (bool, string, string, string)` → `(bool success, string errMsg)`：去 recordId 和 fileUrl（pb Response 只含 Success）

### Added
- 推流功能（被动接收播控 WebSocket NotifyLive 推送）
  - `MVXRSDK.SetStreamSource(RenderTexture)` / `ClearStreamSource()`
  - `event OnPushStreamStarted` / `OnPushStreamStopped` / `OnPushStreamFailed`
- 录屏信令转发（游戏主动调，SDK 通过 WebSocket 下发 `logic.StartRecord`；SDK 不做实际录制）
  - `MVXRSDK.StartRecord(StartRecordOptions opts)`
  - `event OnRecordResult (bool success, string errMsg)`
- Editor Debug 入口（仅 Editor，供手测脚本脱离服务端跑通推流链路）
  - `MVXRSDK.Debug_SimulateNotifyLive(string ip, bool start)`
- 引入 Unity Test Framework 测试体系（`Tests/Editor/` + `Tests/Runtime/`）
- `SocketSystem` 暴露公开属性 `IsConnect`

---

## [1.0.3] - 2026-04-09

### Added
- 场景根节点支持注册多个：`RegisterRootNode(Transform)` 可多次调用，每次将节点追加至列表
- 新增 `UnRegisterRootNode(Transform)`：注销指定单个场景根节点
- 网络断线重连失败后，在相机前生成遮罩提示"网络连接失败"（`NetworkFailureHUD`）

### Changed
- `UnRegisterRootNode()` 更名为 `UnRegisterAllRootNodes()`，语义更清晰

### Fixed
- 修复 `SpaceObstaclesModule.RemoveObstacle` 遍历列表时修改列表导致元素跳过的 Bug
- 修复 `NetworkTransform` 与 `SpaceObstacles` 组件销毁时 MonoSystem 监听器未注销的内存泄漏

### Performance
- `SocketModule`：发送请求新增 30 秒超时机制，超时自动回调失败并清理，防止 `m_ClientRequests` 无限驻留
- `SocketModule`：消息 API 解析从两次 `Split` 改为单次 `IndexOf`，减少临时数组分配
- `NetworkTransform`：热路径上报位置时复用 `Vector3` 字段（`.Set()`），消除每帧 GC 分配
- `SpaceObstacles`：缓存 `MVXRSDK.SelfTransform` 引用，距离阈值平方值在 `SetObstacleInfo` 时预算，LateUpdate 不再重复计算
- WebSocket 重连次数上限由无限改为 5 次（耗尽后触发 `SOCKET_RECONNECT_FAILED` 事件）
