# MVXRSDK 使用说明文档

## 1. 概述

MVXRSDK 是一套用于 XR（扩展现实）环境的多人协作开发工具：房间管理、空间对齐、网络位置同步、WebRTC 推流（WHIP）、录屏信令、导播切镜头。本文档提供 SDK 的完整使用说明。

------

## 2. 安装与依赖

### 2.1 安装

把整个 `com.myverse.xrsdk/` 目录直接放到你的 Unity 工程的 `Packages/` 文件夹下：

```
<YourProject>/
├── Assets/
├── Packages/
│   ├── manifest.json
│   └── com.myverse.xrsdk/      ← SDK 包整体放这里
│       ├── package.json
│       ├── Runtime/
│       ├── Tests/
│       └── ...
└── ProjectSettings/
```

Unity 会自动识别 `Packages/` 下含 `package.json` 的目录为 local embedded package，无需在 `manifest.json` 显式声明，Package Manager 窗口会显示为 "MyVerse XR SDK"。

### 2.2 依赖

下列依赖由 Package Manager 自动拉取（已在 `package.json` 声明）：

| 依赖 | 版本 |
|---|---|
| `com.unity.webrtc` | 3.0.0-pre.8 |
| `com.unity.render-pipelines.universal` | 14.0.11 |
| `com.unity.nuget.newtonsoft-json` | 3.0.2+ |

### 2.3 导入示例

Package Manager 窗口 → 选中 `MyVerse XR SDK` → `Samples` 标签 → `Demo` → `Import` 即可在 `Assets/Samples/` 下获得：

- `MVXRSDKDemo.unity` —— 测试场景，已装配好 XR Rig / 主相机 / 切镜目标相机 / Root 节点 / Floor / Demo 总控 GameObject
- `MVXRSDKDemo.cs` —— 总控脚本，覆盖全部模块：启动模式（Production/WsDirect）、节点注册（XR Offset / Self / Root）、积分扣除、推流（Rig 装配）、切镜（真链路 + 本地直切）、录屏、全局错误监听、热替换/注销演示

打开场景直接 Play；Inspector 的 `MVXRSDKDemo` 字段切 `initMode` + 填 `controlServerAddress` 后按键触发各模块：

| 按键 | 功能 |
|---|---|
| `I` | Init SDK | `U` | UnInit SDK |
| `T` | 自助积分验证 | `R` | StartRecord |
| `D` | 真链路切镜（SendDirectorRequest + OnDirectorSelected → Rig） | `L` | 本地直切（Rig 跳过中控） |
| `K` / `S` | Editor 仿真 NotifyLive 启动/停止（Offline / WsDirect 都能跑） | `X` | 热替换 XR Offset Node |
| `Y` | 注销 Self Node（演示注销后位姿不上报） | | |

### 2.4 功能模块速览

| 模块 | 入口 | 简介 |
|---|---|---|
| **房间** | `MVXRSDK.JoinRoom` / `LeaveRoom` | 进出房间、成员同步 |
| **空间对齐** | `MVXRSDK.RegisterXROffsetNode` / `RegisterRootNode` / `RegisterSelfNode` | XR Rig、场景根节点、玩家相机偏移 |
| **障碍物** | `SpaceObstaclesModule` | 实时同步空间障碍 |
| **网络 Transform** | `NetworkTransform` 组件 | 多端位置同步 |
| **积分扣除** | `MVXRSDK.OnTransactionVerification` / `TransactionVerification` | 中控启动 / 自助验证两种模式 |
| **推流** | `MVXRSDK.SetStreamSource` + `OnPushStream*` 事件 | WebRTC（WHIP），由播控通过 NotifyLive 触发 |
| **录屏信令** | `MVXRSDK.StartRecord(StartRecordOptions)` | 游戏主动触发，SDK 转发到服务端 |
| **导播切镜头** | `MVXRSDK.SendDirectorRequest` + `OnDirectorSelected` | 多机位切换 |
| **全局错误聚合** | `MVXRSDK.OnError` | 一个订阅接所有失败路径 |

------

## 3. 前置条件

- Unity 2022.3 LTS 或更新版本
- URP 渲染管线
- 目标设备 OpenXR 服务已初始化、设备 SN 码可获取（推荐 PICO 4 / 4U 实测过；其它 OpenXR 设备兼容但未实测）
- 必须先成功获取到 PICO SN 码，再调用 `InitMVXRSDK` 初始化
- 外部应用资源存放路径（若用到）：`/storage/emulated/0/myverse/`

> SDK Runtime 不直接引用 PICO 程序集——是否需要 PICO Integration SDK 取决于宿主工程的 XR Plugin Management 配置。

------

## 4. 初始化

### 4.1 初始化方法

```csharp
MVXRSDK.InitMVXRSDK(string deviceId);
```

#### 参数说明

| 参数名   | 类型   | 说明                              |
| :------- | :----- | :-------------------------------- |
| deviceId | string | 设备 SN，用于 SDK 初始化与中控识别 |

#### 入参校验（v2）

`deviceId` 为空 / 超长 64 / 含 ` ` `/` `?` `#` 任意非法字符时抛 `ArgumentException`。

#### 调用时机

XR 服务初始化完成之后调用。

### 4.2 启动模式（InitMode）

`InitMVXRSDK` 提供三档启动模式，正式接入业务方无需关心，直接调单参签名即可。

```csharp
MVXRSDK.InitMVXRSDK(string deviceId, InitMode mode, string controlServerAddress = null);
```

| Mode | 行为 | 用途 |
|---|---|---|
| `Production`（默认） | 本地 HTTP(`localhost:8868`) → 拉中控地址 → 轮询房间分配 → 连房间 WS → 登录 | 正式接入 |
| `WsDirect` | 跳过 `localhost:8868`，外部直接传**中控服地址**（参数 `controlServerAddress`）；仍走房间分配轮询 + 房间 WS 连接 + 登录 | 开发期测试网络链路（无 localhost 中控环境） |
| `Offline` | 完全离线，只装配本地 Manager | 开发期测试推流/节点等本地能力 |

> **WsDirect 注意**：`controlServerAddress` 传的是**中控服地址**（例如 `http://192.168.1.50:7015`），不是房间服 WS 地址；房间服 WS 地址由中控轮询响应中分配返回。

#### 模式差异

| 能力 | Production | WsDirect | Offline |
|---|---|---|---|
| 本地 Manager（推流/节点/池等） | ✅ | ✅ | ✅ |
| WebSocket 连接 + 登录 | ✅ | ✅ | ❌ |
| `MVXRSDK.BaseUrl`（用于积分扣除 API） | ✅ | ❌（空） | ❌（空） |
| `MVXRSDK.TransactionVerification` | ✅ | ❌ 立即返回 false | ❌ 立即返回 false |

### 4.3 状态机与查询（v2）

`MVXRSDK.State`（`MVXRSDKState` 枚举）反映 SDK 完整生命周期：

```
NotInitialized → Initializing → LocalReady → Connecting → Connected
                                      ↑                          ↓
                                      ←─────── Disconnected ←────┘
                                      ↑
                              UnInit  Disposed → NotInitialized
```

公开属性（业务方常用）：

```csharp
MVXRSDK.State           // MVXRSDKState 枚举值
MVXRSDK.IsReady         // 本地就绪（State >= LocalReady），可调本地能力 API
MVXRSDK.IsConnected     // 已完全连通（State == Connected），可调网络 API
MVXRSDK.IsInitializing  // 正在 Init 中
```

`Offline` 模式终态停在 `LocalReady`；`Production` / `WsDirect` 模式登录成功后切到 `Connected`。

------

## 5. 积分扣除管理

**说明**：需要在积分扣除验证成功后真正开始体验应用内容。

**必要条件**：包名需提交到管理后台进行验证，否则验证失败影响收益。

### 5.1 通过中控启动游戏（订阅事件）

```csharp
MVXRSDK.OnTransactionVerification += (bool isResult) =>
{
    if (isResult)
    {
        Debug.Log("积分扣除验证成功");
        // 开始应用内容
    }
    else
    {
        Debug.Log("积分扣除验证失败");
    }
};
```

### 5.2 自主控制（主动调用）

不接入中控系统时，在开始体验前调用：

```csharp
MVXRSDK.TransactionVerification((bool isResult) =>
{
    if (isResult) { /* 验证成功，开始应用 */ }
    else          { /* 验证失败 */ }
});
```

> **二选一**：同时订阅事件 + 主动调用不会重复扣费（两条路径独立），但容易导致业务侧重复处理。

------

## 6. 节点注册与管理

### 6.1 注册 XR 偏移节点

```csharp
MVXRSDK.RegisterXROffsetNode(Transform xrOffsetNode);
```

| 参数名       | 类型      | 说明                                     |
| :----------- | :-------- | :--------------------------------------- |
| xrOffsetNode | Transform | XR Origin（XR Rig）或 Camera Offset 节点 |

#### 调用时机（v2 放宽）

**任意时机皆可**（Init 前/中/后），SDK 实时读取最新值：
- 重复传入不同节点视为**热替换**（场景切换 / 延迟构造的 XR Origin 等场景）
- 重复传入同一节点幂等忽略
- 未注册时 SDK 内部读取该节点的代码（SpaceObstacles / NetworkTransform）会做 null 检查 → 对应功能静默不启用

### 6.2 注销 XR 偏移节点

```csharp
MVXRSDK.UnRegisterXROffsetNode();
```

未注册时调用幂等忽略。

------

### 6.3 注册自身节点（玩家相机）

```csharp
MVXRSDK.RegisterSelfNode(Transform selfNode);
```

| 参数名   | 类型      | 说明                                     |
| :------- | :-------- | :--------------------------------------- |
| selfNode | Transform | 本机玩家相机 Transform（通常是 XR 头盔相机） |

#### 调用时机（与 XR 偏移节点一致）

**任意时机皆可**（Init 前/中/后）：
- 注册后 SDK 会在该节点挂载 NetworkTransform（Reporter 角色），定时上报本机位姿
- 重复传入不同节点视为**热替换**（停止旧节点上报 + 挂新节点）
- 重复传入同一节点幂等忽略
- 未注册时 NetworkFailureHUD / 障碍物距离检测 / 远端玩家近远判定都做了 null 检查 → 对应功能静默不启用

> v2 不再依赖 `Camera.main` 自动抓取——测试场景 / 多相机 / 延迟构造场景下抓不到主相机就会失效，业务侧主动注册更可靠。

### 6.4 注销自身节点

```csharp
MVXRSDK.UnRegisterSelfNode();
```

注销后停止本机位姿上报；节点上的 NetworkTransform 组件保留（messageType 置 None）。

------

### 6.5 注册场景根节点

```csharp
MVXRSDK.RegisterRootNode(Transform rootNode);
```

支持注册**多个**根节点。重复同节点 / null 入参幂等忽略。若节点在外部被销毁（`GameObject.Destroy`），SDK 在下次场景数据更新时自动从列表移除。

### 6.6 注销指定场景根节点

```csharp
MVXRSDK.UnRegisterRootNode(Transform rootNode);
```

### 6.7 注销所有场景根节点

```csharp
MVXRSDK.UnRegisterAllRootNodes();
```

------

## 7. 最佳实践与建议

### 7.1 推荐调用顺序

```csharp
// 1. 注册节点（任意时机可调，可在 Init 后）
MVXRSDK.RegisterXROffsetNode(xrOffsetNode);
MVXRSDK.RegisterSelfNode(playerCamera);     // 玩家相机，启用本机位姿上报与距离判定

// 2. 初始化 SDK
MVXRSDK.InitMVXRSDK(deviceId);

// 3. 注册场景根节点（可选，支持多个）
MVXRSDK.RegisterRootNode(rootNodeA);
MVXRSDK.RegisterRootNode(rootNodeB);

// 4. 积分验证（二选一）
MVXRSDK.OnTransactionVerification += OnVerificationResult;     // 中控触发
// 或
MVXRSDK.TransactionVerification(OnVerificationResult);          // 自助调用
```

### 7.2 反初始化

```csharp
// 反初始化（幂等，未初始化时直接 return）
MVXRSDK.UnInitMVXRSDK();
```

### 7.3 全局错误监控

```csharp
// 一个订阅接全部失败路径（推流/录屏/Socket/HTTP/积分）
MVXRSDK.OnError += (MVXRSDKErrorCode code, string msg, string sourceModule) =>
{
    Telemetry.Report($"SDK error from {sourceModule}: {code}({(int)code}) {msg}");
};
```

------

## 8. 推流与录屏

### 8.1 推流流程（被动接收 NotifyLive）

播控通过 WebSocket 下发 `NotifyLive` 通知 SDK 开始/停止推流；游戏侧只需在初始化后**提供画面源**，剩下的握手、SDP、编码、传输 SDK 内部完成。

```csharp
// 订阅推流状态事件（v2 签名）
MVXRSDK.OnPushStreamStarted += streamServerIp => Debug.Log($"推流已开始 ip={streamServerIp}");
MVXRSDK.OnPushStreamStopped += reason         => Debug.Log($"推流停止 reason={reason}");  // StreamStopReason 枚举
MVXRSDK.OnPushStreamFailed  += (code, msg)    => Debug.LogError($"推流失败 {code}({(int)code}): {msg}");  // MVXRSDKErrorCode

// 推流停止后可选清理
MVXRSDK.ClearStreamSource();
```

`StreamStopReason` 4 种：`ServerStop` / `UserStop` / `NetworkLost` / `ConfigChanged`。

未调用 `SetStreamSource` 即收到推流通知时，`OnPushStreamFailed(MVXRSDKErrorCode.NoStreamSource, "...")`。

------

### 8.2 画面源（IStreamSource）

SDK 提供两个开箱即用的 `IStreamSource` 实现，业务侧二选一交给 `MVXRSDK.SetStreamSource`：

#### 8.2.1 `RenderTextureStreamSource`：业务自己渲染到 RT

```csharp
// 业务侧自己用任意方式渲染到 myRT（任意格式、任意尺寸）
var src = new RenderTextureStreamSource(myRT);
MVXRSDK.SetStreamSource(src);

// 指定推流目标尺寸（SDK 内部 Graphics.Blit 自动缩放 + 格式转换）
var srcWithSize = new RenderTextureStreamSource(myRT, targetWidth: 1280, targetHeight: 720);
MVXRSDK.SetStreamSource(srcWithSize);

// 生命周期事件（被切走时暂停自家采集省 GPU，切回自动恢复）
src.OnAttached += () => myCapture.Resume();
src.OnDetached += () => myCapture.Pause();
```

性能：每帧 1 次 Graphics.Blit，PICO 4U 上约 0.2-0.5ms GPU。目标宽高必须是**偶数**（H.264 要求），可用 `CameraStreamCapture.ComputeStreamSize(srcW, srcH, maxLongSide)` 帮算。

#### 8.2.2 `CameraStreamSource`：让 Camera 直接渲染到 RT（零 Blit）

```csharp
var src = new CameraStreamSource(myDirectorCamera, width: 1280, height: 720);
MVXRSDK.SetStreamSource(src);
```

| 项 | 行为 |
|---|---|
| Camera.targetTexture | Attach 时被 SDK 改为内部推流 RT；Detach 时还原 |
| Camera.enabled | Attach 时被 SDK 强制 true；Detach 时还原（业务相机平时可 `enabled=false` 不烧 GPU） |
| 是否上屏 | 推流 RT 渲染时不上屏（Unity 规则：有 targetTexture 的相机不进入屏幕画面） |
| GPU 开销 | ≈ 0（无 Blit，相机渲染管线直接输出到 RT） |

适合"专门挂一台直播相机"的方案，配合 §8.4 切镜业务最自然。

#### 8.2.3 兼容入口

```csharp
MVXRSDK.SetStreamSource(myRT);   // RT 重载，SDK 内部包一层 RenderTextureStreamSource
```

仅在不需要订阅 `OnAttached / OnDetached` 时用；推荐用 §8.2.1/8.2.2 显式实例。

------

### 8.3 推流装配组件 `MVXRStreamRig`（推荐入口）

`MVXRStreamRig` 是把"画面源 + 游戏音 + 麦克风 + 切镜"一键拧好的 MonoBehaviour，Inspector 拖完字段即可推流，省掉手写 `SetStreamSource` / PCM 推送的胶水代码。

#### Inspector 字段

| 字段 | 说明 |
|---|---|
| `mainCamera` | 业务主相机（玩家看到的画面）。SDK 不会修改它的 `targetTexture`，留空则不推画面 |
| `gameAudioListener` | 游戏音 `AudioListener`，通过 `OnAudioFilterRead` 抓 master mix。留空则不推游戏音 |
| `captureMicrophone` | 是否采集麦克风。注意会占用麦克风设备，可能与 Pico 语音 SDK 冲突 |
| `micSampleRate` | 麦克风采样率（48000 / 44100） |
| `micDevice` | 麦克风设备名，留空使用系统默认 |
| `directorCameras` | 预设切镜目标相机数组；配合 `SwitchCameraTemporary(index, durationSec)` 用 |
| `streamConfigAsset` | 视频编码配置（`Fps` / `StreamMaxLongSide` / `VideoBandwidthKbps` / `VideoMinBitrateKbps` / `ForceH264`）。详见 [8.8.2 配置入口](#882-配置入口推荐-asset兼容代码)。留空全部走 SDK 默认 |

#### 事件

```csharp
rig.Ready     += rt => Debug.Log("画面 RT 就绪 + 已交给 SDK");
rig.OnSwitched += (cam, sec) => Debug.Log($"切镜成功 → {cam.name} 倒计时 {sec}s");
rig.OnRestored += ()         => Debug.Log("切回主相机源");
```

#### 状态查询

```csharp
RenderTexture rt = rig.StreamTexture;        // 当前画面 RT（首帧渲染后非空）
bool inSwitch    = rig.IsInDirectorSwitch;   // 是否处于切镜中
```

#### 与业务自管的差异

| 场景 | 用 Rig | 不用 Rig |
|---|---|---|
| 推流画面 | 拖主相机进字段 | 自己构造 `RenderTextureStreamSource` + `SetStreamSource` |
| 游戏音 | 拖 AudioListener 进字段 | 自己挂脚本调 `PushGameAudioPcm` |
| 麦克风 | 勾 `captureMicrophone` | 自己采集 PCM 调 `PushMicPcm` |
| 切镜 | `rig.SwitchCameraTemporary(target, sec)` | 自己构造 `CameraStreamSource` + `SetStreamSource` + 倒计时切回 |
| SDK Init 时机 | 任意（Rig 内部协程等 `MVXRSDK.IsReady`） | 业务自己保证 Init 后再 `SetStreamSource` |

Rig 内部不调 `MVXRSDK.InitMVXRSDK` —— 业务自己控制 Init 时机与模式。

------

### 8.4 切镜（运行时切换画面源）

#### 8.4.1 通过 Rig（推荐，业务编排都收口）

```csharp
// 按预设相机切（推荐）
rig.SwitchCameraTemporary(directorCameraIndex: 0, durationSec: 10);

// 直接传 Camera 引用切
rig.SwitchCameraTemporary(targetCamera, durationSec: 10);

// 业务主动切回（不等倒计时）
rig.RestoreOriginalCamera();
```

行为：
- 倒计时到期自动切回 `mainCamera`
- 重复调用会**抢占**（取消上次倒计时，Detach 旧源，切到新相机）
- 推流尚未启动（首帧未渲染）时拒绝并打 warning
- 切镜中 Rig 被 Disable，倒计时清掉、待切镜的相机源放弃

#### 8.4.2 底层（直接调 SDK）

`MVXRStreamRig` 内部就是用下面这条路径，需要自己编排时直接调即可：

```csharp
var directorSource = new CameraStreamSource(directorCam, width, height);
MVXRSDK.SetStreamSource(directorSource);   // 旧 source.Detach + 新 source.Attach
// ... 业务自己倒计时
MVXRSDK.SetStreamSource(originalSource);   // 切回
```

> **切镜源尺寸**必须 = 当前 SDK 内部推流 RT 的尺寸（即 `StreamConfig.StreamMaxLongSide` 同比例缩后的 W×H），否则 `TextureProviderSystem` 拒绝切源不断流。用 Rig 时这一点自动满足。

------

### 8.5 音频推流（PCM 推送）

SDK 不主动开 AudioRecord（避免与游戏语音 SDK 抢麦克风）。业务侧采集 PCM 后**主动调** `PushGameAudioPcm` / `PushMicPcm`，SDK 内部混音后随视频一起编码上行。

```csharp
// 游戏音：典型在挂 AudioListener 的 GameObject 上写 OnAudioFilterRead
private void OnAudioFilterRead(float[] data, int channels)
{
    // Unity 在音频线程喂数据；MVXRSDK 内部已做线程安全
    MVXRSDK.PushGameAudioPcm(data, AudioSettings.outputSampleRate, channels);
}

// 麦克风：从语音 SDK / Microphone 转发一份
MVXRSDK.PushMicPcm(micPcm, sampleRate: 48000, channels: 1);
```

支持：
- 采样率：**48000 / 44100**（其它抛 `ArgumentException`）
- 通道数：mono / stereo（stereo 内部自动平均成 mono）
- `pcm == null` / 越界采样率 / 越界通道 → `ArgumentException`

> 用 `MVXRStreamRig` 时不需要自己调这两个 API——拖 AudioListener 和勾 `captureMicrophone` 后，Rig 内部的 `GameAudioStreamCapture` / `MicrophoneStreamCapture` 会自动调它们。

------

### 8.6 录屏（游戏主动触发，SDK 仅做参数转发）

游戏在关键节点调用，SDK 通过 WebSocket 下发 `logic.StartRecord`。**SDK 不做实际录制**——所有 5 个 pb 字段由游戏侧填充；服务端根据 `DurationSec` 限时自动停，**没有 StopRecord 接口**。

```csharp
// 订阅结果（v2 签名）
MVXRSDK.OnRecordResult += (MVXRSDKErrorCode code, string errMsg) =>
{
    if (code == MVXRSDKErrorCode.Ok) Debug.Log("录屏请求已被服务端接受");
    else Debug.LogError($"录屏失败 {code}({(int)code}): {errMsg}");
};

// 发起录屏请求
MVXRSDK.StartRecord(new StartRecordOptions
{
    RealCamera   = false,
    CameraId     = string.Empty,
    DurationSec  = 30,
    FileName     = "battle-round-3",
    PicoDeviceId = MVXRSDK.DeviceId    // pb 字段名沿用历史命名（PicoDeviceId），但传入 v2 后的 MVXRSDK.DeviceId
});
```

录屏错误码（`MVXRSDKErrorCode` 5xxx 段）：
- `RecordInvalidOptions` = 5001
- `RecordNotConnected`   = 5002
- `RecordAlreadyRecording` = 5003
- `RecordRemoteRejected` = 5004
- `RecordTimeout`        = 5005
- `RecordParseFailed`    = 5006

### 8.7 Editor Debug 入口

手测脚本可模拟服务端推送，**无需 WS 连接**也能跑通推流链路：

```csharp
#if UNITY_EDITOR
MVXRSDK.Debug_SimulateNotifyLive("192.168.1.100", start: true);
#endif
```

完整推流测试场景参考：`Tests/PlayModeScenes/WsDirectRecordSwitch.unity`。

### 8.8 推流限制与配置

#### 8.8.1 webrtc 包限制与项目实测约束

推流模块完全建立在 `com.unity.webrtc 3.0.0-pre.8` 上。本节只列两类内容：

- **A. webrtc 包官方明文要求**——出处：包内 `Library/PackageCache/com.unity.webrtc@3.0.0-pre.8/Documentation~/requirements.md` 与 `videostreaming.md`
- **B. 运行时硬性约束 + C. 项目实测约束**——多次实机踩坑沉淀

> 跟推流无直接关系的项目通用接入要求（PICO Player Settings / XR Plug-in / AndroidManifest 权限 / UPM 包依赖等）见 [2.1 安装](#21-安装) / [2.2 依赖](#22-依赖) / [3 前置条件](#3-前置条件)。

##### A. webrtc 包官方明文要求

| 项 | 官方要求 | 项目当前 |
|---|---|---|
| Unity Editor | 2020.3 / 2021.3 / **2022.3** / 2023.1 LTS | ✓ 2022.3 |
| Scripting Backend（Android） | **IL2CPP** | ✓ |
| Target Architectures（Android） | **ARM64 only**，禁用 ARMv7 | ✓ |
| Internet Access | **Require** | ✓ `ForceInternetPermission=1` |
| Optimized Frame Pacing | **关闭**（issue #437：开启时 video PTS 时间戳异常） | 2022.3 默认关闭 |
| Audio System Sample Rate | 48000Hz 推荐（避免内部重采样） | AudioManager `m_SampleRate=0`（Best latency 自动选） |

**不支持目标平台**：Windows UWP / iOS Simulator / WebGL。

**iOS 额外**：Build Settings → Build Options → **Enable Bitcode = No**（本项目不打 iOS）。

**硬件编码器矩阵**（`videostreaming.md` L230-266）：

| 平台 | 编码器 | 备注 |
|---|---|---|
| Windows x64（DX11 / DX12 / Vulkan） | **NVCODEC** | NVIDIA 驱动 ≥ 456.71；同时活动 track ≤ 2 |
| Linux x64（GL Core / Vulkan） | NVCODEC | NVIDIA 驱动 ≥ 455.27 |
| macOS / iOS | VideoToolbox | — |
| **Android**（Vulkan 或 OpenGL ES） | **MediaCodec** | 设备 OEM 提供 H.264 |

##### B. 运行时硬性约束（违反时握手期报错）

| 限制 | 数值 | 触发后果 | 错误码 |
|---|---|---|---|
| 推流 RT 尺寸下限 | width ≥ 145，height ≥ 49 | `WebRTCSystem.Start` 入口预检 fail-fast | `InvalidStreamSourceSize` 4003 |
| 推流 RT 尺寸上限 | width ≤ 4096，height ≤ 4096 | `new VideoStreamTrack(rt)` 抛 native 异常 | `WebRTCInitFailed` 4101 |
| H.264 编码器 | 必须在 com.unity.webrtc 构建中可用 | SDP offer 不含 H.264 payload，握手期拦截 | `CodecNegotiationFailed` 4109 |

**RT 尺寸下限来源**：native 抛 `Texture size is invalid. minWidth:145, maxWidth:4096 minHeight:49, maxHeight:4096`。

**RT 尺寸预检踩坑**：Editor Game View 窗口拖到 145×49 以下、PICO XR 初始化异常 fallback 到非 XR 路径都会让源 RT 缩到非法尺寸。`MVXRStreamRig` 用 `StreamConfig.StreamMaxLongSide`（默认 1280）等比缩放后，理论下界 ≈ XR 渲染目标短边 / 长边 × 1280；业务自管 RT 时自己保证 ≥ 145×49。

##### C. 项目实测约束

| 实测 | 现象 | 处置 |
|---|---|---|
| **PICO 4U + Vulkan + WebRTC** | 编码器异常 / 帧不出（即使官方矩阵明示 Android Vulkan 支持 MediaCodec） | **强制 OpenGL ES3**——设备/驱动 specific bug，与 webrtc 包本身无关 |
| **libwebrtc GCC 慢启动** | 默认初始 BWE ~100kbps，前 ~40s 编码 fps 仅 2-30；接收端 ffmpeg 按 PTS 推算时 dup 帧卡死（PA9410 实测 `dup=158760 speed=0.00238x`） | `VideoMinBitrateKbps` 强制下限——详见 8.8.4 BWE 慢启动机制 |
| **仅局域网部署** | 项目未配 STUN/TURN，跨网段 ICE 失败（`IceConnectionFailed` 4108） | 保持 PICO 与 mediamtx 同子网，避免公网链路 |


#### 8.8.2 配置入口（推荐 Asset，兼容代码）

**入口 A · ScriptableObject Asset（推荐，无需写代码）**

**作用范围**：仅暴露 5 个视频编码参数——使用者通常需要调整的项：

| 字段 | 默认 | 说明 |
|---|---|---|
| `Fps` | 30 | 推流帧率上限（同时锁定 sender.maxFramerate） |
| `StreamMaxLongSide` | 1280 | 推流画面长边像素上限（按比例缩，0 = 不限） |
| `VideoBandwidthKbps` | 3500 | 码率上限（SDP b=AS + sender.maxBitrate） |
| `VideoMinBitrateKbps` | 1500 | 码率下限（sender.minBitrate，跳过 BWE 慢启动） |
| `ForceH264` | true | 强制 H.264 编码 |

**不包含**：握手超时、WHIP 重试节奏、DTLS 自愈窗口、Stats 间隔等 SDK 内部业务逻辑参数（这些走 SDK 默认；确需改走下面入口 B）。

使用步骤：

1. Project 窗口右键 → **Create → MyVerse XR SDK → Stream Config** 生成 `StreamConfig.asset`。
2. Inspector 调字段（鼠标悬停看中文 Tooltip——用途、单位、调参建议）。
3. 拖到场景中 `MVXRStreamRig` 的 **streamConfigAsset** 字段。
4. Rig `OnEnable` 时自动调 `StreamConfigAsset.Apply()` 写入生效配置——无需手写 `SetStreamConfig`。

Rig 上 streamConfigAsset 留空时全部走 SDK 默认。

Asset 与 SDK 的关系：

```
StreamConfigAsset (ScriptableObject, Inspector 编辑 4 个视频编码字段)
   └─ ToStreamConfig() → new StreamConfig()（其余字段保留 SDK 默认）
        └─ Apply() / MVXRSDK.SetStreamConfig(POCO)
             └─ StreamConfig.Active（SDK 内部全局生效）
```

`StreamConfigAsset.ToStreamConfig()` 返回**独立 POCO**：业务侧改返回值不会反向污染 Asset；Editor 调 Asset 也不会污染已经 `Apply` 的 Active。

**入口 B · 代码直接配置（需要改握手/重试/容错时走这条）**

业务侧需要运行时动态生成配置、或者要调握手 / 重试 / 容错这类 SDK 内部参数时走代码入口：

```csharp
var cfg = new StreamConfig
{
    // === 视频编码 ===
    Fps                  = 30,       // 推流帧率上限；同时落到 sender.maxFramerate 锁定编码器
    StreamMaxLongSide    = 1280,     // 推流画面长边像素上限（Rig 按比例缩到 InternalRT，0 = 不限）
    VideoBandwidthKbps   = 3500,     // 码率上限：SDP b=AS:N + sender.maxBitrate
    VideoMinBitrateKbps  = 1500,     // 码率下限：sender.minBitrate（跳过 BWE 慢启动）
    ForceH264            = true,     // 强制 H.264；局域网 + PICO 推荐 true

    // === 握手 / 网络 ===
    IceGatheringTimeoutSec   = 3,    // ICE 收集超时（non-trickle，局域网 < 1s 收齐）
    WhipHttpTimeoutSec       = 30,   // WHIP POST/DELETE HTTP 超时
    WhipHandshakeTimeoutSec  = 150,  // WHIP 握手协程总超时（覆盖重试预算）
    DeleteRetryDelaysMs      = new[] { 1000, 3000, 8000 },          // WHIP DELETE 重试节奏
    PushStreamRetryDelaysMs  = new[] { 2000, 5000, 10000 },         // 可恢复错误自动重试节奏

    // === 容错 / 监控 ===
    DisconnectedSelfHealSec  = 5,    // PeerConnectionState=Disconnected 自愈窗口
    StatsReportIntervalMs    = 1000, // OnPushStreamStats 回调间隔
};
MVXRSDK.SetStreamConfig(cfg);   // 注：SDK 保存引用，运行时不要再改原 cfg 字段
```

两条入口的字段含义、默认值、生效时机完全一致——下面 8.8.3 / 8.8.4 / 8.8.6 节通用。

#### 8.8.3 关键字段约束

| 字段 | 约束 | 违反后果 |
|---|---|---|
| `VideoMinBitrateKbps` | `0 < min ≤ VideoBandwidthKbps` | `SetParameters` 返回 `InvalidParameter`，编码参数回退默认 |
| `Fps` | 1-60，建议 ≤ XR 主循环频率（PICO 4 = 72/90） | > 60 时编码器丢帧但不报错；过低 PTS 步长大、画面卡顿 |
| `VideoBandwidthKbps` | 局域网 ≤ 上行带宽 | 超过上行实测带宽会丢包 + 重传，码率与 jitter 同步飙升 |
| `*TimeoutSec` | 全部 > 0 | 0 或负数会瞬间超时报错 |

#### 8.8.4 BWE 慢启动机制（PA9410 PTS 卡死修复背景）

`libwebrtc` 的拥塞控制（GCC）默认初始可用带宽估算 ~100kbps，需要数十秒爬升才能稳定。期间编码器主动降帧（H.264 低码率策略）：实测前 40 秒编码 fps 仅 2-30，PTS 步长不稳，**接收端 ffmpeg 会按 PTS 推算帧间隔大量 dup 帧（曾观察到 `dup=158760 speed=0.00238x`）直至卡死**。

修复路径：`VideoMinBitrateKbps` 在 `SetLocalDescription` 后通过 `RTCRtpSender.SetParameters` 写入 `encodings[0].minBitrate`，强制编码器从首帧起按目标码率输出。**局域网部署（项目默认形态）开箱即用**；公网/拥塞链路若发现起步丢包，把 `VideoMinBitrateKbps` 调到 500-800 之间。

#### 8.8.5 典型场景配置

```csharp
// 场景 A：局域网 + PICO（默认，无需改）
MVXRSDK.SetStreamConfig(new StreamConfig());

// 场景 B：低延迟优先（牺牲画质）
MVXRSDK.SetStreamConfig(new StreamConfig {
    Fps = 60, StreamMaxLongSide = 960, VideoBandwidthKbps = 2000, VideoMinBitrateKbps = 800,
});

// 场景 C：高画质（带宽充足局域网）
MVXRSDK.SetStreamConfig(new StreamConfig {
    Fps = 30, StreamMaxLongSide = 1920, VideoBandwidthKbps = 6000, VideoMinBitrateKbps = 2500,
});

// 场景 D：公网/弱网（不建议，超出当前部署设计）
MVXRSDK.SetStreamConfig(new StreamConfig {
    Fps = 30, StreamMaxLongSide = 720, VideoBandwidthKbps = 1500, VideoMinBitrateKbps = 300,
});
```

#### 8.8.6 运行时修改的注意事项

- **协商期字段**（`Fps` / `StreamMaxLongSide` / `VideoBandwidthKbps` / `VideoMinBitrateKbps` / `ForceH264` / `IceGatheringTimeoutSec`）只在 `WebRTCSystem.Start → NegotiateOffer` 或 Rig Attach 期间读取一次，**推流进行中改不会重建 RT / 触发重协商**——必须 Stop → Start。
- **每次推流读取**字段（`WhipHttpTimeoutSec` / 各 `*RetryDelaysMs` / `DisconnectedSelfHealSec` / `StatsReportIntervalMs`）下一次相应触发即生效。
- `SetStreamConfig(null)` 等价于恢复全部默认值（`new StreamConfig()`）。
- 完整字段定义见 [`StreamConfig.cs`](../Runtime/Scripts/Operation/Stream/StreamConfig.cs)。

------

## 9. 日志级别控制

```csharp
MVXRSDKLog.SetMinLevel(MVXRSDKLog.Level.Warning);  // 生产期屏蔽 Debug/Info
MVXRSDKLog.SetTag("MyGame.SDK");                    // 自定义标签前缀
```

编译期完全关闭：Player Settings → Scripting Define Symbols 添加 `MVXRSDK_LOG_DISABLED`。

------

## 10. 错误码体系（v2 新增）

`MVXRSDKErrorCode` 按业务域分段（数值序号是稳定契约，可 `(int) cast` 用于埋点/服务端日志）：

| 段 | 域 | 示例 |
|---|---|---|
| 1xxx | 通用/状态 | `NotInitialized` 1001 / `InvalidArgument` 1003 |
| 2xxx | 网络/Socket | `SocketConnectFailed` 2002 / `ProtobufParseFailed` 2005 |
| 3xxx | 房间/中控 | `LoginFailed` 3003 / `RoomAllocateFailed` 3001 |
| 4xxx | 推流 | `NoStreamSource` 4001 / `WhipPostFailed` 4103 |
| 5xxx | 录屏 | `RecordRemoteRejected` 5004 / `RecordTimeout` 5005 |
| 6xxx | 节点/空间 | `NodeNull` 6001 |
| 7xxx | 积分 | `TransactionFailed` 7001 |

完整清单见 [`MVXRSDK.cs`](../Runtime/Scripts/MVXRSDK.cs) 顶部 `public enum MVXRSDKErrorCode`。

------

## 11. 常见问题

- **Q：初始化抛 ArgumentException？**
  A：v2 起 `deviceId` 入参严格校验——不可空、不可超 64 字符、不含 ` ` `/` `?` `#`。

- **Q：节点注册时机不对？**
  A：v2 起任何时机皆可（推翻 v1.x「必须 Init 前」约定）。

- **Q：积分扣除验证失败？**
  A：检查网络连通性、账户积分、包名是否提交。订阅 `MVXRSDK.OnError` 接细致错误码。

- **Q：注册多个根节点时，场景偏移如何应用？**
  A：所有已注册的根节点会同步应用相同的位置偏移与旋转。

------

**文档生成时间**：2026-05-20
**适用版本**：MVXRSDK v2.x（Unreleased，内测中）

详细更新记录请参阅 [CHANGELOG.md](../CHANGELOG.md)（含 v1.x → v2.x Migration Guide）。

技术支持：参考相关开发文档或联系 `support@myverse.com`。
