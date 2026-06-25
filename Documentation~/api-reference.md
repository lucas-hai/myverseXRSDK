# MVXRSDK API 参考手册

> 适用版本 **2.0.1** ｜ 命名空间 `MyVerseXRSDK` ｜ 包名 `com.myverse.xrsdk`  
> 唯一对外入口是静态门面 `MVXRSDK`，所有对外 API 均为其 `public static` 成员。功能说明、使用流程与最佳实践见 [开发指南](index.md)。

---

## 1. 总览与约定

- **唯一入口**：业务方只与 `MVXRSDK`（`public static partial class`）交互；其下 `*Manager` / `*Module` / `*System` 全部 `internal`，业务不可见、不可调用。
- **错误处理**：除入参非法会同步抛 `ArgumentException` 外，运行期失败统一通过 `MVXRSDK.OnError`（错误码 + 信息 + 来源模块名）回调，不抛异常；错误码清单见 §9。
- **连接状态**：没有 `JoinRoom` / `LeaveRoom` / `OnConnected` / `OnDisconnected`。入房是 `InitMVXRSDK` 网络阶段的自动结果，离房即 `UnInitMVXRSDK`；连接态用 `State` / `IsConnected` 查询。
- **收录边界**：本手册只列业务方应直接使用的对外 public 面；SDK 内部基础设施虽有部分 `public`（同程序集可见），不属业务面，集中说明见 §11。
- **线程**：除特别标注（如 `PushGameAudioPcm` 可在 Unity 音频线程调用）外，API 默认在主线程调用。

> 章节导航：§2 生命周期与状态机 ｜ §3 节点注册与同房间虚影 ｜ §4 积分验证 ｜ §5 推流/切镜/录屏 ｜ §6 事件 ｜ §7 配置与数据类型 ｜ §8 画面源与组件 ｜ §9 枚举与错误码 ｜ §10 日志 ｜ §11 附录

## 2. 生命周期与状态机

SDK 生命周期围绕「本地阶段 + 网络阶段」两段式展开：`InitMVXRSDK` 同步装配本地 Manager 后，按 `InitMode` 决定是否拉起网络连接；`UnInitMVXRSDK` 对称释放。生命周期推进通过 `MVXRSDKState` 状态机表达，业务侧用 `State` / `IsReady` / `IsConnected` / `IsInitializing` 查询当前阶段，判断 API 是否可调。

### 2.1 状态机枚举 MVXRSDKState

`public enum MVXRSDKState`（`MVXRSDKState.cs:7-23`）。`(int)` 值固定，业务可依赖。

| 成员 | 值 | 含义 |
|------|----|------|
| `NotInitialized` | 0 | 进程启动 / UnInit 后的初始态；禁止调任何业务 API |
| `Initializing` | 1 | `InitMVXRSDK` 调用中，本地阶段未完成 |
| `LocalReady` | 2 | 本地 Manager 装配完成，可调 `SetStreamSource` 等本地能力；**Offline 模式终态** |
| `Connecting` | 3 | Production/WsDirect：HTTP 配置拉取 / 房间分配轮询 / WS 握手 / 登录中 |
| `Connected` | 4 | WS 已连接且登录成功、房间已分配；可调所有业务 API |
| `Disconnected` | 5 | 曾 Connected 后掉线（含 reconnect 中）；应等待自愈或调 UnInit |
| `Disposed` | 6 | `UnInitMVXRSDK` 完毕；下次 Init 回到 NotInitialized → Initializing 流程 |

状态迁移路径（`MVXRSDK.cs:151-177`）：`Initializing → LocalReady`（本地阶段同步完成）→ 按模式分支。Offline 停在 `LocalReady`；Production/WsDirect 进入 `Connecting`，网络阶段成功后由内部 RoomManager 切到 `Connected`，网络阶段失败则回退到 `LocalReady`（`MVXRSDK.cs:204`、`219`）。

> 注：`Disposed` 在 `UnInitMVXRSDK` 内只是瞬时终态——方法末尾立即把内部状态重置回 `NotInitialized`（`MVXRSDK.cs:266-267`），因此外部稳定观测到的是 `NotInitialized`。

### 2.2 InitMVXRSDK（两个 public 重载）

#### 重载一：`InitMVXRSDK(string)`

```csharp
public static void InitMVXRSDK(string deviceId)
```

一句话：兼容旧签名的便捷入口，等价于 `InitMVXRSDK(deviceId, InitMode.Production, null)`（`MVXRSDK.cs:115-118`）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `deviceId` | `string` | 本机设备 SN，赋给 `MVXRSDK.DeviceId` |

- 返回值：无。
- 副作用：以 Production 模式启动完整链路。

#### 重载二：`InitMVXRSDK(string, InitMode, string = null)`

```csharp
public static void InitMVXRSDK(string deviceId, InitMode mode, string controlServerAddress = null)
```

一句话：初始化 SDK 并指定启动模式（`MVXRSDK.cs:132-177`）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `deviceId` | `string` | 本机设备 SN。非空、长度 ≤ 64、不含 `空格 / ? # `（`MVXRSDK.cs:134-137`）。赋给 `MVXRSDK.DeviceId` |
| `mode` | `InitMode` | 启动模式（见下表） |
| `controlServerAddress` | `string`（默认 `null`） | **仅 WsDirect 模式使用**：中控服地址（例 `http://192.168.1.50:7015`）。注意是中控服地址而非房间服 WS 地址，房间服 WS 地址由中控轮询响应分配返回（`MVXRSDK.cs:123-125`） |

`InitMode`（`MVXRSDKDefine.cs:41-49`）：

| 模式 | 值 | 网络阶段 |
|------|----|---------|
| `Production` | 0 | 本地 HTTP(`localhost:8868`) 拉中控地址 → 轮询房间分配 → 连房间 WS → 登录（业务默认） |
| `WsDirect` | 1 | 跳过 `localhost:8868`，外部传入 `controlServerAddress`；其余房间分配 + WS + 登录链路同 Production |
| `Offline` | 2 | 完全跳过网络阶段，停留 `LocalReady`；`BaseUrl` 为空 |

- 返回值：无（异步链路无返回值；连接结果通过 `State` 与事件观察）。
- 抛出异常：`ArgumentException` —— `deviceId` 为空/纯空白，或含非法字符/超长（`MVXRSDK.cs:135`、`137`）。
- 约束/幂等：已处于 `LocalReady/Connecting/Connected/Disconnected` 时重复调用仅打警告并直接返回（`MVXRSDK.cs:139-144`）；`Initializing` 中重复调用同样忽略（`MVXRSDK.cs:145-149`）。
- 关键副作用：进入 `Initializing` → 拉起 System 层（Socket/Event/Pool）→ 装配本地 Manager → 置 `LocalReady` → 按模式分发网络阶段。WsDirect 缺 `controlServerAddress` 时报错并回退 `LocalReady`（`MVXRSDK.cs:216-221`）。
- 注意：调用返回后 `IsReady == true` 仅代表「本地就绪」，**不代表已连网**；连网完成以 `IsConnected == true`（`State == Connected`）为准。

最小调用示例：

```csharp
// 业务默认：正式模式
MVXRSDK.InitMVXRSDK("PA1234567890");

// 无 localhost 中控的测试环境，直连中控服
MVXRSDK.InitMVXRSDK("PA1234567890", InitMode.WsDirect, "http://192.168.1.50:7015");

// 完全离线，仅验证本地能力（推流源/节点等）
MVXRSDK.InitMVXRSDK("PA1234567890", InitMode.Offline);
```

### 2.3 UnInitMVXRSDK

```csharp
public static void UnInitMVXRSDK()
```

一句话：对称释放 SDK——卸载业务 Manager、注销所有已注册节点、重置 System 层与静态字段（`MVXRSDK.cs:227-268`）。

- 返回值：无。
- 抛出异常：无。
- 约束/幂等：`State == NotInitialized` 或 `Disposed` 时仅打警告并返回（`MVXRSDK.cs:229-233`）。
- 关键副作用：反向卸载 RoomManager/Business/NetworkTransform/Space/Stream；调用 `UnRegisterAllRootNodes` / `UnRegisterXROffsetNode` / `UnRegisterSelfNode`（`MVXRSDK.cs:243-245`）；清空积分与推流事件订阅；重置 `DeviceId`/`BaseUrl` 等静态字段为初始态（`MVXRSDK.cs:259-263`）。完成后内部状态回到 `NotInitialized`，可干净二次 Init。

### 2.4 状态查询属性

| 属性 | 签名 | 含义 | (file:line) |
|------|------|------|-------------|
| `State` | `public static MVXRSDKState State` | 当前生命周期状态（只读） | `MVXRSDK.cs:48` |
| `IsReady` | `public static bool IsReady` | 本地是否已就绪（`LocalReady`/`Connecting`/`Connected`/`Disconnected` 任一即 true）；可调本地能力 API | `MVXRSDK.cs:51-55` |
| `IsConnected` | `public static bool IsConnected` | 是否已成功连接中控（仅 `State == Connected`，含 WS 握手 + 登录成功 + 房间已分配） | `MVXRSDK.cs:58` |
| `IsInitializing` | `public static bool IsInitializing` | 是否正在执行本地阶段（仅 `State == Initializing`） | `MVXRSDK.cs:61` |

### 2.5 DeviceId

```csharp
public static string DeviceId { get; private set; }
```

一句话：本机设备 SN，`InitMVXRSDK` 时传入（`MVXRSDK.cs:27`）。业务侧做「中控切镜是否落在本机」判定时需要它。`UnInitMVXRSDK` 后置回 `null`（`MVXRSDK.cs:259`）。只读（外部不可赋值）。

> 范围外提示：`MVXRSDK.BaseUrl` / `IsTransactionVerification` / `RoomAllocationStatus` 均为 `internal`（`MVXRSDK.cs:29-34`），业务不可见，不在本手册收录。

---

## 3. 节点注册与同房间虚影

SDK 把场景中的关键 Transform 通过「节点注册」交给 SDK 管理，用于空间对齐、本机位姿上报、远端玩家虚影落地。共三类节点：**XR Offset 节点**（空间对齐根）、**Self 节点**（自身玩家相机）、**Scene Root 节点**（场景根，可多个）。

> **v2 现行约定（贯穿本节）**：所有注册/注销 API 任何时机皆可调用（Init 前/中/后），SDK 实时读取最新值；重复注册**同一节点**幂等忽略，注册**不同节点**视为热替换；未注册时 SDK 内部读取该节点的代码都做了 null 检查 → 对应功能**静默不启用**，不报错（`MVXRSDK.Nodes.cs:6-12`、`70-81`、`147-158`）。

### 3.1 XR Offset 节点

场景空间对齐根节点（XR Origin），障碍物、其他玩家虚影等都挂在其子树并随它对齐。

#### RegisterXROffsetNode

```csharp
public static void RegisterXROffsetNode(Transform node)
```

一句话：注册或热替换 XR 偏移节点（`MVXRSDK.Nodes.cs:82-106`）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `node` | `Transform` | XR 偏移根节点；为 `null` 时记 Error 并放弃注册 |

- 返回值：无。抛出异常：无（`null` 仅打 Error 日志，`MVXRSDK.Nodes.cs:84-88`）。
- 幂等/热替换：与当前节点引用相同 → Debug 日志后忽略（`MVXRSDK.Nodes.cs:89-93`）；不同节点 → 热替换，记 Info（`MVXRSDK.Nodes.cs:94-97`）。
- 副作用：触发内部事件 `OnXROffsetNodeRegistered`（仅 SDK 内部模块订阅，业务不可见）。

#### UnRegisterXROffsetNode

```csharp
public static void UnRegisterXROffsetNode()
```

一句话：注销 XR 偏移节点，连带回收 XR 子树下的障碍物/虚影 GO（`MVXRSDK.Nodes.cs:114-143`）。

- 返回值：无。抛出异常：无。
- 幂等：从未注册（真 `null`）时仅警告并返回（`MVXRSDK.Nodes.cs:118-122`）。
- 关键副作用：若 Self 节点（玩家相机）挂在 XR 子树下或已随之销毁，则**连带注销 Self 节点**，清掉内部悬挂引用（`MVXRSDK.Nodes.cs:129-135`）；Self 独立挂载则保留。典型用于「切场景销毁整个 XR Origin」——先调本方法回收 SDK 状态，再销毁 XR GameObject。

### 3.2 Self 节点（自身玩家相机）

通常为玩家相机 Transform。注册后 SDK 在该节点上挂 `NetworkTransform` 并切到 Reporter 角色，开始本机位姿上报。

#### RegisterSelfNode

```csharp
public static void RegisterSelfNode(Transform node)
```

一句话：注册或热替换自身节点（`MVXRSDK.Nodes.cs:159-186`）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `node` | `Transform` | 自身玩家相机 Transform；为 `null` 时记 Error 并放弃 |

- 返回值：无。抛出异常：无（`null` 仅 Error，`MVXRSDK.Nodes.cs:162-165`）。
- 幂等/热替换：同一节点 → 忽略（`MVXRSDK.Nodes.cs:166-170`）；不同节点 → 先 `StopReporterOn` 停旧节点 Reporter 再挂新节点（`MVXRSDK.Nodes.cs:171-176`）。
- 关键副作用：在节点上 `GetComponent<NetworkTransform>()`，无则 `AddComponent`，并 `SetRole(Reporter)` 开始上报（`MVXRSDK.Nodes.cs:209-214`）；触发内部事件 `OnSelfNodeRegistered`。
- 设计说明：v2 不再依赖 `Camera.main` 自动抓取，业务须自持相机引用主动注册（`MVXRSDK.Nodes.cs:156-157`）。

#### UnRegisterSelfNode

```csharp
public static void UnRegisterSelfNode()
```

一句话：注销自身节点，停止本机位姿上报（`MVXRSDK.Nodes.cs:192-206`）。

- 返回值：无。抛出异常：无。幂等：未注册时仅警告返回（`MVXRSDK.Nodes.cs:195-199`）。
- 关键副作用：调用 `StopReporterOn` 把节点上 `NetworkTransform` 的角色置 `None`（组件保留，仅停上报，`MVXRSDK.Nodes.cs:217-222`）；触发内部事件 `OnSelfNodeUnregistered`。

### 3.3 Scene Root 节点（可多个）

转发到内部 SpaceManager 管理场景根节点；支持注册多个，并提供一次性全部注销。

| 方法 | 签名 | 一句话 | (file:line) |
|------|------|--------|-------------|
| `RegisterRootNode` | `public static void RegisterRootNode(Transform root)` | 注册场景根节点 | `MVXRSDK.Nodes.cs:53-56` |
| `UnRegisterRootNode` | `public static void UnRegisterRootNode(Transform root)` | 注销指定场景根节点 | `MVXRSDK.Nodes.cs:58-61` |
| `UnRegisterAllRootNodes` | `public static void UnRegisterAllRootNodes()` | 注销全部场景根节点（`UnInitMVXRSDK` 内部也会调用） | `MVXRSDK.Nodes.cs:63-66` |

- 三者返回值均为无，均转发到 `SpaceManager`，具体幂等语义由 SpaceManager 实现（本文件仅做转发，未在 Facade 层做参数校验）。

> **使用注意（动静节点分离）**：根节点偏移只改根节点自身 `Transform`；被标记为 **Static / Batching Static** 的子物体经静态批处理后世界坐标固定，**不随根节点偏移**。建议把动态与静态节点分离，加载时**先 `RegisterRootNode` 完成偏移、再激活静态节点**。场景偏移在正式流程中由中控在开始游戏时下发一次，布店部署期才需手动调整（建议用动态节点作锚点）。详见 [开发指南 §6.2](index.md)。

### 3.4 同房间虚影开关与显示距离

控制是否为「同房间（本房间）其他玩家」创建场景虚影，以及同房间虚影的显示距离。

**显示距离规则（重要）**：

| 对象 | 是否显示 | 显示距离 |
|------|---------|---------|
| **本机自己** | **一律不显示**（按 `DeviceId == 本机` 过滤，`NetworkTransformManager.cs`） | — |
| **其他房间虚影** | 显示 | **固定 2m**（`MVXRSDKConfig.NORMAL_DISTANCE`，不受参数影响） |
| **同房间虚影** | 默认不显示，开关开启后显示 | **外部传参，默认 2m**（见下方 `displayDistanceMeters`） |

> 默认只为「非本房间」成员创建虚影，本房间成员位置推送被跳过（`NetworkTransformManager.cs`）；开关与查询定义见 `MVXRSDK.NetworkTransform.cs`。

#### IsSyncSameRoomAvatar

```csharp
public static bool IsSyncSameRoomAvatar
```

一句话：是否同步同房间其他玩家虚影（只读查询，默认 `false`）。

#### SameRoomAvatarDistance

```csharp
public static float SameRoomAvatarDistance
```

一句话：同房间虚影当前显示距离（米，只读，默认 `2`）。

#### SetSyncSameRoomAvatar

```csharp
public static void SetSyncSameRoomAvatar(bool enable, float displayDistanceMeters = 2f)
```

一句话：开关同房间虚影同步并设其显示距离。

| 参数 | 类型 | 含义 |
|------|------|------|
| `enable` | `bool` | `true` 开启，`false` 关闭。默认行为 `false`（不同步） |
| `displayDistanceMeters` | `float` | 同房间虚影显示距离（米），默认 `2`，`<=0` 回退 2m；**仅在 `enable=true` 时生效**（关闭时忽略，不改写已设值） |

- 返回值：无。抛出异常：无。约束：任何时机皆可调用（Init 前/中/后）。
- 关键副作用：开启后续收到本房间位置推送即创建虚影（成员静止不发推送时，等其移动后才出现）；关闭立即回收已创建的本房间虚影，不影响非本房间虚影。
- 距离生效时机：改距离后，已存在的同房间虚影在**下一次位置推送**时按新距离重新判定（静止成员需等其移动后生效）。
- 依赖：虚影外观复用 SDK 内置角色 prefab（`Characters/Prefabs/Role`），且与其他远端虚影一样**依赖已注册 XR 偏移节点**才会落地到场景。

### 3.5 NetworkTransform 组件（SDK 内部管理，了解即可）

`public class NetworkTransform : MonoBehaviour`（`NetworkTransform.cs:8`），其嵌套枚举 `public enum MessageType`（`NetworkTransform.cs:33-41`）：

| 成员 | 值 | 含义 |
|------|----|------|
| `None` | 0 | 不上报也不接收（角色被注销后空转） |
| `Reporter` | 1 | 上报者：上报本地位姿，不处理外部数据 |
| `Receiver` | 2 | 接收者：接收并应用外部位姿，不上报 |

> 该组件由 SDK 自管：`RegisterSelfNode` 时自动挂载并切 `Reporter`，远端虚影由 SDK 自动挂 `Receiver`。**业务通常无需直接操作**，了解即可。其字段（`messageType` 等）为 `internal`/`private`（`NetworkTransform.cs:44`），不属业务面 API。

### 3.6 节点相关错误码（6xxx，仅供参考）

`MVXRSDKErrorCode`（`MVXRSDKErrorCode.cs:61-64`）中节点/空间段：

| 错误码 | 值 |
|--------|----|
| `NodeNull` | 6001 |
| `NodeAlreadyRegistered` | 6002 |
| `XROffsetAfterInit` | 6003 |

> 注：`NodeAlreadyRegistered(6002)` / `XROffsetAfterInit(6003)` 与 v2「节点注册任意时机皆可、重复同节点幂等、不同节点热替换」的现行约定表面冲突——现行注册实现对重复/Init 后注册均不抛错（见 3.1/3.2 源码）。此处如实列出码值，**是否实际触发以实现为准**。

---

## 4. 积分验证

接入中控房间系统时积分扣除由网络阶段自动完成；**若当前项目不接入中控房间系统**，业务需手动调用 `TransactionVerification` 完成单机积分交易验证，验证通过即可开始游戏。

### 4.1 TransactionVerification

```csharp
public static void TransactionVerification(Action<bool> cb)
```

一句话：发起单机积分交易验证，结果通过回调返回（`MVXRSDK.cs:276-306`）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `cb` | `Action<bool>` | 验证结果回调；参数 `true` 表示验证通过，可开始游戏 |

- 返回值：无（结果走 `cb`）。抛出异常：无。
- 约束/幂等：
  - 已验证通过（内部 `IsTransactionVerification == true`）→ 仅警告并直接返回，**不再回调 `cb`**（`MVXRSDK.cs:278-282`）。
  - `BaseUrl` 为空（HTTP 初始化未完成，或当前为 **WsDirect/Offline 模式**）→ 警告并立即 `cb(false)`（`MVXRSDK.cs:284-289`）。
  - 验证正在进行中 → 仅警告并直接返回，**不再回调 `cb`**（`MVXRSDK.cs:293-297`）。
- 关键副作用：转发到内部 `BusinessManager.TransactionVerification`；成功后置内部 `IsTransactionVerification = isResult`（`MVXRSDK.cs:300-305`）。

最小调用示例：

```csharp
// 不接入中控房间系统时，手动做积分验证
MVXRSDK.TransactionVerification(passed =>
{
    if (passed) StartGame();        // 验证通过，进入游戏
    else ShowVerifyFailedTip();     // 未通过（如 BaseUrl 为空 / 中控拒绝）
});
```

> **事件交叉引用**：积分验证还会通过对外事件 `MVXRSDK.OnTransactionVerification`（`Action<bool>`，`MVXRSDK.Events.cs:23`）广播结果——例如内部命中本机 SN 时由 BusinessManager raise `OnTransactionVerification(true)`（`BusinessManager.cs:81`）。该事件的完整说明见 **第 6 章 事件**。
> **错误码交叉引用**：积分相关错误码（7xxx）`TransactionFailed(7001)` / `TransactionInProgress(7002)` / `TransactionBaseUrlMissing(7003)`（`MVXRSDKErrorCode.cs:66-69`）。

---

> 以上为生命周期、节点注册与积分验证相关 API；底层 `RoomManager` / `BusinessManager` / `SpaceManager` 等均为 internal，不在业务面范围内。

## 5. 推流 / 切镜 / 录屏（方法）

本章收录 `MVXRSDK` Facade 中转发到推流业务域的 public 方法（源码 `Runtime/Scripts/MVXRSDK.Stream.cs`，全部为 `public static partial class MVXRSDK` 成员，是对 internal `StreamManager` 的薄转发）。配置类型 `StreamConfig` / `StartRecordOptions` / `DirectorRequestOptions` / `DirectorSource` 的字段细节见第 7 章，这里仅引用。

推流状态查询属性（`IsStreaming` / `CurrentStreamUrl` / `GetStreamStats()`）见对应章节；本节 `CurrentStreamUrl` 因任务要求一并列出。

> 说明：被中控选中而真正开始推流的信号是 `NotifyLive(start)`，对外表现为 `OnPushStreamStarting` 事件——业务在该回调里 `SetStreamSource` 接源。本节方法均不直接"开始推流"。

---

### 5.1 SetStreamSource(RenderTexture)

```csharp
public static void SetStreamSource(RenderTexture source)
```

设置推流画面源：业务自渲到一张任意格式 `RenderTexture`，SDK 每帧把它 Blit 到内部合法格式 RT，自动处理 `com.unity.webrtc` 的格式约束。(`MVXRSDK.Stream.cs:30`)

| 参数 | 类型 | 含义 |
|---|---|---|
| `source` | `RenderTexture` | 业务渲染目标 RT；传 `null` 等效于清源（见副作用） |

- **返回值**：无。
- **异常**：无显式抛出。
- **约束/默认值**：推流期间切换到不同尺寸的 RT 需先 `ClearStreamSource`（`StreamManager.cs:143` 注释）。InternalRT 为固定尺寸（`StreamConfig.StreamMaxLongSide` 按 16:9），任意源可热切。
- **副作用**：
  - 每帧多一次 GPU Blit（约 0.2–0.5ms，`MVXRSDK.Stream.cs:28`）。
  - 传 `null` 时调用 `TextureProviderSystem.ClearSource()`；若推流会话非 Idle 还会打 Warning "推流期间清空 source，将推黑帧直到重新接源"（`StreamManager.cs:147-154`）。
  - **一相机推流保护**：推流会话活跃（`State != Idle`）且已有源时，本次 `SwitchSource` 直接丢弃新源（不排队不抢占，`StreamManager.cs:59-61`、`IStreamSource.cs:14`）。

```csharp
// 典型用法：在 OnPushStreamStarting 回调里接源
MVXRSDK.OnPushStreamStarting += url => MVXRSDK.SetStreamSource(myRenderTexture);
```

---

### 5.2 SetStreamSource(IStreamSource)

```csharp
public static void SetStreamSource(IStreamSource source)
```

高级用法：业务自行实现 `IStreamSource` 直接交给 SDK，可订阅 `source.OnAttached` / `OnDetached` 生命周期事件做精细控制（典型：切走时暂停自家采集 Blit 省 GPU，切回自动恢复）。(`MVXRSDK.Stream.cs:41`)

| 参数 | 类型 | 含义 |
|---|---|---|
| `source` | `IStreamSource`（public 接口，`IStreamSource.cs:16`） | 业务实现的画面源；传 `null` 等效清源 |

- **返回值**：无。
- **异常**：无显式抛出。
- **副作用**：同 5.1（`null` 清源 + 推流中 Warning；一相机推流保护）。`MVXRStreamRig` 切镜内部即用此入口包装 SDK 内置的 `RenderTextureStreamSource` / `CameraStreamSource`（`StreamManager.cs:160-176`）。
- **备注**：`IStreamSource` 是对外可实现接口，业务可做 XR 立体合成、多摄拼接等"喂画面工具"（`IStreamSource.cs:6-9`）。

---

### 5.3 ClearStreamSource

```csharp
public static void ClearStreamSource()
```

清除画面源引用。(`MVXRSDK.Stream.cs:47`)

- **参数**：无。 **返回值**：无。 **异常**：无。
- **约束**：推流停止后可选调用；多场景约定为场景卸载前调用以解绑相机（SDK 有相机销毁自保护兜底，但属误用防护）。
- **副作用**：转发 `TextureProviderSystem.ClearSource()`（`StreamManager.cs:179-182`）。清源后若推流仍活跃将推黑帧。

---

### 5.4 SetStreamConfig

```csharp
public static void SetStreamConfig(StreamConfig config)
```

应用推流配置（分辨率 / 码率 / 超时 / H.264 强制 / 重试节奏等）。(`MVXRSDK.Stream.cs:56`)

| 参数 | 类型 | 含义 |
|---|---|---|
| `config` | `StreamConfig`（字段见第 7 章） | 推流配置对象；传 `null` 恢复默认配置 |

- **返回值**：无。 **异常**：无显式抛出。
- **约束/默认值**：传 `null` 恢复默认；修改 `Width`/`Height`/`Bandwidth` 仅对后续 `Start` 生效，**进行中的推流不会重协商**（`MVXRSDK.Stream.cs:53-54`）。
- **副作用**：转发 `StreamConfig.Apply(config)`（该 `Apply` 方法为 SDK 内部，业务不直接调用）。

---

### 5.5 CurrentStreamUrl（属性）

```csharp
public static string CurrentStreamUrl { get; }
```

当前推流的 WHIP URL；未推流时返回 `null`。(`MVXRSDK.Stream.cs:18`)

- **返回值**：`string`，未连入会话时为 `null`（`StreamManager.cs:194-195`）。
- **副作用**：只读，无。

---

### 5.6 SendDirectorRequest(DirectorRequestOptions)

```csharp
public static void SendDirectorRequest(DirectorRequestOptions opts)
```

纯请求重载：向中控发 `DirectorInsert.Request` 申请切镜。受理结果走 `OnDirectorRequestResult` 事件；**是否被选中以 NotifyLive 为准**——被选中时 SDK 触发 `OnPushStreamStarting`，业务需在该回调中自行 `SetStreamSource` 接源。(`MVXRSDK.Stream.cs:108`)

| 参数 | 类型 | 含义 |
|---|---|---|
| `opts` | `DirectorRequestOptions`（字段见第 7 章） | 切镜请求参数（`Source`/`Lenses`/`DurationSec`/`Record`/`FileName`） |

- **返回值**：无。
- **异常**：无显式抛出；非法入参/状态通过 `OnDirectorRequestResult(false)` 异步反馈，不抛异常。
- **约束/默认值**（`StreamManager.cs:215-264`）：
  - `opts.DurationSec` 必须 `> 0`，否则记 Error 并 `RaiseDirectorRequestResult(false)` 返回。
  - `opts.Lenses < 1` 时打 Warning 并按 `1` 处理。
  - SDK 未初始化 / WebSocket 未连接时丢弃请求并回 `false`。
  - `opts.Source` 留空（`null`/空串）是合法协议值 = 原直播，SDK **原样透传不做默认填充**；请求切回原直播用 `DirectorSource.Mr` 或留空。
- **副作用**：本重载传 `camera=null`，会**覆盖并清除**上一次自动接源 pending 相机（`StreamManager.cs:257-258`）。

---

### 5.7 SendDirectorRequest(DirectorRequestOptions, Camera)

```csharp
public static void SendDirectorRequest(DirectorRequestOptions opts, Camera camera)
```

请求切镜 + 被选中后**自动接源**（推荐）：`opts.Source` 留空时自动填 `DirectorSource.Unity`；被选中（`NotifyLive start`）时若业务未手动接源，SDK 自动把 `camera` 包成 `CameraStreamSource` 接上，停流时自动清除。(`MVXRSDK.Stream.cs:120`)

| 参数 | 类型 | 含义 |
|---|---|---|
| `opts` | `DirectorRequestOptions` | 同 5.6；`Source` 留空自动填 `"unity"` |
| `camera` | `UnityEngine.Camera` | 被选中后自动包成 `CameraStreamSource` 的相机 |

- **返回值**：无。 **异常**：无显式抛出（失败走 `OnDirectorRequestResult(false)`）。
- **约束/默认值**（`StreamManager.cs:215-264`）：
  - 传了 `camera` 即明确"unity 机位"：`opts.Source` 留空自动填 `Unity`；若显式填了非 `"unity"` 的值视为矛盾请求，记 Error 并回 `false`。
  - `DurationSec`/`Lenses`/未初始化/未连接校验同 5.6。
- **关键副作用——自动接源 pending 生命周期**（`StreamManager.cs:257-258`、`280-303`、`402-438`）：

  | 事件 | pending 行为 |
  |---|---|
  | 新 `SendDirectorRequest`（任一重载） | 覆盖旧 pending（纯请求重载传 `null` 会清掉自动接源意图） |
  | 请求被拒（应答失败/`Success=false`） | 清除 pending |
  | 会话启动（`OnPushStreamStarting` 后） | 消费 pending；若业务已手动接源则**手动优先**，自动接源跳过 |
  | 相机在 pending 期间被销毁 | 放弃自动接源（推黑帧，打 Warning） |
  | 停流 / 推流失败且不再重试 | 按实例同一性清除 SDK 自动接的源；业务手动接的源不受影响（业务自清） |

```csharp
// 推荐用法：一行请求切镜并托管接源
var opts = new DirectorRequestOptions { Lenses = 1, DurationSec = 30, Record = false };
MVXRSDK.SendDirectorRequest(opts, myGameCamera); // Source 留空→自动 "unity"
```

---

### 5.8 StartRecord

```csharp
public static void StartRecord(StartRecordOptions opts)
```

通知播控开始录屏。所有 pb 字段（`PicoDeviceId` / `FileName` / `DurationSec` 等）必须由游戏侧填充。(`MVXRSDK.Stream.cs:68`)

| 参数 | 类型 | 含义 |
|---|---|---|
| `opts` | `StartRecordOptions`（字段见第 7 章） | 录屏参数集，对应 pb `StartRecord.Request` 5 字段 |

- **返回值**：无。 **异常**：无显式抛出。
- **约束**：限时模式由 `opts.DurationSec` 控制，服务端到时自动停止；**本期无 StopRecord 接口**（`MVXRSDK.Stream.cs:65-66`）。SDK 未初始化时拒绝并以 `OnRecordResult(NotInitialized)` 反馈（`StreamManager.cs:201-207`）。
- **副作用**：结果异步走 `OnRecordResult` 事件。

---

### 5.9 PushGameAudioPcm

```csharp
public static void PushGameAudioPcm(float[] pcm, int sampleRate, int channels)
```

推送游戏音频 PCM 给 SDK（推流唯一音频源；不推麦克风语音）。推荐挂在与 `AudioListener` 同 GameObject 的 `OnAudioFilterRead` 里调用。(`MVXRSDK.Stream.cs:82`)

| 参数 | 类型 | 含义 |
|---|---|---|
| `pcm` | `float[]` | PCM 采样数据，不能为 `null` |
| `sampleRate` | `int` | 采样率，区间 8000–192000 Hz |
| `channels` | `int` | 通道数，仅 1（mono）或 2（stereo） |

- **返回值**：无。
- **异常**：`ArgumentException`（参数校验在 facade 的 `ValidatePcmArgs`，`MVXRSDK.Stream.cs:88-98`）：
  - `pcm == null` → "pcm 不能为空"（paramName `pcm`）。
  - `sampleRate < 8000 || > 192000` → "不支持的采样率…（仅 8000–192000）"（paramName `sampleRate`）。
  - `channels != 1 && != 2` → "不支持的通道数…（仅 mono=1 / stereo=2）"（paramName `channels`）。
- **约束/默认值**：与设备输出率一致时直通零重采样，否则 SDK 线性重采样；stereo 内部自动平均成 mono（`MVXRSDK.Stream.cs:78-79`）。SDK 未初始化时静默返回不抛错（`StreamManager.cs:307-309`）。
- **备注**：实测设备采样率 PICO 4U 输出常见 24000，语音 SDK 常见 16000（`MVXRSDK.Stream.cs:93` 注释）。

```csharp
void OnAudioFilterRead(float[] data, int channels)
{
    MVXRSDK.PushGameAudioPcm(data, AudioSettings.outputSampleRate, channels);
}
```

---

### 5.10 Debug_SimulateNotifyLive（调试入口）

```csharp
public static void Debug_SimulateNotifyLive(string streamServerIp, bool start)
```

调试用：模拟播控下发 `NotifyLive`，绕过 WebSocket 走真业务链路（真 WHIP POST + WebRTC 协商，事件链路与正式 NotifyLive 完全一致）。`Offline` 模式下是唯一启动推流的入口。(`MVXRSDK.Stream.cs:128`)

| 参数 | 类型 | 含义 |
|---|---|---|
| `streamServerIp` | `string` | 模拟下发的 WHIP 服务地址（必须是合法 http/https 绝对 URL，`start=true` 时由 Manager 层校验） |
| `start` | `bool` | `true` 开始 / `false` 停止 |

- **返回值**：无。 **异常**：无显式抛出（非法 URL 走 `OnPushStreamFailed`）。
- **约束**：前置条件为 SDK 已 `InitMVXRSDK`、画面源已设置（`StreamManager.cs:599-607`）。
- **备注（与任务清单的核对）**：源码中该方法**未**包裹 `#if UNITY_EDITOR` 条件编译（`MVXRSDK.Stream.cs:127-131` 无预处理指令）——它是常规 public 方法，运行时包内也可调用。若文档需要"仅编辑器/调试使用"的语义，应以"约定仅调试场景使用"措辞表达，而非声明编译期受限；此点拿不准的部分按源码实测如实标注。

---

以上方法均为 `MVXRSDK` Facade 的 public 转发，底层 `StreamManager` / `PushStreamModule` / `TextureProviderSystem` 等为 internal，不在业务面 API 范围内。

## 6. 事件

MVXRSDK 对外事件全部声明在 `MVXRSDK.Events.cs`（`public static partial class MVXRSDK`），均为 `public static event`，用 `+=` / `-=` 订阅。所有 `Raise*` 触发方法和 `Clear*EventSubscribers` 清理方法都是 `internal` / `private`，业务方不可调用——业务只负责订阅，触发由 SDK 内部完成。`UnInitMVXRSDK` 会清空全部订阅。

> **不存在 `OnConnected` / `OnDisconnected`**：facade 上没有这两个连接态事件（`MVXRSDK.Events.cs` 全文无此声明；同名 event 仅存在于 internal 接口 `IWebRTCSession`——`IWebRTCSession.cs:27-28` 声明、`:15` 注明"实现内部用"、`:28` 注明"当前 SDK 内未订阅"，与业务无关）。
> - **入房**：由 `InitMVXRSDK` 的网络阶段自动完成，没有独立的"已连接"事件。
> - **离房**：调 `UnInitMVXRSDK`。
> - **连接态查询**：用 `MVXRSDK.IsConnected` / `MVXRSDK.State`（轮询查询，非事件）。

事件一览（`MVXRSDK.Events.cs`）：

| 事件 | 委托签名 | 触发时机 | file:line |
|------|----------|----------|-----------|
| `OnTransactionVerification` | `Action<bool>` | 中控房间系统主动推送积分验证结果 | :23 |
| `OnPushStreamStarting` | `Action<string>` | SDK 接受推流指令、开始建会话（本机被选中信号） | :68 |
| `OnPushStreamStarted` | `Action<string>` | 推流真正建立成功 | :41 |
| `OnPushStreamStopped` | `Action<StreamStopReason>` | 推流停止 | :44 |
| `OnPushStreamFailed` | `Action<MVXRSDKErrorCode, string>` | 推流失败 | :47 |
| `OnPushStreamStats` | `Action<StreamStats>` | 推流实时统计（默认每 1s） | :50 |
| `OnRecordResult` | `Action<MVXRSDKErrorCode, string>` | 录屏请求结果回调 | :53 |
| `OnDirectorSelected` | `Action<string, bool, int, int>` | 中控仲裁结果透传（仅协议日志） | :60 |
| `OnDirectorRequestResult` | `Action<bool>` | 中控对切镜请求的受理应答 | :75 |
| `OnError` | `Action<MVXRSDKErrorCode, string, string>` | 全局错误聚合 | :82 |

---

### 6.1 OnTransactionVerification —— 积分验证结果

```csharp
public static event Action<bool> OnTransactionVerification;
```

积分验证结果事件，**仅当中控房间系统主动推送时触发**（`MVXRSDK.Events.cs:23`，由 `internal RaiseTransactionVerification(bool ok)` 触发，:26-29）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `ok` | `bool` | `true` 验证通过（已扣费成功），`false` 验证失败 |

- **使用约定（二选一，不要同时用）**：
  - 接入中控房间系统：只订阅本事件，中控启动游戏时 SDK 自动触发。
  - 不接入中控、自助验证：只调 `TransactionVerification(Action<bool>)` 用其回调。
- 同时订阅本事件 + 调 `TransactionVerification(cb)` **不会重复扣费**（两条路径独立），但容易导致业务侧重复处理；商业接入务必二选一（:18-21）。

---

### 6.2 OnPushStreamStarting —— 推流握手开始（被选中信号）

```csharp
public static event Action<string> OnPushStreamStarting;
```

SDK 接受推流指令、开始建立会话时触发，早于 `OnPushStreamStarted` 约 1~2s 的握手期（`MVXRSDK.Events.cs:62-68`，由 `internal RaisePushStreamStarting`,  :99 触发）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `streamServerIp` | `string` | 推流目标服务器 IP（来自 `NotifyLive.StreamServerIp`） |

- **`NotifyLive(start)` 即"本机被选中"的权威信号**——业务应在此回调中 `SetStreamSource` 接相机，使首帧即有画面；没被选中就不接源（避免相机白渲染）。
- SDK 内部自动重试，**不会重复触发**本事件。

最小调用示例：

```csharp
MVXRSDK.OnPushStreamStarting += ip =>
{
    // 被选中，立刻接上本机机位相机，保证首帧有画面
    MVXRSDK.SetStreamSource(myCameraRenderTexture);
};
```

---

### 6.3 OnPushStreamStarted —— 推流开始

```csharp
public static event Action<string> OnPushStreamStarted;
```

推流会话建立成功后触发（`MVXRSDK.Events.cs:40-41`，由 `internal RaisePushStreamStarted`,  :84 触发）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `streamServerIp` | `string` | 推流服务器 IP（来自 `NotifyLive.StreamServerIp`） |

- 时序：`OnPushStreamStarting`（握手开始）→ 约 1~2s 后 `OnPushStreamStarted`（推流就绪）。

---

### 6.4 OnPushStreamStopped —— 推流停止

```csharp
public static event Action<StreamStopReason> OnPushStreamStopped;
```

推流停止时触发（`MVXRSDK.Events.cs:43-44`，由 `internal RaisePushStreamStopped`,  :85 触发）。参数 `StreamStopReason` 替代旧版 `bool active` 的歧义参数。

| 参数 | 类型 | 含义 |
|------|------|------|
| `reason` | `StreamStopReason` | 停止原因（见下表） |

`StreamStopReason` 枚举值（`StreamStopReason.cs:6-18`）：

| 枚举 | (int) | 含义 |
|------|-------|------|
| `ServerStop` | 0 | 业务方主动调 Stop / 中控 `NotifyLive(start=false)` |
| `UserStop` | 1 | SDK 内部主动停止（如 `NotifyLive` 切 URL 时断旧重连） |
| `NetworkLost` | 2 | 网络异常断流 / WS 重连耗尽 |
| `ConfigChanged` | 3 | URL 变更触发的断旧准备启新 |
| `SdkUnInit` | 4 | 反初始化（`UnInitMVXRSDK`）触发的强制停流 |

---

### 6.5 OnPushStreamFailed —— 推流失败

```csharp
public static event Action<MVXRSDKErrorCode, string> OnPushStreamFailed;
```

推流失败时触发（`MVXRSDK.Events.cs:46-47`，由 `internal RaisePushStreamFailed`,  :86-90 触发）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `code` | `MVXRSDKErrorCode` | 统一错误码（推流域为 4xxx 段） |
| `msg` | `string` | 错误信息 |

- **关键副作用**：触发本事件的同时，SDK 会向 `OnError` 同步广播一份（来源 `"Stream"`，:89）。订阅了 `OnError` 的业务无需再单独订阅本事件去做监控上报。

---

### 6.6 OnPushStreamStats —— 推流实时统计

```csharp
public static event Action<StreamStats> OnPushStreamStats;
```

推流实时统计回调，**默认每 1s 触发一次**，间隔可由 `StreamConfig.StatsReportIntervalMs` 配置（`MVXRSDK.Events.cs:49-50`，由 `internal RaisePushStreamStats`,  :91 触发）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `stats` | `StreamStats` | 实时统计快照 |

`StreamStats` 字段（`StreamStats.cs:14-42`，`public sealed class`；字段为 0 表示该指标本周期未拿到，如握手刚完成时 remote-inbound 还没回报）：

| 字段 | 类型 | 含义 |
|------|------|------|
| `BytesSent` | `long` | 累计发送字节数（视频 + 音频） |
| `PacketsSent` | `long` | 累计发送包数 |
| `PacketsLost` | `long` | 远端反馈：累计丢包数 |
| `JitterMs` | `double` | 远端反馈：网络抖动（毫秒） |
| `RttMs` | `double` | 当前 ICE 链路 RTT（毫秒） |
| `FramesEncoded` | `int` | 视频累计编码帧数 |
| `VideoBitrateKbps` | `int` | 近 1 秒视频发送码率（kbps） |
| `AvailableOutgoingBitrateKbps` | `int` | BWE 估算可用上行带宽（kbps），0 = 未上报 |
| `Timestamp` | `float` | 本次采样时间戳（秒，`Time.realtimeSinceStartup`） |

> 同样数据也可用 `MVXRSDK.GetStreamStats()` 同步查询（`StreamStats.cs:5`）。

---

### 6.7 OnRecordResult —— 录屏结果

```csharp
public static event Action<MVXRSDKErrorCode, string> OnRecordResult;
```

录屏请求结果回调（`MVXRSDK.Events.cs:52-53`，由 `internal RaiseRecordResult`,  :92-96 触发）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `code` | `MVXRSDKErrorCode` | 错误码，`Ok=0` 表成功 |
| `errorMsg` | `string` | 错误信息，**成功时为空字符串** |

- **关键副作用**：仅当 `code != Ok` 时才向 `OnError` 广播一份（来源 `"Record"`，:95）；成功不广播。

---

### 6.8 OnDirectorSelected —— 中控仲裁结果透传

```csharp
public static event Action<string, bool, int, int> OnDirectorSelected;
```

中控仲裁结果透传（含本机和其它客户端），SDK 仅做 pb→基本类型翻译，不做编排（`MVXRSDK.Events.cs:55-60`，由 `internal RaiseDirectorSelected`,  :97-98 触发）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `deviceId` | `string` | 被选中客户端的设备 ID |
| `isPrimary` | `bool` | 是否主位 |
| `slot` | `int` | 槽位下标 |
| `durationSec` | `int` | 持续秒数 |

> **业务一般无需处理本事件**：v3 起"本机被选中"的权威信号改由 `OnPushStreamStarting`（`NotifyLive(start)`）承载，本事件**仅做协议日志/透传**（:57）。判断本机是否要接源推流，请以 `OnPushStreamStarting` 为准，不要依赖本事件。

---

### 6.9 OnDirectorRequestResult —— 切镜请求应答

```csharp
public static event Action<bool> OnDirectorRequestResult;
```

中控对 `DirectorInsert`（切镜）请求的应答（`MVXRSDK.Events.cs:70-75`，由 `internal RaiseDirectorRequestResult`,  :100 触发）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `success` | `bool` | 请求是否被中控受理 |

- **`success=true` 仅表示请求被受理，不代表本机真的被选中推流**——是否被选中仍以 `NotifyLive`（即 `OnPushStreamStarting`）为准（:71-72）。
- 以下情形都触发 `false`：参数校验失败 / WS 未连接 / 应答错误 / `Response.success=false`（:73）。

---

### 6.10 OnError —— 全局错误聚合

```csharp
public static event Action<MVXRSDKErrorCode, string, string> OnError;
```

全局错误聚合事件（v2）。任何域的失败回调都会同步广播一份到这里，便于业务统一接监控/上报（`MVXRSDK.Events.cs:77-82`，由 `internal RaiseError`,  :103-104 触发）。

| 参数 | 类型 | 含义 |
|------|------|------|
| `code` | `MVXRSDKErrorCode` | 错误码 |
| `msg` | `string` | 错误信息 |
| `source` | `string` | 来源模块名（`"Stream"` / `"Record"` / `"Socket"` / `"Room"` 等） |

- **用途**：订阅本事件后，业务无需再分别订阅 `OnPushStreamFailed` / `OnRecordResult` 等做监控（推流失败来源 `"Stream"` :89、录屏失败来源 `"Record"` :95 都会汇聚到这里）。

最小调用示例：

```csharp
MVXRSDK.OnError += (code, msg, source) =>
{
    Debug.LogError($"[MVXRSDK][{source}] {(int)code} {msg}");
    // 统一上报监控
};
```

---

> 以上为 `MVXRSDK` 全部 public 对外事件；所有 `Raise*` 触发方法为 `internal`、`Clear*` 清理方法为 `private`，业务只订阅、不触发。

## 7. 配置与数据类型

本章收录推流相关的对外配置类与数据载荷类型。除特别标注外，均位于命名空间 `MyVerseXRSDK`、`Runtime/Scripts/Operation/Stream/` 下。

### 7.1 StreamConfig（推流参数 POCO）

可变 POCO，承载推流模块全部可调参数。业务侧不直接持有，而是构造一份实例后通过 `MVXRSDK.SetStreamConfig(StreamConfig)` 注入；SDK 内部各 System/Module 统一从全局 `StreamConfig.Active` 读取最新生效配置（`StreamConfig.cs:11`、`:85-86`）。

定义：`public class StreamConfig`（`StreamConfig.cs:11`）。

#### public 字段总表

下表「读取时机」三类含义：
- 协商期读一次：在建立 `PeerConnection`/写 SDP 时读取，推流进行中改不生效，需 Stop→Start（或 UnInit→Init）。
- 每次推流读：每轮推流启动时读取，下次推流自动应用。
- 下一帧立即生效：运行时改动即时生效，无需重启推流。
- InternalRT 常驻：影响一次性创建的 InternalRT，创建后不重建，必须 UnInit → Init 才生效。

| 字段 | 类型 | 默认值 | 约束 | 读取时机 / 生效条件 | 说明 | 源码 |
|------|------|--------|------|----------------------|------|------|
| `Fps` | `int` | `30` | 建议 ≤ XR 主循环频率（PICO 4 = 72/90），>60 编码器会丢帧 | 下一帧立即生效 | 推流目标帧率上限，RT 源按此节流 Blit，相机源落到编码器 maxFramerate | `StreamConfig.cs:20` |
| `StreamMaxLongSide` | `int` | `1280` | ≤0 按 1280 处理；有效下限 16；固定 16:9（1280→1280x720） | InternalRT 常驻，改后需 UnInit→Init | 推流 InternalRT 长边像素，决定 RT 尺寸（与画面源无关） | `StreamConfig.cs:28` |
| `VideoBandwidthKbps` | `int` | `3500` | 不应超过实际上行带宽 | 协商期读一次（写入 SDP `b=AS:N`） | 视频带宽/码率上限（kbps） | `StreamConfig.cs:31` |
| `VideoMinBitrateKbps` | `int` | `1500` | 必须 ≤ `VideoBandwidthKbps`，否则被 libwebrtc 拒绝 | 协商期读一次（落到 `minBitrate`） | 编码器输出码率下限，跳过 GCC 慢启动；局域网建议 1500，弱网 300-800 | `StreamConfig.cs:41` |
| `ForceH264` | `bool` | `true` | webrtc 未编译 H.264 时设 true 会握手期报 CodecNegotiationFailed | 协商期读一次 | 是否强制 H.264（删除 SDP 中其他视频 codec）；局域网+PICO 4U 推荐 true | `StreamConfig.cs:44` |
| `IceGatheringTimeoutSec` | `int` | `3` | non-trickle 模式超时即失败 | 协商期读一次 | ICE 收集超时（秒），局域网 host candidate 一般 <1s 收齐 | `StreamConfig.cs:49` |
| `WhipHttpTimeoutSec` | `int` | `30` | 覆盖 POST 与 DELETE | 每次推流读 | WHIP HTTP 请求超时（秒） | `StreamConfig.cs:52` |
| `WhipHandshakeTimeoutSec` | `int` | `150` | 默认 150 = 30×4 + 12 + 18 buffer | 每次推流读 | WHIP 握手协程全局超时（秒），兜底重试总耗时+死锁 | `StreamConfig.cs:59` |
| `DeleteRetryDelaysMs` | `int[]` | `{1000, 3000, 8000}` | 数组长度=最多重试次数 | 每次推流读 | WHIP DELETE 重试间隔（毫秒） | `StreamConfig.cs:62` |
| `PushStreamRetryDelaysMs` | `int[]` | `{2000, 5000, 10000}` | 数组长度=最多重试次数；空数组=不自动重试；仅可恢复错误（3008/3007/3010/3002）触发，4xx 不触发 | 每次推流读 | 推流自动重试间隔（毫秒），用最近一次 NotifyLive 缓存 URL 自动重连 | `StreamConfig.cs:71` |
| `DisconnectedSelfHealSec` | `int` | `5` | — | 运行时容错（ICE Restart 使用） | `PeerConnectionState=Disconnected` 后自愈等待窗口（秒） | `StreamConfig.cs:76` |
| `StatsReportIntervalMs` | `int` | `1000` | — | 每次推流读（采集循环） | 实时 Stats 采集间隔（毫秒），`OnPushStreamStats`/`GetStreamStats` 节奏 | `StreamConfig.cs:79` |

> 注意：源码字段上的 XML 注释只声明 `Fps` 为「改动下一帧立即生效」（`StreamConfig.cs:18-19`）。其余视频参数（`VideoBandwidthKbps`/`VideoMinBitrateKbps`/`ForceH264`）按类注释「推流进行中修改不会触发已建立 PeerConnection 重新协商，需 Stop → Start」处理（`StreamConfig.cs:7-9`）。`StreamMaxLongSide` 影响常驻 InternalRT，必须 UnInit → Init（`StreamConfig.cs:25-26`）。

#### public 静态成员

```csharp
public static StreamConfig Active { get; }   // StreamConfig.cs:86
```

当前生效配置的只读访问器。SDK 内部读取入口。

> 约束：该属性虽为 public，但源码明确「SDK 内部使用，不要在业务代码持有该引用做读改写」（`StreamConfig.cs:85`）。业务侧应构造新实例后调 `MVXRSDK.SetStreamConfig` 替换，而非原地修改 `Active`。

> `internal static void Apply(StreamConfig)`（`StreamConfig.cs:92`）是 internal，业务不可直接调用——对外等价入口是 `MVXRSDK.SetStreamConfig(StreamConfig)`（内部转调 `Apply`，传 null 恢复默认配置）。

#### 最小调用示例

```csharp
// 弱网场景：降码率下限 + 限分辨率（StreamMaxLongSide 需在 Init 前设好）
var cfg = new StreamConfig
{
    StreamMaxLongSide  = 960,   // → 960x540
    Fps                = 30,
    VideoBandwidthKbps = 2000,
    VideoMinBitrateKbps = 600,  // 必须 ≤ VideoBandwidthKbps
};
MVXRSDK.SetStreamConfig(cfg);   // 转调内部 Apply，替换全局生效配置
```

---

### 7.2 StreamConfigAsset（Inspector 视频编码配置 SO）

ScriptableObject，推流「视频编码参数」的 Unity Asset 入口。仅暴露使用者通常需要调整的 5 项视频编码参数（分辨率 / 帧率 / 码率上限 / 码率下限 / 编码格式）；握手超时、重试节奏、自愈窗口、Stats 间隔等业务逻辑参数**不在此 Asset**，走 SDK 内置默认，确需调整时用 `MVXRSDK.SetStreamConfig` 代码入口（`StreamConfigAsset.cs:5-11`）。

定义：`public sealed class StreamConfigAsset : ScriptableObject`（`StreamConfigAsset.cs:17`）。

#### 创建菜单与挂载

- **CreateAssetMenu 菜单路径**：`Create → MyVerse XR SDK/Stream Config`，默认文件名 `StreamConfig`（`StreamConfigAsset.cs:16`）。
- **用法**：Project 右键 → Create → MyVerse XR SDK → Stream Config 生成 Asset，拖到 `MVXRStreamRig` 的 `streamConfigAsset` 字段；Rig 在 `OnEnable` 时自动调 `Apply()` 写入生效配置（`StreamConfigAsset.cs:12-14`）。

#### Inspector 字段

| 字段 | 类型 | 默认值 | Inspector 约束 | 说明 | 源码 |
|------|------|--------|----------------|------|------|
| `StreamMaxLongSide` | `int` | `1280` | `[Min(16)]` | 推流 InternalRT 长边像素（固定 16:9）；创建后常驻，运行中改需 UnInit→Init | `StreamConfigAsset.cs:27` |
| `Fps` | `int` | `30` | `[Range(1, 60)]` | 推流帧率上限；VR 建议 30，2D 录屏可上 60 | `StreamConfigAsset.cs:35` |
| `VideoBandwidthKbps` | `int` | `3500` | `[Min(100)]` | 视频码率上限（kbps），落到 SDP `b=AS:N` + `sender.maxBitrate` | `StreamConfigAsset.cs:42` |
| `VideoMinBitrateKbps` | `int` | `1500` | `[Min(100)]` | 视频码率下限（kbps），落到 `sender.minBitrate`；须 ≤ `VideoBandwidthKbps` | `StreamConfigAsset.cs:51` |
| `ForceH264` | `bool` | `true` | — | 是否强制 H.264 编码 | `StreamConfigAsset.cs:57` |

> 注意：Asset 仅覆盖上述 5 个视频编码字段；`StreamConfig` 的握手/重试/容错/Stats 字段保留默认值（SDK 内置策略）。Inspector 的 `[Range(1,60)]` 把 `Fps` 上限收到 60，比 `StreamConfig.Fps` 字段本身（无硬上限）更严格。

#### public 方法

```csharp
public StreamConfig ToStreamConfig()   // StreamConfigAsset.cs:65
```
一句话：把 Asset 的 5 个视频编码字段叠加到一份新的 `StreamConfig` 实例并返回。

| 项 | 说明 |
|----|------|
| 返回值 | 新建的 `StreamConfig`，仅覆盖 5 项视频编码字段，其余保留 `StreamConfig` 默认值 |
| 副作用 | 无（不写全局 Active） |
| 异常 | 无显式抛出 |

```csharp
public void Apply()   // StreamConfigAsset.cs:80
```
一句话：把 Asset 配置写入 SDK 全局生效配置，等价于 `MVXRSDK.SetStreamConfig(ToStreamConfig())`。

| 项 | 说明 |
|----|------|
| 返回值 | 无 |
| 副作用 | 调 `MVXRSDK.SetStreamConfig`，替换全局 `StreamConfig.Active` |
| 异常 | 无显式抛出 |
| 关联 | `MVXRStreamRig` 在 `OnEnable` 自动调用（`StreamConfigAsset.cs:13-14`） |

> 区分：业务 Inspector 流程用 `StreamConfigAsset.Apply()`（public，Asset 自带）；纯代码流程用 `MVXRSDK.SetStreamConfig(cfg)`。`StreamConfig` 上的同名 `Apply` 是 internal，业务不可调。

---

### 7.3 DirectorSource（切镜机位来源常量）

静态常量类，承载 `DirectorInsert.source` 机位来源的合法字符串值。proto 注释为语义权威：`"unity"`=本机 Unity 游戏内机位；空字符串/`"mr"`=原直播。空字符串是合法协议值（=原直播），SDK 不做默认填充（`StreamDefine.cs:33-45`）。

定义：`public static class DirectorSource`（`StreamDefine.cs:38`）。

| 常量 | 类型 | 值 | 说明 | 源码 |
|------|------|----|------|------|
| `Unity` | `const string` | `"unity"` | 本机 Unity 游戏内机位（具体推哪个相机是本地决策，中控不感知） | `StreamDefine.cs:41` |
| `Mr` | `const string` | `"mr"` | 原直播（播控第一视角）；空字符串等效 | `StreamDefine.cs:44` |

---

### 7.4 DirectorRequestOptions（切镜请求参数）

`struct`（值类型），切镜请求参数集，对应 pb `DirectorInsert.Types.Request` 的 5 个字段。与 `StartRecordOptions` 同风格，协议加字段时在此扩展不破坏调用方（`StreamDefine.cs:47-67`）。作为 `MVXRSDK.SendDirectorRequest(...)` 的入参。

定义：`public struct DirectorRequestOptions`（`StreamDefine.cs:51`）。

| 字段 | 类型 | 含义 | 约束 / 默认 | 源码 |
|------|------|------|-------------|------|
| `Source` | `string` | 机位来源，见 `DirectorSource`。空=原直播，SDK 原样透传 | struct 默认 `null`；camera 自动接源重载下留空会被自动填为 `"unity"`（传相机即明确本机机位） | `StreamDefine.cs:55` |
| `Lenses` | `int` | 镜头数：1=单镜头全屏 / 2=双拼 / 3=品字 / 4=2x2 四宫格 | `<1` 时按 1 处理 | `StreamDefine.cs:58` |
| `DurationSec` | `int` | 这步持续秒数 | 必须 `>0`；到期由服务端停流，客户端不做本地倒计时 | `StreamDefine.cs:61` |
| `Record` | `bool` | 是否录制这一段（服务端执行） | struct 默认 `false` | `StreamDefine.cs:64` |
| `FileName` | `string` | 录制文件名（不含扩展名），仅 `Record=true` 时有意义 | struct 默认 `null`，SDK 构造时规整为空串，原样透传不校验 | `StreamDefine.cs:67` |

> 提示：`DirectorRequestOptions` 是 struct，未显式赋值的字段取该类型零值（`Source=null`、数值=0、`Record=false`、`FileName=null`）。`DurationSec` 必须显式设 >0，`Lenses` 不设则被按 1 处理；`FileName` 不设等效不指定录制文件名。

#### 最小调用示例

```csharp
// 请求本机 Unity 机位、单镜头全屏、持续 50 秒、录制并命名文件；传相机走自动接源重载
var opts = new DirectorRequestOptions
{
    Source      = DirectorSource.Unity, // 或留空由 camera 重载自动填 "unity"
    Lenses      = 1,
    DurationSec = 50,
    Record      = true,
    FileName    = "battle-round-3", // 仅 Record=true 时有意义，不含扩展名
};
MVXRSDK.SendDirectorRequest(opts, myCamera);
```

---

### 7.5 StartRecordOptions（录屏启动参数）

`sealed class`，启动录屏的参数集，对应 pb `StartRecord.Types.Request` 的 5 个字段。所有字段由游戏侧传入，SDK 仅做参数转发不做实际录制（`StreamDefine.cs:11-31`）。

定义：`public sealed class StartRecordOptions`（`StreamDefine.cs:15`）。

| 字段 | 类型 | 默认值 | 含义 | 源码 |
|------|------|--------|------|------|
| `RealCamera` | `bool` | `false` | 是否为真实摄像头；true 时使用 `CameraId`，false 时使用 `PicoDeviceId` | `StreamDefine.cs:18` |
| `CameraId` | `string` | `string.Empty` | 真实摄像头 ID（`RealCamera=true` 时使用） | `StreamDefine.cs:21` |
| `DurationSec` | `int` | `0` | 录制时长（秒），到时由服务端自动停止 | `StreamDefine.cs:24` |
| `FileName` | `string` | `string.Empty` | 录制文件名（不含扩展名） | `StreamDefine.cs:27` |
| `PicoDeviceId` | `string` | `string.Empty` | Pico 设备 ID（`RealCamera=false` 时使用） | `StreamDefine.cs:30` |

> `RealCamera` 决定 `CameraId` 与 `PicoDeviceId` 二选一生效。

---

### 7.6 StreamStats（推流实时统计快照）

`sealed class`，推流实时统计快照。由 WebRTCSystem 每 `StreamConfig.StatsReportIntervalMs`（默认 1000ms）采集一次，通过 `MVXRSDK.OnPushStreamStats` 事件回调与 `MVXRSDK.GetStreamStats()` 同步查询两条路径暴露（`StreamStats.cs:3-14`）。

数据来源（com.unity.webrtc 3.0.0-pre.8）：`RTCOutboundRTPStreamStats`（发送字节/包数/编码帧数）、`RTCRemoteInboundRtpStreamStats`（远端丢包/jitter/RTT）、`RTCIceCandidatePairStats`（可用上行带宽/RTT）。字段为 0 表示该指标在本周期未拿到（如握手刚完成时 remote-inbound 还没回报）（`StreamStats.cs:6-12`）。

定义：`public sealed class StreamStats`（`StreamStats.cs:14`）。

| 字段 | 类型 | 含义 | 来源 | 源码 |
|------|------|------|------|------|
| `BytesSent` | `long` | 累计发送字节数（视频+音频） | outbound-rtp | `StreamStats.cs:17` |
| `PacketsSent` | `long` | 累计发送包数 | outbound-rtp | `StreamStats.cs:20` |
| `PacketsLost` | `long` | 远端反馈：累计丢包数 | remote-inbound | `StreamStats.cs:23` |
| `JitterMs` | `double` | 远端反馈：网络抖动（毫秒） | remote-inbound | `StreamStats.cs:26` |
| `RttMs` | `double` | 当前 ICE 链路 RTT（毫秒） | candidate-pair | `StreamStats.cs:29` |
| `FramesEncoded` | `int` | 视频累计编码帧数 | outbound-rtp | `StreamStats.cs:32` |
| `VideoBitrateKbps` | `int` | 近 1 秒视频发送码率（kbps），由 bytesSent 增量计算 | 计算值 | `StreamStats.cs:35` |
| `AvailableOutgoingBitrateKbps` | `int` | BWE 估算的可用上行带宽（kbps），mediamtx 端给出；0=未上报 | candidate-pair | `StreamStats.cs:38` |
| `Timestamp` | `float` | 本次采样的时间戳（秒，`Time.realtimeSinceStartup`） | 本地 | `StreamStats.cs:41` |

> 字段语义：`BytesSent`/`PacketsSent`/`PacketsLost`/`FramesEncoded` 为累计量（单调递增），`VideoBitrateKbps`/`JitterMs`/`RttMs` 为瞬时/近窗口量。判断「是否在持续发包」应比较相邻两次快照的累计量增量，而非看绝对值。

---

> 附：本章未收录的相关推流类型属于 SDK 内部，业务不可用——`StreamConfig.Apply`（internal，`StreamConfig.cs:92`）、枚举 `PushStreamState`（internal，`StreamDefine.cs:4`）。对外等价/可用入口分别为 `MVXRSDK.SetStreamConfig` 与 `MVXRSDK.State`（推流连接态查询走 facade）。

## 8. 画面源与组件

本节收录推流画面源抽象 `IStreamSource` 及其两个 SDK 内置实现（`RenderTextureStreamSource` / `CameraStreamSource`），以及业务在 Inspector 挂载的 `MVXRStreamRig` 组件；末尾对 SDK 自管的 `NetworkTransform` 组件作"了解即可"说明。

画面源通过 `MVXRSDK.SetStreamSource(IStreamSource)` 接入推流链路（见第 7 节推流 API）。SDK 维护一张长生命周期 InternalRT（绑定 VideoStreamTrack），切源不断流：`SetStreamSource` 时只 `Detach` 旧源 + `Attach` 新源到同一张 RT；InternalRT 为固定尺寸（`StreamConfig.StreamMaxLongSide` 按 16:9，默认 1280x720），任意源可热切。

### 8.1 IStreamSource（画面源接口）

`MyVerseXRSDK.IStreamSource`，`public interface : IDisposable`，定义于 `IStreamSource.cs:16`。

业务侧可自行实现本接口（XR 立体合成、多摄拼接、屏幕捕获等"喂画面工具"），把画面渲染到 SDK 内部 RT。SDK 内置 2 个实现：`RenderTextureStreamSource`、`CameraStreamSource`。

> 注意：实现自定义画面源时，禁止"从主相机 / eye buffer / 屏幕抓帧"走 Vulkan（会复现 FDM 注视点渲染下的画面错乱，见项目约定 PICO Vulkan 风险）。推流源应是独立相机直渲普通 RT。

#### 属性

| 成员 | 类型 | 含义 | (file:line) |
|------|------|------|-------------|
| `Width` | `int` { get; } | 画面源宽（像素，仅信息展示，不参与 InternalRT 尺寸决策） | IStreamSource.cs:19 |
| `Height` | `int` { get; } | 画面源高（像素，仅信息展示） | IStreamSource.cs:22 |
| `DisplayName` | `string` { get; } | 日志/调试展示名，如 `"Camera(MainCam)"` / `"RT(1280x720)"` | IStreamSource.cs:25 |

#### 方法

```csharp
void Attach(RenderTexture targetRT);
```
绑定到 SDK 内部 RT。实现要把自己的画面渲染/Blit 到 `targetRT`。约束：同一 source 在被 `Detach` 之前不会被重复 `Attach`。`IStreamSource.cs:31`

| 参数 | 类型 | 含义 |
|------|------|------|
| `targetRT` | `RenderTexture` | SDK 内部推流 RT（固定尺寸） |

```csharp
void Detach();
```
解绑。实现需还原它对外部对象的修改（如 `Camera.targetTexture` 还原）、取消 `LateUpdate` 钩子等。`Detach` 后允许再次 `Attach`（同一或不同 `targetRT`）。`IStreamSource.cs:37`

```csharp
void Dispose();   // 继承自 IDisposable
```
释放资源。SDK 内置实现的 `Dispose` 会先 `Detach` 再清空内部引用。

#### 事件

| 成员 | 类型 | 触发时机 | (file:line) |
|------|------|----------|-------------|
| `OnAttached` | `event Action` | 本 source 被 SDK `Attach` 后（已开始喂画面）；Director 切回时也会触发。上层可据此恢复采集 | IStreamSource.cs:43 |
| `OnDetached` | `event Action` | 本 source 被 SDK `Detach` 后（已不再喂画面，但仍可能再次被 `Attach`）；Director 切走时触发。上层可据此暂停采集，避免空跑 GPU | IStreamSource.cs:49 |

### 8.2 RenderTextureStreamSource（RT 画面源）

`MyVerseXRSDK.RenderTextureStreamSource`，`public sealed class : IStreamSource`，定义于 `RenderTextureStreamSource.cs:15`。

业务侧自己渲染到任意格式 RT，SDK 在 `LateUpdate` 每帧 `Graphics.Blit` 转换格式 + 写到推流 RT（格式/尺寸不匹配由 Blit 隐式处理：双线性缩放 + 格式转换，非 16:9 外部 RT 会被拉伸）。性能：每帧 1 次 GPU Blit，PICO 4U 上约 0.2-0.5ms GPU。

#### 构造

```csharp
public RenderTextureStreamSource(RenderTexture externalRT)
```
`RenderTextureStreamSource.cs:37`

| 参数 | 类型 | 含义 |
|------|------|------|
| `externalRT` | `RenderTexture` | 业务侧渲染目标 RT，SDK 每帧从它 Blit 到推流 RT |

约束：v3 不再传目标尺寸——InternalRT 固定尺寸，`BlitTick` 的 `Graphics.Blit` 自动做尺寸/格式转换。

#### 成员

| 成员 | 类型/签名 | 说明 | (file:line) |
|------|-----------|------|-------------|
| `Width` | `int` { get; } | 外部 RT 原始宽；`externalRT == null` 时返回 0 | RenderTextureStreamSource.cs:24 |
| `Height` | `int` { get; } | 外部 RT 原始高；同上 | RenderTextureStreamSource.cs:25 |
| `DisplayName` | `string` { get; } | `"RT({w}x{h})"`；已 Dispose 时为 `"RT(disposed)"` | RenderTextureStreamSource.cs:26 |
| `OnAttached` | `event Action` | 同接口语义 | RenderTextureStreamSource.cs:30 |
| `OnDetached` | `event Action` | 同接口语义 | RenderTextureStreamSource.cs:31 |
| `Attach(RenderTexture targetRT)` | `void` | 记录 targetRT 并注册 `MonoSystem.AddLateUpdateListener(BlitTick)` | RenderTextureStreamSource.cs:42 |
| `Detach()` | `void` | 移除 LateUpdate 钩子、清空 targetRT | RenderTextureStreamSource.cs:62 |
| `Dispose()` | `void` | 先 `Detach()` 再置空 `externalRT` | RenderTextureStreamSource.cs:91 |

#### 关键副作用 / 约束

- 重复 `Attach`（已 attached）：忽略并打 Warning（`RenderTextureStreamSource.cs:46`）；不重复触发 `OnAttached`。
- `externalRT == null` 或 `targetRT == null`：`Attach` 打 Error 并直接返回，不进入 attached 态（`RenderTextureStreamSource.cs:49-53`）。
- 未 attached 时 `Detach`：直接返回（`RenderTextureStreamSource.cs:64`）。
- 帧率节流：每帧从 `StreamConfig.Active.Fps` 读上限，业务改 fps 立即生效；`fps <= 0` 表示不节流（`RenderTextureStreamSource.cs:80-86`）。
- 推流期间业务销毁了外部 RT：`BlitTick` 有 null 保护，跳过 Blit 不抛异常（`RenderTextureStreamSource.cs:76`）。
- 回调异常隔离：`OnAttached/OnDetached` 回调内抛异常会被 catch 并打 Error，不影响 SDK 状态机（`RenderTextureStreamSource.cs:58-59`、`69-70`）。

#### 最小调用示例

```csharp
// 业务自渲到 myRT，交给 SDK 推流
var source = new RenderTextureStreamSource(myRT);
MVXRSDK.SetStreamSource(source);   // 见第 7 节
// 停流/切场景前
source.Dispose();
```

### 8.3 CameraStreamSource（相机画面源）

`MyVerseXRSDK.CameraStreamSource`，`public sealed class : IStreamSource`，定义于 `CameraStreamSource.cs:20`。

让 Camera 直接渲染到推流 RT，零额外 GPU Blit（`Camera.targetTexture = RT` 后渲染管线直接输出到该 RT，72/90Hz 下 GPU 开销 ≈ 0）。推荐用法：业务侧专门挂一台"直播相机"，SDK 接管 `targetTexture`。多数情况下无需手动 new——`MVXRSDK.SendDirectorRequest(opts, camera)` 在被中控选中后会自动把相机包成 `CameraStreamSource` 接上，停流自动清（见第 7 节）。

#### 构造

```csharp
public CameraStreamSource(Camera camera)
```
`CameraStreamSource.cs:40`

| 参数 | 类型 | 含义 |
|------|------|------|
| `camera` | `Camera` | 直播相机，Attach 后 SDK 接管其 `targetTexture` 与 `enabled` |

约束：v3 不再传宽高——InternalRT 固定尺寸（`StreamConfig.StreamMaxLongSide` 按 16:9），相机 attach 后按 RT 尺寸渲染，比例永远正确。

#### 成员

| 成员 | 类型/签名 | 说明 | (file:line) |
|------|-----------|------|-------------|
| `Width` | `int` { get; } | attach 后推流 RT 宽；未 attach 时为 0 | CameraStreamSource.cs:29 |
| `Height` | `int` { get; } | attach 后推流 RT 高；未 attach 时为 0 | CameraStreamSource.cs:30 |
| `DisplayName` | `string` { get; } | `"Camera({cam.name})"`；已 Dispose 时为 `"Camera(disposed)"` | CameraStreamSource.cs:31 |
| `OnAttached` | `event Action` | 同接口语义 | CameraStreamSource.cs:33 |
| `OnDetached` | `event Action` | 同接口语义 | CameraStreamSource.cs:34 |
| `Attach(RenderTexture targetRT)` | `void` | 接管相机渲染到 targetRT，注册自保护 tick | CameraStreamSource.cs:45 |
| `Detach()` | `void` | 还原相机 `targetTexture`/`enabled`，摘除自保护 tick | CameraStreamSource.cs:72 |
| `Dispose()` | `void` | 先 `Detach()` 再置空 camera 引用 | CameraStreamSource.cs:105 |

> `SelfProtectTick` 为 `private`（`CameraStreamSource.cs:99`），非公开成员，不在业务 API 范围。

#### 对 Camera 的改动与还原（重点）

`Attach` 时（`CameraStreamSource.cs:57-62`）：
1. 快照 `m_OriginalTarget = camera.targetTexture`、`m_OriginalEnabled = camera.enabled`；
2. `camera.targetTexture = targetRT`；
3. 强制 `camera.enabled = true`（业务直播相机平时可 `enabled=false` 不上屏、不烧 GPU，被中控选中接源时由 SDK 临时启用）。

> Unity 规则：有 `targetTexture` 的相机不上屏，因此玩家屏幕始终只看到主视角相机。

`Detach` 时（`CameraStreamSource.cs:76-86`）：
1. **仅当** `camera.targetTexture` 仍是 SDK 设置的那张 RT 时才还原回 `m_OriginalTarget`——业务若中途自己改了 `targetTexture`，SDK 不覆盖业务的赋值；
2. 还原 `camera.enabled = m_OriginalEnabled`（Attach 前默认 disabled 的相机切回后继续不渲染）。

#### 相机销毁自保护

Attach 期间每帧检查相机是否已被销毁（典型：场景卸载前业务未 `ClearStreamSource`）。检测到 `camera == null`（Unity 空）时，自动调 `TextureProviderSystem.HandleSourceCameraDestroyed(this)` 让 SDK 清源推黑帧，避免悬挂引用（`CameraStreamSource.cs:99-103`）。这是误用兜底——多场景约定仍应在场景卸载前 `ClearStreamSource()`。

#### 其他副作用 / 约束

- 重复 `Attach`：忽略并打 Warning（`CameraStreamSource.cs:47-51`）。
- `camera == null` 或 `targetRT == null`：`Attach` 打 Error 并返回（`CameraStreamSource.cs:52-56`）。
- 未 attached 时 `Detach`：直接返回（`CameraStreamSource.cs:74`）。
- 回调异常隔离：同 `RenderTextureStreamSource`（`CameraStreamSource.cs:68-69`、`91-92`）。

#### 最小调用示例

```csharp
// 通常不必手 new——直接交给切镜 API 自动接源：
MVXRSDK.SendDirectorRequest(opts, liveCamera);   // 被选中后 SDK 自动包成 CameraStreamSource

// 需要手动精细控制时：
var source = new CameraStreamSource(liveCamera);
MVXRSDK.SetStreamSource(source);
// 场景卸载前
source.Dispose();
```

### 8.4 MVXRStreamRig（推流装配组件）

`MyVerseXRSDK.Streaming.MVXRStreamRig`，`public sealed class : MonoBehaviour`，定义于 `MVXRStreamRig.cs:20`。

- 特性：`[AddComponentMenu("MyVerse XR SDK/Stream Rig")]`、`[DisallowMultipleComponent]`（`MVXRStreamRig.cs:18-19`）。
- 命名空间是 `MyVerseXRSDK.Streaming`（与画面源类的 `MyVerseXRSDK` 不同，引用时注意）。

v3 切镜化重构后，本组件**不承载画面推流**（画面源管理由业务直接调 SDK 公共 API：推荐 `SendDirectorRequest(opts, camera)`，或手动订阅 `OnPushStreamStarting` 接源 / `OnPushStreamStopped` 清源）。Rig 职责仅两项：`OnEnable` 时应用 `StreamConfigAsset`（写入 `StreamConfig.Active`）、装配游戏音采集（`AudioListener` tap → 推流 PCM）。推流不含麦克风语音（SDK 不碰麦克风设备）。

#### Inspector 字段（public）

| 字段 | 类型 | Header | 含义 | (file:line) |
|------|------|--------|------|-------------|
| `gameAudioListener` | `AudioListener` | 音频源 | 游戏音 `AudioListener`，通过 `OnAudioFilterRead` 抓 master mix；**留空则不推游戏音** | MVXRStreamRig.cs:24 |
| `streamConfigAsset` | `StreamConfigAsset` | 推流配置 Asset | 推流视频编码参数 Asset（Fps / InternalRT 长边 / 码率 / H.264）；`OnEnable` 时自动 `Apply()` 写入 `StreamConfig.Active`；**留空时全部走 SDK 默认**（Fps=30 / 长边 1280 / 码率 3500 / H.264 强制） | MVXRStreamRig.cs:31 |

> `StreamConfigAsset` 创建方式：Project 右键 → Create → MyVerse XR SDK → Stream Config（其 public 成员见配置章节）。

#### 生命周期行为

- `OnEnable`（`MVXRStreamRig.cs:35`）：若 `streamConfigAsset != null` 则 `Apply()`；若 `gameAudioListener != null` 则创建游戏音采集器。
- `OnDisable`（`MVXRStreamRig.cs:48`）：释放并清空游戏音采集器。

本组件无 public 方法，业务只需在场景里挂上并在 Inspector 配置上述两个字段。

### 8.5 NetworkTransform（SDK 自管组件，了解即可）

`MyVerseXRSDK.NetworkTransform`，`public class : MonoBehaviour`，定义于 `NetworkTransform.cs:8`。

> **SDK 内部管理，了解即可，一般无需直接操作。** 这是 SDK 网络同步的内部组件：`RegisterSelfNode` 时自动挂 Reporter（上报本地位姿），远端虚影自动挂 Receiver（接收并平滑应用位姿）。业务通常不手动添加或操作此组件。

`messageType` 字段为 `internal`（`NetworkTransform.cs:44`），由 SDK 装配时设置，业务方跨程序集不可直接赋值。

#### MessageType 枚举（注意 int 值顺序）

`public enum NetworkTransform.MessageType`，定义于 `NetworkTransform.cs:33`：

| 枚举值 | (int) | 含义 | (file:line) |
|--------|-------|------|-------------|
| `None` | 0 | 未设角色（默认值） | NetworkTransform.cs:36 |
| `Reporter` | 1 | 上报者：上报本地位姿，不处理外部数据 | NetworkTransform.cs:38 |
| `Receiver` | 2 | 接收者：接收并应用外部位姿，不上报 | NetworkTransform.cs:40 |

#### public 方法（了解即可）

| 签名 | 说明 | 约束 | (file:line) |
|------|------|------|-------------|
| `void SmoothMove(Vector3 targetPosition, Vector3 targetEulerAngles)` | 设置目标位姿，由 SDK 每帧驱动的 `OnUpdate`（经 `MonoSystem` 注册的 Update 钩子）插值平滑逼近（位置 Lerp / 旋转 Slerp，到阈值内吸附）。兼容旧调用 | 仅 `Receiver` 生效：非 Receiver 直接返回（`NetworkTransform.cs:188`） | NetworkTransform.cs:185 |
| `void SetRole(MessageType type)` | 设置角色（写 `messageType`）；与当前相同则忽略 | — | NetworkTransform.cs:290 |
| `void StartUpload()` | 手动启动定时上报（当前为占位实现，方法体未实现上报逻辑） | 仅 `Reporter` 生效：非 Reporter 直接返回（`NetworkTransform.cs:236`） | NetworkTransform.cs:233 |

> 行为说明：`Receiver` 在 `OnUpdate` 做插值并按移动状态播放 idle/move 动画，且每秒做一次距离可见性判定（超 `MVXRSDKConfig.NORMAL_DISTANCE` 隐藏远端虚影，`NetworkTransform.cs:156-181`）；`Reporter` 按 `uploadInterval` 节流上报本地 XZ 位移与 Y 旋转（`NetworkTransform.cs:122-151`）。上报要求房间已分配（`RoomAllocationStatus.Allocated`）且 `DeviceId(SN)` 非空，否则中止并打 Error（`NetworkTransform.cs:249-257`）。

> `StreamConfigAsset`（`MVXRStreamRig.streamConfigAsset` 字段类型）的成员见 [§7.2](#72-streamconfigassetinspector-视频编码配置-so)；`MVXRSDK.SetStreamSource` / `SendDirectorRequest` / `OnPushStreamStarting` 等推流 API 见 [§5](#5-推流--切镜--录屏方法) 与 [§6](#6-事件)。

## 9. 枚举与错误码

本章列出 SDK 业务面对外的所有 public 枚举、状态常量与错误码。
internal 枚举（`RoomAllocationStatus` / `ObstacleType` / `RoomStatusType` / `PushStreamState`）不在收录范围，业务方跨程序集不可见。

### 9.1 InitMode — SDK 启动模式

决定 `InitMVXRSDK` 是否拉起网络阶段（HTTP / WS）。`(int)` 值稳定，可直接 cast。
(MVXRSDKDefine.cs:41-49)

| 成员 | 值 | 含义 | 网络阶段 |
|------|----|------|----------|
| `Production` | 0 | 正式模式：本地 HTTP(`localhost:8868`) 拉 WS 地址 → 连 WS → 登录。业务方默认走这条 | 拉起 |
| `WsDirect` | 1 | 测试模式：跳过 HTTP，外部传入中控服地址直连+登录，用于无中控环境验证网络模块 | 拉起（跳过 localhost HTTP） |
| `Offline` | 2 | 测试模式：完全离线，只装配本地 Manager（测试推流/节点等本地能力） | 跳过 |

> 注：`WsDirect` 模式下外部传入的是中控服地址（非房间服 WS 地址），房间服 WS 地址仍由中控分配返回。详见启动流程章节。

```csharp
// 三档启动示例
MVXRSDK.InitMVXRSDK(picoId);                                  // 等价 Production（单参重载默认 Production）
MVXRSDK.InitMVXRSDK(picoId, InitMode.WsDirect, "ws://...");   // 直连中控地址
MVXRSDK.InitMVXRSDK(picoId, InitMode.Offline);               // 纯本地
```

### 9.2 MVXRSDKState — SDK 生命周期状态机

业务方通过 `MVXRSDK.State` 查询当前阶段，配合 `MVXRSDK.IsReady` / `MVXRSDK.IsConnected` 判断业务 API 是否可调。
(MVXRSDKState.cs:7-23)

| 成员 | 值 | 迁移含义 | 此态可调能力 |
|------|----|----------|--------------|
| `NotInitialized` | 0 | 进程启动 / `UnInit` 后的初始态 | 禁止调任何业务 API |
| `Initializing` | 1 | `InitMVXRSDK` 调用中，本地阶段未完成 | 无 |
| `LocalReady` | 2 | 本地 Manager 装配完成；**Offline 模式终态** | 可调 `SetStreamSource` 等本地能力 |
| `Connecting` | 3 | Production/WsDirect：HTTP 配置拉取 / 房间分配轮询 / WS 握手 / 登录中 | 仅本地能力 |
| `Connected` | 4 | WS 已连接且登录成功；房间已分配 | 可调所有业务 API |
| `Disconnected` | 5 | 曾 `Connected` 后掉线（含 reconnect 中） | 业务侧应等待自愈或调 `UnInit` |
| `Disposed` | 6 | `UnInitMVXRSDK` 完毕 | 下次 `InitMVXRSDK` 回到 `NotInitialized → Initializing` 流程 |

典型迁移路径：

- **Production / WsDirect**：`NotInitialized → Initializing → LocalReady → Connecting → Connected`；掉线 `→ Disconnected`（自愈可回 `Connected`）；`UnInit → Disposed`
- **Offline**：`NotInitialized → Initializing → LocalReady`（终态，不进入 `Connecting`）；`UnInit → Disposed`

### 9.3 StreamStopReason — 推流停止原因

`OnPushStreamStopped` 回调携带，替代旧版歧义 bool 参数，用于区分停流来源。
(StreamStopReason.cs:6-18)

| 成员 | 值 | 含义 |
|------|----|------|
| `ServerStop` | 0 | 业务方主动调 Stop / 中控 `NotifyLive(start=false)` |
| `UserStop` | 1 | SDK 内部主动停止（如 `NotifyLive` 切 URL 时断旧重连） |
| `NetworkLost` | 2 | 网络异常断流 / WS 重连耗尽 |
| `ConfigChanged` | 3 | URL 变更触发的断旧准备启新 |
| `SdkUnInit` | 4 | SDK 反初始化（`UnInitMVXRSDK`）触发的强制停流，用于区分用户主动 stop |

> 注：源码定义了 5 个取值（含 `SdkUnInit=4`），任务清单仅点名前 4 个；此处以源码为准完整列出。`ServerStop=0` 与 `UserStop=1` 的命名语义与直觉相反（`ServerStop` 含业务主动 Stop，`UserStop` 指 SDK 内部主动），引用时以本表说明为准。

### 9.4 DirectorSource — 切镜机位来源常量

`static class` 常量容器（非枚举），对应 `DirectorRequestOptions.Source` / `DirectorInsert.source` 字段。
(StreamDefine.cs:38-45)

```csharp
public static class DirectorSource
{
    public const string Unity = "unity"; // 本机 Unity 游戏内机位（推哪个相机本地决策，中控不感知）
    public const string Mr    = "mr";    // 原直播（播控第一视角）；空字符串等效
}
```

| 常量 | 字符串值 | 含义 |
|------|----------|------|
| `DirectorSource.Unity` | `"unity"` | 本机 Unity 游戏内机位 |
| `DirectorSource.Mr` | `"mr"` | 原直播（播控第一视角），与空字符串等效 |

> 约束：空字符串是合法协议值（= 原直播），SDK **不做默认填充**。但 `SendDirectorRequest(opts, camera)` 自动接源重载下，`Source` 留空会被自动填为 `"unity"`（传相机即明确本机机位）。

### 9.5 MVXRSDKErrorCode — 统一错误码

按业务域分段，每段保留约 100 号余量便于扩展。数值序号是稳定契约——业务方上报埋点 / 服务端日志可直接 `(int)` cast。
(MVXRSDKErrorCode.cs:7-70)

`Ok = 0` 仅在结构化结果对象中使用，**事件不会带此值**。(MVXRSDKErrorCode.cs:9-10)

**0 — 成功**

| 成员 | 值 | 含义 |
|------|----|------|
| `Ok` | 0 | 成功（仅结构化结果对象使用，事件不带此值） |

**1xxx — 通用 / 状态** (MVXRSDKErrorCode.cs:13-17)

| 成员 | 值 | 含义 |
|------|----|------|
| `NotInitialized` | 1001 | SDK 未初始化即调业务 API |
| `AlreadyInitialized` | 1002 | 重复初始化 |
| `InvalidArgument` | 1003 | 参数非法 |
| `InvalidState` | 1004 | 当前状态机阶段不允许该操作 |
| `Unknown` | 1099 | 未分类错误 |

**2xxx — 网络 / Socket** (MVXRSDKErrorCode.cs:20-27)

| 成员 | 值 | 含义 |
|------|----|------|
| `SocketNotConnected` | 2001 | WS 未连接 |
| `SocketConnectFailed` | 2002 | WS 连接失败 |
| `SocketReconnectExhausted` | 2003 | 重连次数耗尽（退避 2-30s，最多 5 次） |
| `SocketKickedOut` | 2004 | 被服务端踢下线 |
| `ProtobufParseFailed` | 2005 | Protobuf 帧解析失败 |
| `HttpRequestFailed` | 2006 | HTTP 请求失败 |
| `HttpResponseInvalid` | 2007 | HTTP 响应内容非法 |
| `RequestTimeout` | 2008 | 请求超时 |

**3xxx — 房间 / 中控** (MVXRSDKErrorCode.cs:30-33)

| 成员 | 值 | 含义 |
|------|----|------|
| `RoomAllocateFailed` | 3001 | 房间分配失败 |
| `RoomDisbanded` | 3002 | 房间已解散 |
| `LoginFailed` | 3003 | 登录失败 |
| `RoomNotAllocated` | 3004 | 房间未分配 |

**4xxx — 推流**（覆盖旧 `StreamErrorCode`）(MVXRSDKErrorCode.cs:37-51)

> 备注：`NoStreamSource` 当前 `PushStreamModule` 不再触发（无源启动推黑帧），仅 `WebRTCSystem` 对 null RT 的预检使用。(MVXRSDKErrorCode.cs:36)

| 成员 | 值 | 含义 |
|------|----|------|
| `NoStreamSource` | 4001 | 无画面源（见上备注） |
| `InvalidStreamUrl` | 4002 | 推流 URL 非法 |
| `InvalidStreamSourceSize` | 4003 | RT 尺寸低于 webrtc 编码器最小（minWidth=145 / minHeight=49），常因 Editor Game View 过小或 PICO XR fallback 异常 |
| `WebRTCInitFailed` | 4101 | WebRTC 初始化失败 |
| `IceGatheringTimeout` | 4102 | ICE 收集超时 |
| `WhipPostFailed` | 4103 | WHIP POST 失败 |
| `WhipAuthFailed` | 4104 | WHIP 鉴权失败 |
| `WhipStreamNotFound` | 4105 | WHIP 目标流不存在 |
| `SdpNegotiationFailed` | 4106 | SDP 协商失败 |
| `DtlsHandshakeFailed` | 4107 | DTLS 握手失败 |
| `IceConnectionFailed` | 4108 | ICE 连接失败 |
| `CodecNegotiationFailed` | 4109 | 编解码协商失败 |
| `ConnectionLost` | 4110 | 推流连接丢失 |

**5xxx — 录屏** (MVXRSDKErrorCode.cs:54-59)

| 成员 | 值 | 含义 |
|------|----|------|
| `RecordInvalidOptions` | 5001 | 录屏参数非法 |
| `RecordNotConnected` | 5002 | 未连接时发起录屏 |
| `RecordAlreadyRecording` | 5003 | 已在录屏中 |
| `RecordRemoteRejected` | 5004 | 远端拒绝 |
| `RecordTimeout` | 5005 | 录屏信令超时 |
| `RecordParseFailed` | 5006 | 录屏响应解析失败 |

**6xxx — 节点 / 空间** (MVXRSDKErrorCode.cs:62-64)

| 成员 | 值 | 含义 |
|------|----|------|
| `NodeNull` | 6001 | 注册节点为 null |
| `NodeAlreadyRegistered` | 6002 | 节点重复注册 |
| `XROffsetAfterInit` | 6003 | Init 后再注册 XROffset 节点 |

> 注：`NodeAlreadyRegistered(6002)` / `XROffsetAfterInit(6003)` 与 v2 现行约定"节点注册任意时机皆可、重复同节点幂等、不同节点热替换"表面冲突。此处如实列出码值，但**节点注册当前为任意时机皆可，该码是否实际触发以实现为准**。

**7xxx — 积分** (MVXRSDKErrorCode.cs:67-69)

| 成员 | 值 | 含义 |
|------|----|------|
| `TransactionFailed` | 7001 | 积分交易失败 |
| `TransactionInProgress` | 7002 | 已有交易进行中 |
| `TransactionBaseUrlMissing` | 7003 | BaseUrl 缺失（如 Offline 模式无网络阶段，`BaseUrl` 为空） |

## 10. 日志

SDK 内部日志类 `MVXRSDKLog`（`Runtime/Scripts/System/Log/MVXRSDKLog.cs:22`）当前声明为 **`internal static class`**——仅在 SDK 程序集内可见，**业务方无法直接调用** 其 `SetMinLevel` / `SetTag` / `SetEnabled` / `SetPrefixProvider` 等运行时方法（跨程序集编译不通过）。

业务方可用的日志控制只有**编译期开关**一种：

| 手段 | 做法 | 效果 |
| :--- | :--- | :--- |
| `MVXRSDK_LOG_DISABLED` | Player Settings → Scripting Define Symbols 添加该宏 | SDK 全部日志在编译期裁剪为不输出（零运行时开销） |

> 说明：早期文档曾示例业务调用 `MVXRSDKLog.SetMinLevel(...)`，与当前可见性（`internal`）不符，已更正。若业务确需运行时调节 SDK 日志级别，需由 SDK 将 `MVXRSDKLog` 改为 `public`（目前为已知限制）。

## 11. 附录：非业务面 public 类型

以下类型在源码中为 `public`（按 UPM 分发会进入消费者 IntelliSense），但属 SDK 内部基础设施或第三方 / 生成代码，**不是业务面 API，请勿在业务代码中使用**，后续可能收敛为 `internal`：

- 基础设施 System：`SocketSystem`、`PoolSystem` 及其对象池数据 / 模块类
- Socket 实现：`SocketModule` / `MessageSendData` / `MessageReciveData`
- 内置 WebSocket 协议栈：`IWebSocket` / `WebSocket` / `*EventArgs` / `CloseStatusCode` / `Opcode` / `WebSocketState`
- Protobuf 生成产物：`Logic.cs` / `Ws.cs` 中的全部消息类型（请勿手改，改 `.proto` 后重新生成）

> 连接状态查询请用 `MVXRSDK.IsConnected` / `MVXRSDK.State`，不要使用内部的 `SocketSystem`。

---

**文档版本**：MVXRSDK 2.0.1 ｜ 配套功能说明见 [开发指南](index.md) ｜ 变更记录见 [CHANGELOG.md](../CHANGELOG.md)  
技术支持：support@myverse.com
