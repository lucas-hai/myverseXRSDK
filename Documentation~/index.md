# MVXRSDK 4 接入指南

> 命名空间：`MyVerseXRSDK`。本主版本不兼容 3.x C# API；后端 Protobuf、区域轴向和账务字段语义保持不变。版本号见 `package.json`，发布状态见 CHANGELOG。

## 1. 架构与能力

```text
MVXRSDK.Create(SdkOptions) → SdkClient
  ├─ Session       房间接入、状态与外部会话租约
  ├─ Space         地块匹配、逆校准、障碍物、区域地面
  ├─ Devices       本机R帧采样、远端设备与虚影
  ├─ Admission     开始游戏通知，不自动扣费或开局
  ├─ Billing       显式积分验证、每次初始化成功一次
  ├─ Director      仲裁请求与候选相机
  ├─ Streaming     独立画面源、游戏音、WHIP/WebRTC
  ├─ Recording     远端限时录制命令
  └─ Diagnostics   当前实例的结构化错误
```

运行时不再自动创建旧 `MVXRSDKManager`，不再使用静态 Manager/Module、全局事件总线或相机 Reporter。`Create` 才创建所属实例的常驻调度宿主。

核心程序集为 `MVXRSDK.Contracts`、`MVXRSDK.Runtime`、`MVXRSDK.Editor`；分场景接入示例使用 `MVXRSDK.EditorDemos` 和 `MVXRSDK.EditorDemos.Editor`。自建 asmdef 的业务代码至少引用 Contracts、Runtime；Editor 和示例程序集不应成为业务 Runtime 的依赖。

## 2. 安装与依赖

Unity 2022.3 LTS，URP 为必需依赖。包清单声明：

| 包 | 版本 |
|---|---|
| `com.unity.webrtc` | `3.0.0-pre.8` |
| `com.unity.nuget.newtonsoft-json` | `3.0.2` |
| `com.unity.render-pipelines.universal` | `14.0.11` |

可将完整包目录放到工程 `Packages/` 下，或通过 Package Manager 的本地 `package.json` 导入。使用 Git 分发时锁定**实际已发布**的 tag/commit；本地版本变更不代表远端已发布。不要同时保留旧 SDK DLL 或拷贝到 Assets 的旧脚本。

开发包直接打开 `Samples/EditorDemos/Scenes/`，不在同一工程重复导入；否则会出现同名示例程序集。发布脚本在临时分发副本中转换为 `Samples~/EditorDemos`，分发包通过 Package Manager 的 **Editor Integration Demos** 导入。七个场景分别展示连接、空间/设备、业务命令、直接WHIP、中控推流和跨场景；操作步骤见示例目录的README。旧联合Demo及其专用程序集已删除；升级时自行处理旧示例副本，避免其旧API调用参与编译。

SDK 不直接引用 PICO 程序集；设备身份获取、XR Plug-in Management 和 Android 权限由宿主负责。目标仍为 PICO 4/4U；其他设备须自行验证。

## 3. 生命周期与接入方式

同一进程最多存在一个未完全释放的客户端。`Create` 捕获配置快照并校验输入；失败不占用单实例名额。

| 操作/状态 | 含义 |
|---|---|
| `Create` / `Created` | 已有本地实例，尚未启动网络 |
| `Start()` / `Running` | 本地服务运行，不等于已经登录或完成校准 |
| `Session.Current.IsJoined` | 当前房间已登录 |
| `Space.Status.IsApplied` | 当前有效校准已应用到绑定 |
| `Shutdown(completed)` | 延后到主线程调度点开始关闭，完成时资源已安全收敛 |
| `Dispose()` | 立即发起非阻塞关闭；有在途 native 使用时仍保持 `Stopping` |
| `Disposed` | 终态，不可重新 Start；此后才可 Create 下一实例 |

### 三种会话模式

- **Directory**（正式，默认）：固定POST `http://localhost:8868/v1/vr/service/config`取得中控地址，再轮询分配、连接房间WS、登录。IP、端口和接口路径内置固定，接入方无需填写地址。
- **ControlServer**：显式设置 `Session.ControlServerUrl`，跳过本机目录。这里传**中控 HTTP(S) 地址，不是房间 WS 地址**。
- **External**：不建立原生网络连接；调用 `Session.OpenExternalSession(sessionKey)` 取得专属数据写入句柄。

### 正式与测试的共同连接流程

| 阶段 | 正式 Directory | 测试 ControlServer | 观察点 |
|---|---|---|---|
| 取得中控地址 | POST固定本机接口；读取`data.serveIp`、账务`data.baseUrl` | 使用`ControlServerUrl`，不访问本机目录 | 正式经过Discovering；两者随后Allocating |
| 轮询设备分配 | GET `{中控地址}/v1/open/api/device?id={DeviceId}` | 同左 | 默认前次请求结束后间隔1秒，不重叠；入房后仍继续 |
| 取得房间地址 | 响应`Code == 0`，读取字符串`Data.CenterIp` | 同左 | 非空有效地址用于WS/WSS，不是另一个中控HTTP地址 |
| 连接并登录 | 连接房间WS，发送`logic.Binding`，Token为DeviceId | 同左 | Connecting → Authenticating → Joined |
| 接收空间数据 | 登录区域/后续推送；查询`logic.QueryGameSceneInfo`获取障碍物 | 同左 | Joined不等于空间Ready，需完整Rig和匹配地块 |
| 未分配/房间解散 | `Data.CenterIp == ""`：无房时继续等待；已有房间则结束旧会话并继续轮询 | 同左 | 不卸载SDK，不重置该初始化周期的计费成功缓存 |

联调前协调中控网页控制台开房并拉入目标设备；DeviceId必须属于中控“设备管理”，随意填写不能完成登录。缺字段、null、错误类型或非法地址不是合法空字符串，SDK报告错误，不据此冒充房间解散。正式目录请求失败会进入Failed；不能将设备分配的持续轮询理解为目录发现也会自动重试。

连接实现依据：[NativeSessionConnection](../Runtime/Scripts/Client/Session/NativeSessionConnection.cs)。上表描述客户端行为，不承诺所有后端换房都必然先返回一次可观察的空字符串。

`Session.NetworkLimits` 控制单消息/解压、单方向队列字节/条数、每帧派发和待应答请求上限；超限返回或报告CapacityExceeded。设备与障碍物数量分别由Devices.MaximumDevices和Spatial.MaximumObstacles限制，Native与External同策略。默认值仅防失控，不是压测性能保证。

`Session.BillingBaseUrl` 可显式提供账务端点；未配置且原生会话尚未取得端点时，账务操作返回不可用，不伪造通过。

### 最小绑定与启动

以下变量由宿主提供；生产 `deviceId` 应在设备身份获取成功后传入，不记录到日志。

```csharp
var client = MVXRSDK.Create(new SdkOptions
{
    DeviceId = deviceId,
    Session = new SessionOptions
    {
        Mode = SessionConnectionMode.ControlServer,
        ControlServerUrl = controlServerUrl
    }
});

var binding = client.Space.BindRig(
    new SpatialRigBinding(motionRoot, calibrationRoot, trackingRoot, head));
if (!binding.Success)
{
    Debug.LogError(binding.Result.Message);
    client.Dispose();
    return;
}
var started = client.Start();
if (!started.Success) { Debug.LogError(started.Message); client.Dispose(); }
```

保留 `client` 和 `binding.Handle`。切换 XR 层级时可事务式重新 BindRig；旧句柄释放不会解除新绑定。场景卸载前释放绑定，客户端本身可跨场景；整个业务结束时再 Shutdown。不要手动销毁 SDK 调度宿主。

## 4. 空间坐标与节点所有权

推荐层级：

```text
MotionRoot                         业务移动层B（业务拥有）
└─ CalibrationRoot                专用校准层C（SDK独占写入）
   ├─ TrackingRoot / CameraOffset  XR追踪层（宿主/XR系统拥有）
   │  └─ Head / Hands
   └─ MVXRSDK.PhysicalProxies      SDK创建的柱子、虚影和区域地面
```

- 地图保持规范游戏场地坐标 **G**，SDK 不移动地图。
- 服务端区域、障碍物及设备位姿使用协议房间坐标 **R**。
- `GameToRoom` 为 **T=G→R**；`RoomToGame` 为 **C=T⁻¹**。
- `MotionRoot` 的世界刚体变换为 **B**。最终 `pW = B × C × pR`。
- SDK 只写 `CalibrationRoot.localPose` 以及自有代理，不写 MotionRoot、TrackingRoot、Head、Hands。
- CalibrationRoot 必须独占、初始为单位位姿；相关节点及祖先必须单位缩放。

保留原协议正向公式：

```text
q = regionRotation × gameOffsetRotation
t = regionCenter + regionRotation × gameOffset − tilePosition
C.rotation = inverse(q)
C.position = inverse(q) × (−t)
```

地块 rotation 不进入 T。不能只把 t 取负充当完整逆变换。

默认 **HorizontalYaw**：高度容差 0.001m、pitch/roll 容差 0.5°；容差内归零，超出拒绝，不静默截断。**Rigid3D** 保留完整刚体变换，但网络位姿协议仍只发送 XZ/yaw。

`Space.TryGetRoomPose` 从 Head 相对 CalibrationRoot 的变换采样，包含 Camera Offset 等下层偏移；不能用 `Head.localPosition` 替代。`TryRoomToWorld` 使用当前绑定组合 B×C，`TryWorldToRoom` 使用其逆将任意世界位姿转回R帧，包含MotionRoot当前业务移动。双向查询不修改节点；参考帧失效、外部争写、非单位缩放或输入非法时返回false、输出default，不能用StaleHold继续转换。

### 状态与重连

- 无绑定、缺区域或地块不匹配时，表现不启用；查询明确区分 `Unbound`、`WaitingForRegion`、`Unavailable`。
- 计算成功不等于应用成功，分别看 `TryGetCalibration` 与 `Status.IsApplied`。
- 重连时保持上一 C，但标记为陈旧，不继续把它当作有效上报参考帧；等待新代次有效区域。
- 外部改写 CalibrationRoot 会失去应用有效性，SDK 不抢写覆盖业务数据。
- XR recenter/追踪原点改变后调用 `InvalidateTrackingOrigin()`；External 模式必须重新开会话并提供新帧数据。

### 区域、障碍物与虚影

地块默认加载 `Resources/MVXRSDK/LocalRegionData`，按激活环境组验签、索引。规格 id 保持**宽x长、小写 x、绝对值截断取整**。可通过 `Spatial.TileSource` 注入自有只读地块源。

障碍物按 Id 全量差分；等数量换 Id、尺寸变化和形状切换都会重新处理。矩形保持 Len→X、Width→Z 的协议映射。距离与虚影采样在 R 坐标内，不受业务移动层 B 干扰。

区域地面使用 **Region 自身 Center/Rotation/尺寸**，作为代理根的子对象；不再作为独立常驻地图根显示，也不把 T 或 C 当作它的局部位姿。

设备字典只读且排除本机。按RoomId区分同房与不同房：**不同房虚影默认启用、距离2m；同房默认关闭**。`Devices.SetSameRoomVisibility(true, distance)` 开启后，同房和不同房虚影可以同时显示，不是二选一；关闭只隐藏同房表现，不丢弃缓存、不关闭不同房显示。静止远端也会在本机靠近时重新评估可见性。

柱子与人物虚影是**真实空间防碰撞的视觉提示**，但当前实现不是完整安全保障：表现依赖有效空间参考帧及后端广播；断线/校准失效会隐藏或清理表现，不能把“未显示”理解为“现实中无障碍/无人”。远端设备没有位姿时间戳过期判定，缺少退出消息时可能保留最后位置；跨房广播还须由中控提供同一物理坐标基准。柱子显隐基于XZ中心距离加尺寸估算，不是精确柱面距离；需在现场验证距离、延迟和失效处置。

实现依据：[DevicePresenceService](../Runtime/Scripts/Client/Devices/DevicePresenceService.cs)、[RemoteDeviceStore](../Runtime/Scripts/Client/Devices/RemoteDeviceStore.cs)、[ObstaclePresenter](../Runtime/Scripts/Client/Space/ObstaclePresenter.cs)。当前SDK没有柱子/虚影专属的暂停、透视、声音或震动失效兜底。

`ComputeOnly` / `Disabled` 模式须同时关闭 `Spatial.ShowObstacles`、`Spatial.ShowFloor`、`Devices.ReportingEnabled`、`Devices.AvatarsEnabled`，否则配置校验拒绝矛盾组合。

## 5. 外部数据源

```csharp
// 客户端须以External模式Start；每次开会话都会使上一个writer失效。
var writer = client.Session.OpenExternalSession("room-session-key");
writer.ApplyRegion(1, regionUpdate);
writer.ApplyObstacles(1, obstacleSnapshot);
writer.UpsertDevice(1, devicePose);
// 位姿、障碍物、区域和Live各有独立revision；设备域不是每个device独立计数。
```

输入限主线程，revision 必须递增。非法输入不消耗 revision；旧代次返回 `StaleGeneration`。不要把服务器 PB 对象暴露到业务层或绕过 writer 调内部服务。

External 联调开流使用 `writer.NotifyLive(revision, fullWhipUrl, true)`，停流传 false。URL 必须是完整 HTTP(S) WHIP 地址，不是单独 IP。此操作会真实创建媒体会话，不是纯画面模拟；不要把含凭据的地址写日志。

## 6. 游戏准入、扣费与远端命令

`Admission.StartRequested`通知本机开始游戏请求，由业务处理开局；SDK不在这条链中发扣费HTTP。中控启动与自助扣费是不同接入方式，不应把收到中控通知自动串接成一次自助扣费。

自助接入显式调用`client.Billing.VerifyAccess(completed)`，遵守“初始化一次，扣一次费”。当前客户端成功后，后续调用都返回缓存；房间重连/解散/重入、场景切换或同周期新一局不触发重扣。完整释放后创建新客户端才开启新周期，不按整个进程永久去重，也不提供任意周期标识或关闭缓存开关。账务字段仍遵守既有后端契约；SDK不在初始化时自动扣费，不自动重试可能重复扣费的请求。

账务、导演和录制返回 `OperationHandle`；完成回调为 `OperationResult`，至多一次且延后派发。结果含 RequestId、TargetId、WasCached、RemoteOutcomeUnknown。`Cancel()` 仅取消本地等待，不撤销已发出的服务端动作；未知远端结果不能当作明确失败后直接重发。

录制按目标类型和目标 Id 做在途互斥：不同目标可并发。`Recording.Start` 成功只表示命令受理，不表示文件已完成。SDK 不做本地视频录制，也没有主动 StopRecord。

## 7. 导演、画面源与游戏音

导演请求示例：

```csharp
client.Director.Request(new DirectorRequest
{
    Source = "unity",
    AutoCamera = directorCamera,
    DurationSeconds = 30
}, result => Debug.Log("导演请求受理结果：" + result.Success));
```

受理 ≠ 选中 ≠ 已连接。`Director.Selected` 为协议观察事件；本机新有效开流意图到达后触发 `Streaming.Starting`，媒体连接完成后 State 为 `Streaming`。自动恢复不再次消费导演候选，也不再次发布新的选中信号。

- 有 `AutoCamera` 时，SDK 在被选中后自动接源；手动源优先；自动源在控制会话终止时释放。
- 无相机时空 Source 保留为空（原直播合法值）；指定 AutoCamera 且空 Source 才补 `unity`。
- 候选相机有代次与过期限制；`CancelPendingCamera()` 不会撤销已受理的远端仲裁或停止正在推的流。

### 画面源所有权

`Streaming.TrySetSource(IStreamSource)` 返回 `Accepted / Busy / Invalid`。**只有 Accepted 转移源适配器的所有权**；被拒对象仍由调用者负责 Dispose。

提供 `CameraStreamSource`（独立相机直渲普通 RT）和 `RenderTextureStreamSource`（普通 Tex2D Blit）。底层相机/外部 RT 为借用对象，不因源适配器释放而被销毁。

推流活跃且已有不同源时返回 Busy，不抢占、不排队；同源重复提交幂等。空闲时可以替换。输出固定 16:9、偶数对齐，默认 1280×720；无源时推黑帧。清源不销毁编码器仍借用的输出 RT。

手动源默认跨正常停流保留；场景卸载前必须 `ClearSource()`，随后再销毁借用相机或 RT。相机 targetTexture 被业务重新接管后，SDK 不覆盖业务的新设置。

### 音频与配置

音频只有游戏音，不采麦克风。使用 `CaptureGameAudio(listener)` 取得可释放绑定句柄，或调用 `PushGameAudioPcm(pcm, sampleRate, channels)`。支持 8000–192000Hz、mono/stereo；stereo 折叠为 mono，连续重采样，有界缓冲写满丢最旧、读空补零。

`MVXRStreamRig.Bind(client)` 显式绑定实例，应用 `StreamConfigAsset` 的完整媒体配置并采集指定 Listener；组件不拥有主相机或切镜编排。

`ApplyConfiguration` 要求 Version 递增：采集节流和重试策略即时更新；协商参数下一媒体会话生效。更改输出尺寸前须无媒体会话且已清源；音频环形缓冲容量仅创建客户端时配置。

关闭先阻止新音频写入和 native 使用，再等待在途回调归零。等待过久报告 `AudioDrainStalled`，仍保持 Stopping，不靠超时强行释放音轨。

### WHIP 与平台边界

- 仅局域网，无 STUN/TURN；使用 non-trickle ICE。
- HTTP 使用协程；POST 迟到的 Location 在有限清理预算内仍被接管。
- 所有结束路径统一进行一次 best-effort DELETE；200/204/404 视为完成，失败或超预算有诊断，不承诺远端必达、不自动反复 DELETE。默认远端清理预算 2 秒；它不限制 native 安全释放等待。
- 强制 H.264 默认开启；不支持时明确失败。码率下限不能高于上限；配置不能保证设备一定达到目标码率或帧率。
- Vulkan 历史问题发生在读取 XR eye buffer/FDM 的采集层。本链使用独立普通 RT，不新增主相机、eye buffer 或屏幕抓帧路径。PICO/Vulkan 的最终结论必须来自真机回归。

## 8. Editor 工具与迁移

[Editor 工具指南](editor-tools-guide.md) 描述平台验证、规格和地块编辑入口。正式/测试/离线环境、资产字段与封签规范保留。

异步规格/上传/封签操作捕获固定环境、端点、凭据与资产修订；切环境不重定向在途请求，资产已变时拒绝覆盖。远端可能已经接受的写操作不自动回滚或重试。

`MVXRSDKSettingsAsset.requireSpatialDataForBuild` 默认 true：缺必要地块、封签无效或环境不一致会阻断构建。非正式环境默认警告；`failOnNonProductionBuild` 可设为错误。CI 与交互构建执行同一规则，不再通过“继续出包”绕过。

封签是本地一致性措施，不是服务端授权签章。凭据不得进入业务日志、测试输出或报告。

### 从3.x迁移

1. 所有静态业务入口改成 `Create` 返回的客户端域服务；程序集引用同步更新。
2. 移除场景根节点偏移方案；显式插入单位 CalibrationRoot，完整绑定四节点。
3. `SpatialRigSetup.EnsureCalibrationRoot` 是可撤销的 Editor 装配辅助，不保存场景。检查层级后由开发者手动保存。
4. 保留的PICO/宿主旧场景序列化引用保留，但**不会自动补齐新增四节点接线**；不要假设打开旧场景即可直接运行。
5. SDK 默认 prefab 的脚本 GUID 保留；NetworkTransform/SpaceObstacles 已变为内部被动视图，不再挂到玩家相机作为 Reporter。
6. `StreamConfigAsset.ToMediaOptions/ApplyTo` 替代旧 ToStreamConfig/Apply；手动源被拒时自行释放。
7. 区分准入、扣费、导演受理、媒体连接、录制文件完成，不能沿用旧 bool 事件推断后续阶段。
8. `ShowNetworkFailureHud` 默认 true；终态失败且已绑定 Head 时显示，换绑/结束/释放自动收敛。自定义业务 UI 可关闭它。

## 9. 验证范围

测试位于宿主 `Assets/Tests`，不随 SDK 分发。EditMode 验证算法、状态、所有权、协议映射和 Editor 用例；PlayMode 验证真实帧驱动、销毁和异步关闭。替身媒体测试不等于硬件编码通过。

实例日志通过创建配置`SdkOptions.Logging.Sink`注入，包含状态变化、告警和捕获异常；支持等级过滤、独立Unity输出开关、有界主线程延后派发及关闭排空。日志开关不改变业务错误结果或事件。`Diagnostics.DroppedLogCount`可观察sink积压丢弃；sink自身异常/递归与超限提示走Unity兜底。该出口不接管Editor、宿主或引擎原生日志，详细默认值与脱敏边界见API参考。

发布前还须独立工程导入、真实中控/房间/WHIP/WSS 联调、Android 构建及 PICO 真机/Vulkan 回归。开发中的本地测试数字不能代替这些验收。

完整签名与配置见 [API 参考](api-reference.md)。
