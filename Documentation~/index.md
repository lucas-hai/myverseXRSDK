# MVXRSDK 开发指南

> 本文档面向接入方，讲"怎么用、为什么这么设计"。**字段级签名、完整参数表、错误码全清单等细节**统一交给 [API 参考手册](api-reference.md)，本文在需要时以"详见 API 文档"指引，不在正文堆砌签名表。
>
> 适用版本：MyVerse XR SDK **2.0.1**，命名空间 `MyVerseXRSDK`，包名 `com.myverse.xrsdk`。

---

## 1. 概述

MyVerse XR SDK 是一套用于 XR（扩展现实）多人协作的 Unity 工具包，以嵌入式 UPM 包形式分发。它把房间、空间对齐、网络同步、推流、录屏、导播切镜等基础设施收敛到一个对外 Facade（`MVXRSDK`）后面，业务方只面对一个 `public static` 入口。

| 能力 | 一句话说明 | 业务入口（详见 API 文档） |
|---|---|---|
| **生命周期** | 初始化、反初始化、状态机查询 | `MVXRSDK.InitMVXRSDK` / `UnInitMVXRSDK` / `State` / `IsReady` / `IsConnected` |
| **房间与连接** | 入房由 Init 网络阶段自动完成，无独立 JoinRoom | （无显式 API，连接态查询用 `State` / `IsConnected`） |
| **空间对齐** | XR 偏移基准、多场景根节点偏移、障碍物实时同步 | `RegisterXROffsetNode` / `RegisterRootNode` |
| **网络位置同步** | 多端位姿同步、远端虚影、同房间虚影开关 | `RegisterSelfNode` / `SetSyncSameRoomAvatar` |
| **积分验证** | 中控事件 / 自助调用两种模式 | `OnTransactionVerification` 事件 / `TransactionVerification` 方法 |
| **推流** | WebRTC（WHIP），由播控通过 NotifyLive 驱动 | `SetStreamSource` / `ClearStreamSource` + `OnPushStream*` 事件 |
| **导播切镜** | 多机位请求，中控仲裁后被选中才推流 | `SendDirectorRequest` + `OnPushStreamStarting` |
| **录屏信令** | 游戏主动触发，SDK 仅转发，服务端按时长自停 | `StartRecord` + `OnRecordResult` |
| **全局错误聚合** | 一个订阅接所有失败路径 | `OnError` |

> 本文是开发指南。每个 API 的完整签名、入参约束、错误码数值等，见 [API 参考手册](api-reference.md)。

---

## 2. 安装与依赖

### 2.1 安装（嵌入式 UPM 包）

把整个 `com.myverse.xrsdk/` 目录直接放到宿主 Unity 工程的 `Packages/` 文件夹下：

```
<YourProject>/
├── Assets/
├── Packages/
│   ├── manifest.json
│   └── com.myverse.xrsdk/      ← SDK 包整体放这里
│       ├── package.json
│       ├── Runtime/
│       ├── Samples/
│       ├── Documentation~/
│       └── ...
└── ProjectSettings/
```

Unity 会自动识别 `Packages/` 下含 `package.json` 的目录为 local embedded package，**无需在 `manifest.json` 显式声明**，Package Manager 窗口会显示为 "MyVerse XR SDK"。

### 2.2 依赖

下列依赖已在 SDK 的 `package.json` 中声明，由 Package Manager 自动拉取：

| 依赖 | 版本 | 用途 |
|---|---|---|
| `com.unity.webrtc` | 3.0.0-pre.8 | WebRTC（WHIP）推流 |
| `com.unity.nuget.newtonsoft-json` | 3.0.2+ | JSON 序列化 |

**URP（Universal RP）单独说明**：URP 与 SRP Core **不在** SDK 的 `package.json` 依赖里，由**宿主工程**提供——SDK 程序集 `MVXRSDK.asmdef` 通过 `references` 引用 `Unity.RenderPipelines.Universal.Runtime` / `Unity.RenderPipelines.Core.Runtime`，并对 URP / SRP Core **17.x** 定义 `versionDefines` 条件编译宏（`MV_URP_17_OR_NEWER` / `MV_SRP_CORE_17_OR_NEWER`，前向兼容）。当前宿主工程实测使用 **URP 14.0.11**，请确保宿主工程已安装并启用 URP。

> 推流子系统与 NetworkFailureHUD 的着色器路径依赖 URP，务必保证宿主工程已正确配置。

### 2.3 导入 Demo 示例与按键表

Package Manager 窗口 → 选中 `MyVerse XR SDK` → `Samples` 标签 → `Demo` → `Import`，即可在 `Assets/Samples/` 下获得总控示例场景与脚本。Demo 已装配好 XR Rig（玩家相机）、用于自动接源的直播相机、场景根节点、地面与 Demo 总控 GameObject，覆盖启动模式、节点注册、积分、推流、切镜、录屏、全局错误监听等全部模块。

打开场景直接 Play；在 Inspector 切 `initMode` + 填 `controlServerAddress` 后用如下按键触发各模块：

| 按键 | 功能 |
|---|---|
| `I` | Init SDK |
| `U` | UnInit SDK |
| `T` | 自助积分验证 |
| `R` | StartRecord |
| `D` | 真链路切镜（`SendDirectorRequest` 自动接源重载，被选中后自动接相机） |
| `X` | 热替换 XR Offset Node |
| `K` / `S` | Editor 仿真 NotifyLive 启动 / 停止（Offline / WsDirect 均可跑） |
| `Y` | 注销 Self Node（演示注销后位姿不再上报） |

---

## 3. 前置条件

- **Unity 2022.3 LTS** 或更新版本。
- 渲染管线为 **URP**（由宿主工程提供；当前工程实测 14.0.11，详见 §2.2）。
- 目标设备 OpenXR 服务已初始化、可获取设备 SN 码。**推荐 PICO 4 / 4U（实测过）**，其它 OpenXR 设备兼容但未实测。
- **必须先成功取到设备 SN，再调用 `InitMVXRSDK`** —— SN 是 SDK 初始化与中控识别的唯一标识。
- 外部应用资源（若用到）存放路径约定为 `/storage/emulated/0/myverse/`。

> SDK Runtime 不直接引用 PICO 程序集 —— 是否需要 PICO Integration SDK 取决于宿主工程的 XR Plug-in Management 配置。

---

## 4. 架构总览

SDK 采用三层分层，业务方只面对最顶层 Facade，下两层全部 `internal`、对业务不可见。

```
MVXRSDK (public static facade)        ← 业务方调用的唯一入口
  └─ *Manager (internal)              ← 按业务域聚合：Room / Business / Stream / Space / NetworkTransform
       └─ *Module (internal)          ← 单一职责单元：RoomModule / RecordModule / PushStreamModule …
            └─ *System (internal)     ← 跨业务基础设施：Socket / Http / Event / Mono / Pool / WebRTC / TextureProvider / Audio
```

- **Facade 层（`MVXRSDK`，`public static partial class`）**：唯一对外入口，承担 Bootstrap、生命周期（Init/UnInit）、状态机、积分入口等。`DeviceId` 是 public 只读，但 `BaseUrl` / `RoomAllocationStatus` 等内部字段业务方读不到 —— **查询连接状态请用状态机属性（`State` / `IsConnected`），不要去找内部字段。**
- **Manager 层（internal）**：每个 Manager 管一个业务域，业务无法也不应直接 new。
- **System 层（internal）**：可复用基础设施。Manager 通过 EventSystem 横向解耦、SocketSystem 收发、MonoSystem 每帧驱动。

### 自举启动引导

SDK 的运行骨架是"自举"的 —— 业务方**无需手动创建任何 GameObject**：

- `MVXRSDK.Init()` 标注 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`，在首个场景加载前由 Unity 运行时自动调用，创建名为 `"MVXRSDKManager"` 的 GameObject，并在其上挂 `MonoSystem` 与 `MVXRSDKManager`。
- 该根节点调 `DontDestroyOnLoad` 跨场景常驻，多场景切换不会丢失 SDK 运行时。
- `MonoSystem` 是 SDK 内部所有 `Update` 钩子与协程的宿主（HTTP/IO 全部协程化承载于此）。

> **关键区分**：Bootstrap 只搭骨架，**不**初始化 System 层。"根节点已存在" ≠ "SDK 已可用"。System 层要等业务显式调 `InitMVXRSDK` 才拉起。

---

## 5. 初始化与启动模式

### 5.1 两个 public 重载

```csharp
// 重载一：等价于 Production 模式
MVXRSDK.InitMVXRSDK(string deviceId);

// 重载二：显式指定模式（controlServerAddress 默认 null）
MVXRSDK.InitMVXRSDK(string deviceId, InitMode mode, string controlServerAddress = null);
```

正式接入业务方通常用单参重载即可。`deviceId` 入参严格校验（空 / 纯空白 / 超长 / 含非法字符均抛 `ArgumentException`），具体约束见 [API 文档](api-reference.md)。**调用时机：XR 服务初始化、SN 取到之后。**

### 5.2 三档启动模式：本地阶段 + 网络阶段

`InitMVXRSDK` 把启动拆成"本地阶段 + 网络阶段"。三档模式**共享本地阶段**（同步执行：拉起 System 层 → 装配五个不依赖网络的本地 Manager → 切到 `LocalReady`），仅网络阶段不同。

| 模式 | 网络阶段动作 | `controlServerAddress` | `BaseUrl` | 积分验证 |
|---|---|---|---|---|
| **Production**（默认 / =0） | 本地 HTTP(`localhost:8868`) 拉中控地址 → 轮询房间分配 → 连房间 WS → 登录 | 忽略 | 由 HTTP 回调回填 | 可用 |
| **WsDirect**（=1） | 跳过 `localhost:8868`，外部直传中控服地址，再走相同的房间分配轮询 + WS + 登录 | **必填**，为空则报错退回 `LocalReady` | 不回填（空） | 不可用（立即 `cb(false)`） |
| **Offline**（=2） | 完全跳过网络阶段，停留 `LocalReady` | 忽略 | 空 | 不可用（立即 `cb(false)`） |

> **WsDirect 关键澄清**：`controlServerAddress` 传的是**中控服地址**（如 `http://192.168.1.50:7015`），**不是**房间服 WS 地址；房间服 WS 地址仍由中控轮询响应分配返回。WsDirect 相对 Production 唯一省掉的就是 `localhost:8868` 拉中控地址这一跳，其余链路完全一致。
>
> - `Production`：正式接入。
> - `WsDirect`：开发期无 localhost 中控环境时测试网络链路。
> - `Offline`：开发期测试推流 / 节点等本地能力（被 `Debug_SimulateNotifyLive` 配合可走通整条推流链路，见 §7）。

### 5.3 状态机与查询属性

SDK 用 7 态枚举 `MVXRSDKState` 表达完整生命周期：

```
NotInitialized → Initializing → LocalReady → Connecting → Connected
                                     ↑                          ↕
                                     └────────  Disconnected ───┘
                                     ↑
                       (UnInit) Disposed → NotInitialized
```

- `Offline` 模式终态停在 `LocalReady`；`Production` / `WsDirect` 登录成功后切到 `Connected`。
- 网络阶段失败（HTTP 拉取失败 / WsDirect 地址缺失）从 `Connecting` 退回 `LocalReady`。
- 掉线 / 房间解散：`Connected` 或 `Connecting` 降为 `Disconnected`，重连成功重新登录回到 `Connected`。
- `UnInit` 先置 `Disposed`（瞬时收尾态）再复位为 `NotInitialized`（对外稳定终态）。

业务方常用查询属性：

| 属性 | 含义 | 判什么 |
|---|---|---|
| `MVXRSDK.State` | 当前生命周期枚举 | 原始状态 |
| `MVXRSDK.IsReady` | 本地是否就绪（覆盖 `LocalReady`/`Connecting`/`Connected`/`Disconnected`） | 能否调本地能力 API（如 `SetStreamSource`） |
| `MVXRSDK.IsConnected` | 是否已成功连接中控（仅 `State == Connected`） | 能否调需要联网的业务 API |
| `MVXRSDK.IsInitializing` | 是否正在本地阶段（仅 `State == Initializing`） | Init 进行中 |

> **入房由 Init 自动完成，没有 `JoinRoom` / `LeaveRoom`。** "入房"是 Production / WsDirect 网络阶段（房间分配轮询 → 连 WS → 登录）的自动结果；"离房"即 `UnInitMVXRSDK`。需要判断是否在房间里，用 `IsConnected`。遇到 `Disconnected` 不要急着重建，等内部重连自愈或主动 UnInit。

---

## 6. 核心概念

### 6.1 房间与连接

**链路全貌**：中控拉址（仅 Production）→ 每 1s 轮询房间分配 → 分配到 IP 后连房间 WS → 用 `DeviceId` 作 Token 登录 → 登录成功置 `Connected`、上报在线、广播成员列表 → 服务端解散 / UnInit / 应用退出时统一经 `OnDisbandRoom` 退房（先上报 offline 再关 WS）。SDK **无独立退房 API**。

**重连退避**：WebSocket 连接失败后按指数退避自动重连 —— 初始 **2s**、按 `2·2^(n-1)` 增长并封顶 **30s**、叠加 ±10% 抖动、**最多 5 次**。耗尽 5 次仍失败则停止重连，若曾连上过则降为 `Disconnected`，并抛出对外事件标记重连失败。

**NetworkFailureHUD 遮罩**：重连耗尽事件到达时，SDK 在玩家相机正前方 1m 弹出黑色半透明遮罩 + 文案「网络连接失败，请退出重试」。遮罩以已注册的 Self 节点（玩家相机）为父；相机节点未注册时仅打警告、不显示。该 HUD 单例，UnInit 时销毁。

### 6.2 空间对齐

空间数据由 WS 推送驱动（登录后先拉一次全量，之后实时推），缓存在 SDK 内部 Store，与 GameObject 解耦。涉及两类业务可注册的节点：

- **XR 偏移节点（`RegisterXROffsetNode`）**：作为所有障碍物 GO 的挂载父节点 —— 障碍物坐标都在 XR 偏移节点本地空间下。不变量：有障碍物 ⟹ XR 偏移节点已注册。
- **场景根节点（`RegisterRootNode`，支持多个）**：与 XR 偏移节点**相互独立**。服务端推送 `offset`/`rotation` 时，SDK 遍历所有已注册根节点写入 `localPosition` / `localEulerAngles`。注册时机晚于推送也安全 —— 新注册节点会立即回放一次最新缓存偏移。

**障碍物**按服务端列表与本地 diff 实时增删改（新增实例化、消失回收、变更则原地更新或重建），分椭圆 / 矩形两类，走对象池。每个障碍物可选距离检测：靠近玩家才显示。

> 节点注册遵循统一容错约定（见 §6.3 末），未注册时对应功能静默不启用、不报错。

#### 场景偏移的下发时机与锚点

正式流程中，场景偏移由中控在**开始游戏时下发一次**（不是运行中持续实时调整）。在**布店 / 实景部署**阶段，则需要通过中控**手动调整**场景，使虚拟场景对齐到真实空间中的摆放位置。

- 建议在设计初始场景时，预留**动态节点作为锚点**交给偏移节点作参考，便于部署期对齐；
- **无需考虑静态节点的实时调整** —— 静态物体不参与运行期的实时偏移（原因见下）。

#### 动静节点分离（重要）

场景根节点偏移只改根节点自身的 `Transform`。被标记为 **Static（尤其 Batching Static）** 的子物体会被 Unity 静态批处理烘入合并网格、世界坐标固定，**不会跟随根节点偏移**。

因此建议把场景里的**动态节点与静态节点分离**，并按下面顺序加载：

1. **先 `RegisterRootNode`** 注册根节点并完成偏移；
2. **再激活静态节点** —— 让静态物体在"偏移后的最终位置"上才被批处理烘焙，从而与动态部分对齐。

### 6.3 网络位置同步与虚影

- **本机上报（Reporter）**：`RegisterSelfNode` 注册玩家相机后，SDK 在该节点上自动挂 `NetworkTransform`（Reporter 角色），按 **0.2s 间隔**比对阈值上报位姿 —— 仅 XZ 平面位移超 **0.001m** 或 Y 转角超 **0.5°** 才发，且只在房间已分配时上报。
- **远端接收（Receiver）**：远端成员位姿经 WS 推送，落入虚影 GO 上的 `NetworkTransform`（Receiver 角色），做位置 Lerp + 旋转 Slerp 平滑插值，并切换 move/idle 动画。
- **显示距离判定**：以本机 Self 节点为参考，虚影与本机 XZ 平面距离在阈值内才从对象池创建虚影，超出则回收（保留快照可重建）。阈值分两种 —— **其他房间虚影固定 2m**；**同房间虚影由外部传参，默认 2m**（见下方开关）。Self 节点未注册时退化为"全部较近"并打警告。
- **本机自己一律不显示**：无论同房间还是其他房间，SDK 都会按 `DeviceId == 本机` 过滤位置推送，不为自己创建虚影。
- **同房间虚影开关与距离（`SetSyncSameRoomAvatar(enable, displayDistanceMeters = 2f)`，默认关）**：默认**只为"非本房间"成员创建虚影**，本房间成员位置推送被跳过。开启后本房间推送才会创建虚影（成员静止不上报时需等其移动后出现），其显示距离取 `displayDistanceMeters`（米，默认 2m，`<=0` 回退 2m，仅开启时生效）；关闭时立即回收已创建的本房间虚影（不影响非本房间）。任意时机可调。

> **`NetworkTransform` 组件由 SDK 内部管理**（注册 Self 节点时自动挂 Reporter，远端虚影自动挂 Receiver），业务通常无需直接操作 —— 了解即可。其 `MessageType` 枚举为 `None=0 / Reporter=1 / Receiver=2`，Reporter 只上报不接收、Receiver 只接收不上报，角色互斥。

#### 统一容错约定

XR Offset / Self / Scene Root 三类节点**任何时机皆可注册**（Init 前/中/后）：SDK 读取节点的所有代码都做了 null 检查，未注册时对应功能静默挂起、不报错，节点（重新）注册后各模块自动回放恢复。重复传入不同节点视为热替换，同一节点重复注册无副作用；被外部销毁的节点会在下次更新时自动剔除。

> **关于错误码 `NodeAlreadyRegistered`(6002) / `XROffsetAfterInit`(6003)**：API 文档会如实列出这两个 6xxx 码值，但它们与上述"节点注册任意时机皆可、同节点幂等、不同节点热替换"的现行约定表面冲突 —— 当前实现以"任意时机皆可"为准，该码是否实际触发以实现为准。

### 6.4 积分验证

业务需在积分扣除验证成功后才真正开始体验内容（**包名需提交到管理后台**，否则验证失败影响收益）。两种模式二选一：

```csharp
// 模式 A：通过中控启动游戏 —— 订阅事件
MVXRSDK.OnTransactionVerification += isResult => { if (isResult) { /* 开始内容 */ } };

// 模式 B：不接入中控 —— 体验前主动调用
MVXRSDK.TransactionVerification(isResult => { if (isResult) { /* 开始内容 */ } });
```

> 积分验证强依赖 `BaseUrl` —— **仅 Production 模式可用**；WsDirect / Offline 会立即回 `false`。两条路径独立、不会重复扣费，但同时用容易导致业务侧重复处理，建议二选一。

---

## 7. 推流与导播

### 7.1 推流模型：完全由播控 NotifyLive 驱动

**推流发起权不在客户端。** SDK 不存在"主相机自动推流"或"调个 StartStream 就开推"的概念。是否推、推到哪个流媒体地址，完全由播控经 WebSocket 下发的 `NotifyLive` 消息决定。SDK 收到后用 WHIP / WebRTC 把一张固定尺寸的内部 RenderTexture 推出去 —— 握手、SDP、编码、传输全部 SDK 内部完成，业务只需"在被选中时提供画面源"。

推流状态事件：

```csharp
MVXRSDK.OnPushStreamStarting += ip     => { /* 被选中，握手期，立刻接源 */ };
MVXRSDK.OnPushStreamStarted  += ip     => Debug.Log($"推流已开始 ip={ip}");
MVXRSDK.OnPushStreamStopped  += reason => MVXRSDK.ClearStreamSource();  // reason: StreamStopReason 枚举
MVXRSDK.OnPushStreamFailed   += (code, msg) => Debug.LogError($"推流失败 {(int)code}: {msg}");
```

> **`OnPushStreamStarting` 是"本机被选中"的唯一权威信号**，比 `OnPushStreamStarted`（握手完成）早约 1~2s。业务必须在 `OnPushStreamStarting` 回调里 `SetStreamSource` 接好画面源，才能保证首帧即有画面；**没收到这个事件就不要接源**（否则相机白渲染）。未接源时 SDK 推黑帧等待，不会失败。
>
> SDK 对运行时可恢复错误（ICE/DTLS 失败、连接丢失）会用缓存 URL 自动重试（默认 2s/5s/10s）；4xx 鉴权、404、codec 不支持、无源等配置类错误不重试。

### 7.2 导播切镜：中控仲裁，受理 ≠ 被选中

多个客户端各自调 `SendDirectorRequest(DirectorRequestOptions[, Camera])` 请求成为推流机位，**中控只选一台**。

```
业务调 SendDirectorRequest(opts)
    ──WS──→ 中控仲裁 → OnDirectorRequestResult(bool)   // 受理结果，受理 ≠ 被选中
被选中：中控 → NotifyLive(start) → OnPushStreamStarting(ip)
        业务在此回调 SetStreamSource(...)
被替换：中控 → NotifyLive(stop) → OnPushStreamStopped
        业务在此回调 ClearStreamSource()
```

请求参数 `DirectorRequestOptions` 关键字段（完整定义见 [API 文档](api-reference.md)）：`Source`（机位来源 —— `"unity"`=本机 Unity 机位，空 / `"mr"`=原直播，空是合法协议值不做默认填充）、`Lenses`（镜头数 1/2/3/4）、`DurationSec`（本段时长，必须 > 0，到期服务端停流）、`Record`。

> - **不要把 `OnDirectorRequestResult(true)` 当作"我会推流"** —— 它只表示请求被受理。是否被选中以 `NotifyLive(start)`（即 `OnPushStreamStarting`）为准。
> - **`OnDirectorSelected` 事件仅协议透传**（中控仲裁结果 deviceId/isPrimary/slot/duration），v3 起业务通常无需处理，保留作日志/观测用途。
> - **被替换与服务端主动停流，对外停流原因统一为 `StreamStopReason.ServerStop`**，业务无法据 `reason` 区分二者。

### 7.3 画面源生命周期

业务用 `SetStreamSource` 接画面源。SDK 提供两个开箱即用的画面源 public 类：

- **`RenderTextureStreamSource`**：业务自渲染到任意 RT 后交给 SDK（每帧一次 Blit 到 InternalRT；非 16:9 会被拉伸）。
- **`CameraStreamSource`**：让一台相机直接渲染到 InternalRT（零 Blit，GPU 开销 ≈ 0），适合"专门挂一台直播相机"。

```csharp
// 自动接源（推荐）：请求 + 被选中后自动接相机，停流自动清
MVXRSDK.SendDirectorRequest(opts, myDirectorCamera);

// 手动接源（精细控制）：订阅事件自己接 / 清
MVXRSDK.OnPushStreamStarting += _ => MVXRSDK.SetStreamSource(new CameraStreamSource(myDirectorCamera));
MVXRSDK.OnPushStreamStopped  += _ => MVXRSDK.ClearStreamSource();
MVXRSDK.SendDirectorRequest(opts);
```

设计要点：

- **InternalRT 固定尺寸**：由 `StreamConfig.StreamMaxLongSide` 按 16:9 算出（默认 1280×720），**与画面源尺寸无关**，任意源可热切。RT 生命周期 = Init→Dispose，`ClearStreamSource` 只清黑不释放（改尺寸需 UnInit→Init）。
- **无源推黑帧**：被选中还没接源、或推流中清源，都不卡死，推黑帧直到重新接源。
- **一相机推流保护**：推流会话活跃且已有源时，再次 `SetStreamSource` **直接丢弃新源**（不排队、不抢占），保护观众画面；会话活跃但无源（黑帧等待）或会话空闲时才允许接 / 换源。`ClearStreamSource` 不受此限。
- **自动 vs 手动**：手动接源优先于自动 —— 自动接源时机在 `OnPushStreamStarting` 之后消费，发现业务已手动接则放弃自动接（Info，非告警）。**谁接的谁清**：SDK 自动接的源停流自动清，业务手动接的源由业务自清，互不误清。
- **多场景约定**：相机是场景对象、随场景销毁；推流会话是 SDK 层（`DontDestroyOnLoad`）的，可横跨切场景。**场景卸载前主动 `ClearStreamSource()`**（SDK 对相机销毁有自保护兜底，但那是误用防护，不是正常路径）。

### 7.4 音频：仅游戏音

推流音频只有一路 —— **游戏音**，业务通过 `PushGameAudioPcm(pcm, sampleRate, channels)` 推入（典型挂在 AudioListener 同 GameObject 的 `OnAudioFilterRead`）。**SDK 不采集麦克风、不碰麦克风设备、不与语音 SDK 抢麦。** 业务不推 PCM 则音频通道静音，不影响视频。

工作采样率跟随设备输出率（`AudioSettings.outputSampleRate`，PICO 4U 实测 24000）：等率直通零重采样、异率线性插值重采样 —— **所以直接传 `AudioSettings.outputSampleRate` 即可，任何设备都不必关心具体值**。支持 8000–192000 Hz、mono / stereo（stereo 内部平均成 mono）。

> 用 SDK 提供的推流装配组件（拖 `AudioListener` 进对应字段）时无需手调此 API，组件内部自动采集推送。

### 7.5 录屏：游戏触发，SDK 仅转发，服务端按时长自停

录屏由游戏侧主动调 `StartRecord(StartRecordOptions)` 发起。**SDK 不做任何实际录制**，只把参数打成 pb 经 WS 转发给播控（请求-应答模型），所有字段由游戏侧填充。

```csharp
MVXRSDK.OnRecordResult += (code, errMsg) =>
{
    if (code == MVXRSDKErrorCode.Ok) Debug.Log("录屏请求已被服务端接受");
    else Debug.LogError($"录屏失败 {(int)code}: {errMsg}");
};
MVXRSDK.StartRecord(new StartRecordOptions { DurationSec = 30, FileName = "battle-round-3", /* ... */ });
```

> 录屏是**限时模式**：录多久由 `DurationSec` 控制，到时由**服务端自动停止**。本期**没有 `StopRecord` 接口** —— 客户端无法主动停录。`StartRecordOptions` 字段与录屏错误码见 [API 文档](api-reference.md)。

### 7.6 Editor 离线调试入口

手测脚本可模拟服务端推送，**无需 WS 连接**也能跑通整条推流链路（真 WHIP POST + 真 WebRTC 协商），事件链路与正式 NotifyLive 完全一致；Offline 模式下这是唯一能启动推流的入口：

```csharp
#if UNITY_EDITOR
MVXRSDK.Debug_SimulateNotifyLive("192.168.1.100", start: true);
#endif
```

---

## 8. 平台与 WebRTC 约束

推流子系统建立在 `com.unity.webrtc 3.0.0-pre.8` 上。以下为**要点级**说明，详细配置项、默认值、约束表交给 [API 文档](api-reference.md) 的 `StreamConfig` 章节。

- **PICO / Vulkan 历史坑（已收窄）**：2026-05 实测故障是 **Vulkan + FDM 注视点渲染下读 XR eye buffer（multiview Tex2DArray）内容错乱** —— 坑在**采集层**，编码 / 传输当时即正常。v3 推流源是独立相机直渲普通 RT（不读 eye buffer），该风险点不在链路上，**构建可用 Vulkan**。约束保留：**禁止新代码从主相机 / eye buffer / 屏幕抓帧走 Vulkan**，否则会复现错乱。
- **RT 尺寸上下限**：编码器对绑定 RT 有最小尺寸要求（145×49），SDK 在会话入口预检、低于即 fail-fast；最大支持到 4096×4096。内部 RT 固定 16:9、偶数对齐（H.264 要求）。
- **H.264 强制**（`ForceH264` 默认 true）：局域网 + PICO 推荐。若该 WebRTC 构建没编进 H.264 编码器，offer 不含 H.264 payload 会被提前拦截报错，而非等流媒体服务器拒绝才暴露。
- **BWE 慢启动与 `VideoMinBitrate`**：libwebrtc 默认初始 BWE 仅约 100kbps，GCC 拥塞控制会把 fps 降到 2-5 直到带宽爬升完（实测约 40s），期间接收端会 dup 帧卡死。SDK 用 `VideoMinBitrateKbps`（默认 1500）给 sender 设 `minBitrate`，强制从首帧起按目标码率输出。前提是局域网上行充足；`minBitrate ≤ maxBitrate`。
- **仅局域网，无 STUN/TURN**：不配任何 STUN/TURN，依赖局域网 host candidate，走 non-trickle ICE。**保持 PICO 与流媒体服务器同子网**，避免公网链路。STUN/TURN/Token 刷新等公网容错按约定省略。

---

## 9. 最佳实践

### 9.1 推荐调用顺序

```csharp
// 1. 注册节点（任意时机可调，可在 Init 后；这里放 Init 前更直观）
MVXRSDK.RegisterXROffsetNode(xrOffsetNode);   // 障碍物挂载基准
MVXRSDK.RegisterSelfNode(playerCamera);        // 玩家相机，启用本机位姿上报与近远判定

// 2. 全局错误监控（尽早订阅，覆盖整个生命周期）
MVXRSDK.OnError += (code, msg, sourceModule) =>
    Telemetry.Report($"SDK error from {sourceModule}: {(int)code} {msg}");

// 3. 初始化（单参 = Production；开发期可用 WsDirect / Offline 重载）
MVXRSDK.InitMVXRSDK(deviceId);

// 4. 场景根节点（可选，支持多个）
MVXRSDK.RegisterRootNode(rootNodeA);
MVXRSDK.RegisterRootNode(rootNodeB);

// 5. 积分验证（二选一）
MVXRSDK.OnTransactionVerification += OnVerificationResult;   // 中控触发
// 或
MVXRSDK.TransactionVerification(OnVerificationResult);        // 自助调用
```

### 9.2 全局错误监控

一个 `OnError` 订阅即可接全部失败路径（推流 / 录屏 / Socket / HTTP / 积分），回调参数为 `(MVXRSDKErrorCode code, string msg, string sourceModule)`。错误码可 `(int)` cast 用于埋点 —— 数值序号是稳定契约。

### 9.3 反初始化

```csharp
MVXRSDK.UnInitMVXRSDK();   // 幂等：未初始化时直接 return；这就是"离房"
```

UnInit 是"反向 + 对称 + 幂等"的：卸载顺序与装配顺序相反、System reset 与 System Init 一一配对、无论当前是否初始化都可安全调用。

### 9.4 日志关闭

业务方关闭 SDK 日志的**唯一**方式是编译宏：

> Player Settings → Scripting Define Symbols 添加 **`MVXRSDK_LOG_DISABLED`**，即零开销关闭 SDK 全部日志。

> ⚠️ SDK 内部日志类 `MVXRSDKLog` 是 **`internal`**，业务方**跨程序集调不到** `SetMinLevel` / `SetTag` 等 —— 运行时日志级别 API 属 SDK 内部，业务请勿尝试调用（旧文档教业务用 `MVXRSDKLog.SetMinLevel` 是错误的）。

---

## 10. 常见问题 FAQ

- **Q：怎么入房 / 退房？找不到 `JoinRoom`？**
  A：SDK **没有** `JoinRoom` / `LeaveRoom`，也没有 `OnConnected` / `OnDisconnected` 事件。入房是 Production / WsDirect 网络阶段自动完成的；退房即 `UnInitMVXRSDK`；连接态用 `MVXRSDK.IsConnected` / `MVXRSDK.State` 查询。

- **Q：初始化抛 `ArgumentException`？**
  A：`deviceId` 入参严格校验（不可空 / 不可超长 / 不含非法字符），先确认取到的 SN 合法。

- **Q：节点注册时机不对会报错吗？**
  A：不会。三类节点任何时机皆可注册（Init 前/中/后），未注册时对应功能静默不启用、不报错，注册后自动回放恢复。

- **Q：怎么调 SDK 日志级别？为什么 `MVXRSDKLog.SetMinLevel` 编译不过？**
  A：`MVXRSDKLog` 是 internal，业务方调不到。要关日志，加编译宏 `MVXRSDK_LOG_DISABLED`（见 §9.4）。

- **Q：积分扣除验证总是失败？**
  A：① 确认是 Production 模式（WsDirect / Offline 立即返回 false）；② 检查网络连通、账户积分、包名是否已提交后台；③ 订阅 `OnError` 看细致错误码。

- **Q：PICO 4U 推流画面错乱 / 黑屏？**
  A：画面错乱是 v2 时代 Vulkan 下读 eye buffer 抓帧的坑（FDM 注视点渲染所致），v3 推流源为独立相机直渲普通 RT，无此问题，Vulkan 可用。黑屏先查画面源是否已接上 —— 无源推黑帧是设计行为。

- **Q：推流音频没声 / 音调失真？**
  A：业务采集回调直接传 `AudioSettings.outputSampleRate` 即可（SDK 工作采样率跟随设备输出率）；推流音频仅游戏音一路，无麦克风。仍无声时检查 AudioListener 是否已接入。

- **Q：推流中调 `SetStreamSource` 不生效？**
  A：一相机推流保护 —— 会话活跃且已有源时新源直接丢弃（Warning 日志，不排队不抢占）。先 `ClearStreamSource()` 再接，或等停流。

- **Q：注册多个根节点时偏移如何应用？**
  A：所有已注册根节点同步应用相同的位置偏移与旋转。

- **Q：录屏怎么主动停止？**
  A：本期无 `StopRecord` 接口，录屏是限时模式，到 `DurationSec` 由服务端自动停。

---

## 11. 版本与支持

- **当前版本**：MyVerse XR SDK **2.0.1**（命名空间 `MyVerseXRSDK`）。
- **更新记录**：见 [CHANGELOG.md](../CHANGELOG.md)（含 v1.x → v2.x 及推流 v3 切镜化重构的 Migration Guide）。
- **API 细节**：见 [API 参考手册](api-reference.md)。
- **技术支持**：`support@myverse.com`。

---

## 附录 · 非业务面 public 类型

下列类型在程序集中虽为 public，但属基础设施 / 协议栈，**不面向业务**：`SocketSystem`、`PoolSystem`、内置 WebSocket 协议栈、PB 生成产物（`Logic.cs` / `Ws.cs`）等。**请勿在业务代码中使用** —— 它们后续可能收敛为 internal。
