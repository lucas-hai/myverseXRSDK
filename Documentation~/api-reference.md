# MVXRSDK 4 API 参考

适用于实例化 API，命名空间 `MyVerseXRSDK`（装配组件 `MVXRStreamRig` 位于 `MyVerseXRSDK.Streaming`）。接入流程、坐标公式和迁移步骤见 [开发指南](index.md)。3.x 静态业务接口与旧错误枚举已删除。

## 1. 通用约定

- 除 PCM 写入外，所有调用、事件订阅和句柄释放在 Unity 主线程执行。
- Create 参数错误抛 `ArgumentException`；已有未完全释放客户端时抛 `InvalidOperationException`。运行期业务拒绝主要通过 `SdkResult` / `OperationResult` 返回；对已释放实例调用受保护入口可抛 `ObjectDisposedException`，不要假定所有失败都仅记日志。
- 普通状态事件同步发布；命令完成回调延后派发且至多一次。订阅者异常记录后不阻断其他公开订阅者。
- 客户端拥有其域服务，不要独立 Dispose 某个域后继续复用整个客户端。业务释放客户端、绑定/采集句柄及未被接受的源适配器即可。
- 配置和数据快照不长期借用调用者可变数组；Camera、Transform、Prefab、RenderTexture、地块源对象按各自契约借用。

### 按接入任务查找API

| 任务 | 入口 | 成功判断与后续责任 |
|---|---|---|
| 本地初始化 | `MVXRSDK.Create` → `client.Start()` | Running只代表本地服务启动 |
| 正式/测试中控接入 | `SessionOptions.Mode`、`ControlServerUrl` | 等待`Session.Current.IsJoined`；不手工填写房间WS |
| 空间接入 | `Space.BindRig` | 保存Handle，等待`Status.IsApplied` |
| 本机/远端设备 | `TryGetRoomPose`、`Devices.Current` | 都是R帧；本机采样不等于已上报成功 |
| 同房间虚影 | `Devices.SetSameRoomVisibility` | 仅控制同房显示，不关闭跨房虚影 |
| 中控开局通知 | `Admission.StartRequested` | 业务决定开局，不串接自动扣费 |
| 自助计费 | `Billing.VerifyAccess` | 查Success/WasCached/RemoteOutcomeUnknown |
| 导演机位 | `Director.Request` | 受理后等待中控NotifyLive，不在回调中自行开流 |
| 直接WHIP | External客户端的`writer.NotifyLive` | 显式联网动作；不是中控登录路径 |
| 远端录制 | `Recording.Start` | 成功只表示受理，不代表文件落盘 |
| 场景/客户端结束 | 绑定Handle、`ClearSource`、`Shutdown` | 先撤销场景资源引用，等待安全释放，不阻塞主线程 |

示例入口见[包README](../README.md)，实际操作步骤见EditorDemos目录内的README。

## 2. 工厂与客户端

```csharp
SdkClient MVXRSDK.Create(SdkOptions options);
SdkResult SdkClient.Start();
void SdkClient.Shutdown(Action<SdkResult> completed = null);
void SdkClient.Dispose();
```

属性：`RuntimeId`、`State`，以及 `Session / Space / Devices / Admission / Billing / Director / Streaming / Recording / Diagnostics`。

状态：`Created → Starting → Running → Stopping → Disposed`，启动失败可为 `Faulted`。Running 不表示已联网；Disposed 不可重启。Shutdown 回调执行前本地资源必须安全释放；Dispose 非阻塞，有在途 native 使用时保持 Stopping。

## 3. 会话与外部写入

`SessionService`：

| 成员 | 类型/签名 |
|---|---|
| State | `SessionState` |
| Current | `SessionSnapshot`：Epoch、State、RoomId、IsJoined |
| LastError | `SdkResult` |
| Changed | `event Action<SessionSnapshot>` |
| Error | `event Action<SdkResult>` |
| OpenExternalSession | `ExternalSessionWriter OpenExternalSession(string sessionKey)` |

SessionState：`Disabled / Discovering / Allocating / Connecting / Authenticating / Joined / Reconnecting / Ended / Failed`。

`ExternalSessionWriter` 只属于打开它的客户端及会话，以下方法返回 `SdkResult`：

```csharp
ApplyRegion(long revision, RegionUpdate region);
ApplyObstacles(long revision, IReadOnlyList<ObstacleUpdate> obstacles);
UpsertDevice(long revision, DevicePose device);
RemoveDevice(long revision, string deviceId);
NotifyLive(long revision, string url, bool start);
```

Dispose 结束它仍拥有的会话，不影响替换它的新会话。各域 revision 必须递增；设备新增/更新/删除共用设备域 revision。非法值不消耗 revision，旧租约不能写入新代次。NotifyLive 的 start 地址为完整 HTTP(S) WHIP URL。

## 4. 空间与设备

`SpatialRigBinding(Transform motionRoot, Transform calibrationRoot, Transform trackingRoot, Transform head, ...)` 表达完整绑定；可选追踪节点集合用于验证层级。节点及祖先单位缩放，CalibrationRoot 由 SDK 独占写入。

| `SpatialService` 成员 | 返回/事件类型 | 语义 |
|---|---|---|
| `BindRig(SpatialRigBinding)` | `SpatialBindingResult` | Result、Success、Handle；失败保留原绑定 |
| `TryGetCalibration(out CalibrationResult)` | bool | 当前会话有效计算结果 |
| `TryGetRoomPose(out Pose)` | bool | 当前应用参考帧下的本机R位姿 |
| `TryRoomToWorld(Pose, out Pose)` | bool | 通过当前B×C转换；参考帧不可用或位姿非法时false，输出default |
| `TryWorldToRoom(Pose, out Pose)` | bool | 通过当前(B×C)逆变换；包含业务移动，失败边界同上，不修改节点 |
| `InvalidateTrackingOrigin()` | void | 撤销原点改变前的数据代次 |
| Status | `SpatialStatus` | State、HasCurrentResult、IsApplied、IsCurrent、Reason |
| LastRejectedInput | `SdkResult` | 最近一次被拒的空间输入 |
| Changed | `Action<SpatialStatus>` | 空间状态 |
| CalibrationChanged | `Action<CalibrationResult>` | 计算结果，不自动代表应用完成 |

`SpatialBindingHandle.Dispose()` 释放该次绑定；旧句柄不解绑后续新绑定。

SpatialState：`Disabled / Unbound / WaitingForRegion / Unavailable / Computed / Ready / StaleHold / Invalidated / BindingInvalid`。

`CalibrationResult`：RuntimeId、SessionEpoch、RegionRevision、TileId、TileVersion、GameToRoom（T）、RoomToGame（C）、Mode。

`DevicePresenceService`：

- `IReadOnlyDictionary<string, DevicePose> Current`：只读设备值，不含本机。
- `SdkResult SetSameRoomVisibility(bool enabled, float distance)`：距离为有限非负数，立即重评缓存。
- `event Action<DevicePose> LocalPoseSampled`：只表示采样完成，不表示服务器收到；External 宿主可转交自有传输。

数据类型均为值契约：

- `RegionUpdate`：Length、Width、Center、Rotation、GameOffset、GameOffsetRotation、ShowFloor。旋转为 Quaternion。
- `ObstacleUpdate`：Id、Shape（Box/Ellipse）、Position、Length、Width、Radius。
- `DevicePose`：DeviceId、RoomId、**string ModelId**、Position、Rotation。
- `RegionTile` 与 `IRegionTileSource.Version / Find(string id, out RegionTile)`：自有只读地块源。

## 5. 准入、账务、导演、录制

`AdmissionService.StartRequested` 为 `Action<GameStartRequest>`，`LastRequest` 为可空请求快照；它不自动扣费或开局。

```csharp
OperationHandle BillingService.VerifyAccess(Action<OperationResult> completed);
OperationHandle DirectorService.Request(DirectorRequest request, Action<OperationResult> completed);
void DirectorService.CancelPendingCamera();
OperationHandle RecordingService.Start(RecordingRequest request, Action<OperationResult> completed);
```

`Billing.VerifyAccess`按一次客户端初始化周期计费：成功后所有后续调用返回`WasCached=true`，不再次发送HTTP；房间重连/解散/重入及场景切换不清此状态。完整释放后新建客户端才是新周期。没有自定义计费周期或绕过成功缓存的参数；不在Create/Start自动扣费。

`DirectorService.Selected` 为 `Action<DirectorSelection>`，仅观察仲裁协议结果；`Streaming.Starting` 才是本机开始本次开流控制意图的信号。

| 请求 | 字段与默认值 |
|---|---|
| DirectorRequest | `Source=null`（无相机时保持空协议值）；`Lenses=1`；`DurationSeconds=10`；`Record=false`；`FileName=null`；`AutoCamera=null` |
| RecordingRequest | `TargetKind=Device`（另有Camera）；`TargetId` 必填；`DurationSeconds=60`；`FileName=null` |

导演镜头数至少1，导演/录制时长为正。AutoCamera 为借用，只有 Source 空或 unity 时合法。`DirectorRequest.FileName`仅在Record=true时有意义，协议约定填写不含扩展名的名称（如`business-record`）；SDK原样透传，不增删后缀，不保证服务端兼容带扩展名的输入。录制按 TargetKind+TargetId 互斥，不同目标可并发。

`OperationHandle`：RequestId、TargetId、IsCompleted、Outcome，`void Cancel()`。Cancel 不能撤销远端动作。

`OperationResult`：RequestId、TargetId、Result、Success、WasCached、RemoteOutcomeUnknown。远端结果未知时不能把请求当作从未发生；这些非幂等命令不自动重试。

## 6. 推流

创建客户端时须显式设置`SdkOptions.EnableStreaming=true`（默认false），`Streaming.IsEnabled`为捕获后的只读值。配置更新或Rig绑定不改变能力授权。禁用收到NotifyLive只报告Unavailable诊断，不启动媒体或重试。

```csharp
MediaOptions StreamingService.GetConfiguration();
SdkResult StreamingService.ApplyConfiguration(MediaOptions value);
SourceSetResult StreamingService.TrySetSource(IStreamSource source);
SdkResult StreamingService.ClearSource();
SdkResult StreamingService.PushGameAudioPcm(float[] pcm, int sampleRate, int channels);
AudioCaptureHandle StreamingService.CaptureGameAudio(AudioListener listener);
```

属性：`IsEnabled`、`State`、`Statistics`、`CurrentSource`。不要直接 Dispose 已接受的 CurrentSource，应调用 ClearSource。

事件：`Action Starting`、`Action<StreamingState> Changed`、`Action<StreamingStatistics> StatisticsUpdated`。Starting是新控制意图的源准备时机，发生在Peer创建前；业务同步回调及导演自动相机准备完成后才检查源。默认无有效源则诊断Unavailable并回到Idle，清除本次意图，不因补源/配置更新自动开流；新的通知可重试。AllowBlackFrameStart=true显式允许首次黑帧，已接纳控制会话的清源/丢源及自动恢复保持黑帧。

StreamingState：`Idle / Starting / Streaming / Recovering / Stopping / Failed`。

SourceSetResult：Accepted（转移所有权）、Busy、Invalid（后两者不接管、不Dispose输入）。禁用时TrySetSource返回Invalid，不分配输出；推流中已有不同源时Busy，同源幂等。禁用时CaptureGameAudio抛InvalidOperationException，PushGameAudioPcm返回Unavailable，Rig.Bind返回Unavailable；GetConfiguration/ApplyConfiguration可管理参数但不能开启能力。

`StreamingStatistics`：SessionEpoch、Timestamp、IsValid、VideoBitrateKbps、AudioBitrateKbps、TotalBitrateKbps、RttMilliseconds、PacketsLost。停止后统计失效；不要把旧统计当成新会话数据。

### 画面适配与装配组件

- `CameraStreamSource(Camera)`：独立相机直渲 SDK 输出；相机为借用。
- `RenderTextureStreamSource(RenderTexture)`：外部普通 Tex2D，拒绝数组纹理和自Blit；外部RT为借用。
- `IStreamSource`：Width、Height、DisplayName、IsAvailable；Attach(RenderTexture)、Detach()、Tick(double now, int framesPerSecond)、Dispose()；OnAttached/OnDetached 事件。源被接受后由 Runtime 调度，禁止自行注册旧全局帧回调。
- `AudioCaptureHandle`：Listener、IsDisposed、Dispose；旧句柄不解绑替换后的新 Listener。
- `StreamConfigAsset.ToMediaOptions()`：完整有效快照；`ApplyTo(StreamingService)`：递增运行版本后整体应用。
- `MVXRStreamRig.Bind(SdkClient)`：返回 SdkResult，借用指定客户端；组件禁用时释放其音频采集句柄，不释放客户端。

PCM 可在音频线程写入：8k–192kHz，1或2声道、完整采样帧。调用返回后缓冲内容不再被借用。其他Unity操作仍须在主线程执行。

## 7. 配置默认值

### SdkOptions / SessionOptions

| 配置 | 默认值/约束 |
|---|---|
| DeviceId | 必填、非空白、≤64字符，不含空格及 `/ ? #` |
| EnableStreaming | false；Create时捕获，媒体参数及Rig不能隐式开启 |
| ShowNetworkFailureHud | true；无Head绑定不创建默认视图 |
| Session.Mode | Directory；另有ControlServer、External |
| Directory模式入口 | SDK内部固定`http://localhost:8868/v1/vr/service/config`，不提供地址配置项 |
| ControlServerUrl | ControlServer模式必填HTTP(S)地址 |
| BillingBaseUrl | 可选独立账务HTTP(S)地址 |
| RequestTimeoutSeconds | 30，有限正数 |
| AllocationIntervalSeconds | 1，有限正数 |
| MaximumReconnectAttempts | 5，非负 |
| NetworkLimits.MaximumMessageBytes | 4MiB，原始业务帧及解压数据上限 |
| NetworkLimits.MaximumQueuedMessages | 单方向256条 |
| NetworkLimits.MaximumQueuedBytes | 单方向8MiB，不小于单消息上限 |
| NetworkLimits.MessagesPerFrame | 128个派发通知 |
| NetworkLimits.MaximumPendingRequests | 256个待应答请求 |

### SpatialOptions / DeviceOptions

| 配置 | 默认值 |
|---|---|
| Spatial.Mode / Calibration | XrCalibration / HorizontalYaw |
| HeightTolerance / TiltToleranceDegrees | 0.001m / 0.5° |
| ShowObstacles / ShowFloor | true / true |
| Spatial.MaximumObstacles | 4096；超限全量快照原子拒绝 |
| ObstacleDistance / VisibilityHysteresis | 1.5m / 0.1m |
| TileSource、BoxPrefab、EllipsePrefab | null时使用SDK默认资源/地块库 |
| ReportingEnabled / AvatarsEnabled | true / true |
| Devices.MaximumDevices | 1024；已存在设备仍可更新，移除后释放容量 |
| AvatarPrefab | null时使用SDK默认角色资源 |
| SameRoomAvatars | false |
| SameRoomDistance / OtherRoomDistance | 2m / 2m |
| SamplingIntervalSeconds | 0.2，有限正数 |
| PositionThreshold / YawThresholdDegrees | 0.001m / 0.5° |

容量均须为正数。默认容量是资源保护值，不是PICO帧率或实际房间容量承诺，需按部署规模压测调整。原生与External共用数据容量；拒绝不消耗External revision。

距离、容差、阈值要求有限非负。ComputeOnly/Disabled 时必须显式关闭空间表现和设备采样/虚影选项。

### MediaOptions

| 配置 | 默认值/约束 |
|---|---|
| AllowBlackFrameStart | false；仅显式允许新的控制会话无源黑帧启动，不影响运行中清源 |
| Version | 1；更新必须大于当前版本 |
| OutputLongSide | 1280；范围146–4096，计算输出时按16:9偶数对齐 |
| FramesPerSecond | 30；正数，实际受设备能力限制 |
| VideoBandwidthKbps / VideoMinBitrateKbps | 3500 / 1500；max>0，0≤min≤max |
| ForceH264 | true |
| IceGatheringTimeoutSeconds / HttpTimeoutSeconds | 3 / 30；正整数 |
| HandshakeTimeoutSeconds | 150 |
| DisconnectedGraceSeconds | 5 |
| StatisticsIntervalSeconds | 1 |
| RemoteCleanupSeconds | 2；仅远端清理预算，不强制释放native |
| AudioDrainWarningSeconds | 5；超过后诊断并继续等安全释放 |
| AudioBufferSamples | 32768；正数，仅Create时可设 |
| RetryDelaysMilliseconds | `[2000,5000,10000]`；元素非负，空数组禁用媒体恢复，不能为null |

浮点超时/间隔须有限正数。协商参数按媒体尝试捕获；更新不缩短当前握手期限。变更输出尺寸须无会话并已清源。

## 8. 诊断与错误码

`DiagnosticsService.Error` 为 `Action<SdkError>`，`LastError` 为可空值；错误带 Domain、Result、RuntimeId、RequestId、SessionEpoch。订阅诊断不会改变操作结果，也不是“错误已处理”的成功开关。

### 实例日志

创建前设置`SdkOptions.Logging`（`SdkLogOptions`），客户端捕获独立配置；创建后改原配置不影响实例。

| 配置 | 默认值/语义 |
|---|---|
| MinimumLevel | `Info`；等级依次Info、Warning、Error，None关闭普通日志输出 |
| WriteToUnityConsole | true；与自定义sink独立，可同时输出 |
| Sink | `Action<SdkLogEntry>`，默认null；借用，不由SDK Dispose |
| MaximumQueuedEntries | 256，正数；仅自定义sink队列 |
| EntriesPerFrame | 64，正数；每次主线程调度点预算，关闭时排空已有条目 |

`SdkLogEntry`为只读值：Level、Domain、Message、TimestampUtc、RuntimeId、RequestId、SessionEpoch、Code；无请求/会话归属时相应Id为0。普通状态日志Code=None。捕获的异常记Error，Message保留脱敏后的异常类型/文本/堆栈，不公开原始Exception引用。

- sink在后续主线程调度点执行，不在领域状态提交栈中同步执行；应快速返回，不执行阻塞IO。音频线程不调用sink。
- 关闭保留当前实例日志归属并排空，之后不再调用该sink。sink异常会在Unity Console明确报错；sink回调重入生成的日志只走Unity兜底，避免递归派发。
- 队列满时丢弃新日志，`Diagnostics.DroppedLogCount`累计数量；下次排空在Unity Console报告丢弃数量，即使普通Unity输出关闭也保留此兜底告警。
- 每条消息最多处理4096字符再附截断标记；HTTP(S)/WS(S)地址整体隐藏，常见token/password/secret/api-key及Basic/Bearer授权字段脱敏。业务仍不得把无标记密钥、原始响应或个人数据拼入异常；此过滤不是任意业务文本的隐私识别器。
- 包含实例各域、协议/媒体适配、地面视图、绑定Rig、SDK接管期内置画面源的日志；不劫持全局Unity日志。独立使用的画面源、Editor、Samples/宿主和第三方native日志不归此sink。
- 日志过滤不影响`LastError`、`Diagnostics.Error`、命令完成或错误结果；纯告警/订阅异常日志不会重复发布业务错误事件。

| SdkErrorCode | 数值 |
|---|---:|
| None | 0 |
| InvalidArgument / InvalidState / Busy | 1001 / 1002 / 1003 |
| Disposed / Unavailable / Cancelled | 1004 / 1005 / 1006 |
| CapacityExceeded | 1007 |
| NotConnected / Timeout / TransportFailed | 2001 / 2002 / 2003 |
| InvalidResponse / RemoteRejected | 2004 / 2005 |
| InvalidBinding / InvalidCalibration / MissingRegionTile | 3001 / 3002 / 3003 |
| StaleGeneration / ExternalCalibrationWrite / InvalidGeometry | 3004 / 3005 / 3006 |
| MediaFailed / AudioDrainStalled | 4001 / 4002 |
| InternalError | 9001 |

这些是新C#结果码，不复用3.x数字语义，也不等同于服务端错误码。日志宏 `MVXRSDK_LOG_DISABLED` 只影响残留内部辅助日志类，**不能关闭新实例域的全部诊断**。

Protobuf生成类型仍可能在程序集里为public，因为生成文件不手改；它们不属于业务API，不应被宿主业务引用。网络、池、调度、native会话和被动资源组件均由SDK内部管理。

## 9. 当前预览的明确限制

- 实例日志sink已提供，Diagnostics.Error仍只表示结构化错误事件；引擎、第三方native和宿主自行输出的日志不由SDK全局拦截。
- 空间双向查询已提供TryRoomToWorld/TryWorldToRoom；只有当前有效、未被外部改写的应用参考帧才可转换，StaleHold不当作有效参考帧。
- WebSocket通过实例自有TCP/TLS流完成异步升级，帧处理使用框架Managed WebSocket；TLS1.2保持证书链/主机名校验，不做证书放行，升级头另有16KiB固定保护。WSS与Android/IL2CPP仍需实际环境回归。
