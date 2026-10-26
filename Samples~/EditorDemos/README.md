# Editor SDK 接入示例

这组场景展示**业务如何接入 SDK**，不是内部验收框架。只用普通 Camera、Transform；统一由Inspector配置、组件右键菜单执行动作，Game视图仅保留推流只读预览；不依赖 PICO、XR Interaction Toolkit、Input System、测试程序集或旧 Acceptance 工具。

本组是当前分发的接入示例入口；旧联合Demo不再列为Package Manager Sample，宿主的Acceptance、PICO和动捕资产继续保留作专项验证，不需要复制到业务工程。

## 打开方式

- 开发工作区：直接打开 `Packages/com.myverse.xrsdk/Samples/EditorDemos/Scenes/` 中的场景。
- UPM分发包：Package Manager → MyVerse XR SDK → Samples → **Editor Integration Demos** → Import，打开导入目录的 `Scenes/`。
- 开发工作区不要重复导入同一份Sample，避免重复程序集；分发副本使用 `Samples~`。
- Unity 2022.3 LTS、SDK所需URP；不需要头显或XR Loader。
- 在Inspector选中`接入入口_场景名`配置参数。Play后通过组件右上角菜单/右键菜单执行`SDK/`、`业务/`、`空间/`、`推流/`、`音频/`、`场景/`中的操作，结果看Console。
- 除04外，Play自动执行连接；04只有显式执行“开始直推”才连接WHIP。所有示例均不自动扣费、请求导演或录制。

## 连接真实中控

除04外，所有示例仅有两种`Connection Mode`：

- **Directory（正式）**：固定POST `http://localhost:8868/v1/vr/service/config`，不填写IP、端口或目录URL。
- **ControlServer（测试，示例默认）**：填写完整`Control Server Url`，是中控HTTP(S)地址，不是房间WS或WHIP地址。

`Device Id`必须属于中控设备管理；先协调网页控制台开房并拉入目标设备。进入Play即启动会话并接受中控通知，没有`allowRemoteControl`复选框。地址非法会明确报错，不自动使用离线数据。运行中修改Inspector不会重配现有客户端，需关闭并等待释放后从`SDK/连接中控`重新创建。

`Running`只表示本地启动，`Session.Current.IsJoined`才表示入房；空间还须等待`Space.Status.IsApplied`。02、05/06的区域、障碍物与远端设备来自真实中控，地块使用SDK标准资源，不注入固定Region。接入参数与生产凭据不要提交进版本库。

## 场景与操作入口

| 场景 | 功能 | 启动与主要操作 |
|---|---|---|
| `01_Connection` | 最小客户端生命周期、会话状态 | Inspector配置 → Play自动连接；`SDK/关闭连接` |
| `02_Spatial` | 校准、区域地面、柱子、设备虚影 | 保留现有Inspector自动连接与`空间/`菜单 |
| `03_Business` | 准入、中控导演、远端录制（无扣费入口） | 自动连接；业务命令由`业务/`菜单显式执行 |
| `04_Streaming` | 不经过中控的直接WHIP | Inspector填写`Whip Url` → `推流/开始直推`；也可只准备本地画面 |
| `05_CrossSceneA` / `06_CrossSceneB` | 持久客户端跨场景重绑Rig | 任一场景都可自动连接；`场景/保留客户端并切换场景` |
| `07_ControlledStreaming` | 真实中控控制推流 | 自动连接 → Joined → `推流/向中控申请独立相机镜头` → 等NotifyLive |

03（包含Unity机位）、04、07在创建客户端时显式`EnableStreaming=true`；01、02、05/06不接入媒体，收到开流通知也不会启动WebRTC。默认首次无源拒绝，不会为没有接入推流的业务分配黑帧会话。

### 02：Inspector配置与空间观察点

1. 退出Play，选中`接入入口_02_Spatial`，填写中控设备管理中的`Device Id`并保留节点引用。`Connection Mode`选正式`Directory`时无需填写任何IP或地址，SDK固定访问`http://localhost:8868/v1/vr/service/config`；仅选测试`ControlServer`时填写完整`Control Server Url`。正式模式需要本机配置服务，不是仅启动中控网页即可。
2. 进入Play自动执行`Create → BindRig → Start`；不需要`allowRemoteControl`或运行时连接按钮。地址非法/节点缺失会在Console明确报错，不自动改用示例数据。
3. 在Console观察会话`Joined`与SDK空间状态；本地“启动成功”不代表已入房或校准成功。
4. Play期间，通过组件右上角菜单/右键菜单的`空间/`执行头部前移、业务根移动、同房间虚影切换、房间坐标查询和参考帧撤销；`SDK/关闭连接`可主动关闭，退出Play也会释放。
5. 在Play期间修改地址不重配现有客户端；关闭后用`SDK/连接中控`重试，或退出Play修改后重启。当前不自动保存用户场景。


- 场景中 `G_Map_不随SDK移动` 是业务地图；橙色柱子是固定地图参照，不是 SDK 障碍物。
- 普通四节点：`MotionRoot_B → CalibrationRoot_C_SDK独占 → TrackingRoot → Head/LeftHand/RightHand`；缩放只在显示方块子节点上改变，不改绑定祖先。
- 区域中心和旋转全部由中控下发，结合标准地块计算校准；示例没有可编辑的Region Center/Region Yaw或固定数据兜底，地图不由SDK移动。
- 移动业务根 B 后，世界位姿改变，但本机 R 坐标保持不变。
- 撤销参考帧只调用 SDK `InvalidateTrackingOrigin`，不是硬件 recenter；由SDK重新获取中控参考数据。
- 真实设备由房间协议同步；不同房默认显示，同房默认关闭。`空间/切换同房间虚影`仅切换同房显示，不关闭不同房虚影，两类可同时显示。虚影缺失不代表现实无人，失效边界见SDK接入指南。

### 03：业务结果不能混淆

- Play只创建中控会话和订阅准入；`业务/按配置申请中控机位`、`业务/远端录制10秒`均须显式执行。推流示例不提供账务地址或扣费入口。
- Inspector的“中控推流请求参数”下展开`Streaming Request`，配置对应后台请求的五个字段；`RequestDirector()`每次显式调用时读取配置，不由菜单覆盖Source或固定时长。

| Inspector字段 | 中控字段 | 含义与默认值 |
|---|---|---|
| Source | Source | 默认`unity`，使用本地独立相机；留空或`mr`申请原直播；其他值按中控约定原样发送。 |
| Lenses | Lenses | 申请镜头数，默认1，必须≥1；不代表本地创建多台相机。 |
| Duration Seconds | DurationSec | 申请时长（秒），默认10，必须>0；实际切换和开停由中控决定。 |
| Record | Record | 是否请求中控录制，默认关闭；不是本地录屏。 |
| File Name | FileName | 默认空，仅Record开启时有意义；协议约定不含扩展名，例如`business-record`。SDK原样透传，不增删后缀；不是本地保存路径。 |

- `Director Camera`单独放在“本地独立机位”组，保留原场景引用；仅Source为`unity`时附为本地候选，不发送到中控。空Source不夹带相机，避免被SDK自动补成`unity`。
- 修改请求参数仅影响下次申请，不改写已发请求或自动重发；收到NotifyLive才按已有候选接源。WHIP地址由中控提供，编码/分辨率属于本地媒体配置，不混入这五个字段。
- `Recording Target Id`属于另一条远端录制命令，为空时使用设备标识，不控制上述Record请求。
- `Admission.StartRequested` 仅通知业务，Demo 不自动开局或扣费。SDK的`Billing`仍是独立可选能力，不属于推流接入步骤。
- Director 受理不等于中选或媒体连接；Recording 受理不等于录制文件已经生成。
- `业务/取消本地等待`只取消本地等待，不能撤回远端已执行动作；`RemoteOutcomeUnknown` 不可视为未发生，不自动重试。
- 接入地址和标识由使用者填写，不附带生产凭据；不要把秘密写入场景资产。

### 04：直接填写地址推流

1. 在Inspector填完整HTTP(S) `Whip Url`，不是RTMP/RTSP地址；保留独立`Source Camera`引用。
2. Play后执行`推流/开始直推`，自动创建External媒体客户端、接源并发起WHIP；不经过中控。
3. `推流/仅准备本地画面`只准备本地源，不联网开流；`接独立相机源`、`接普通RT源`展示两种接源方式。Game视图预览不代表远端已经收到画面。
4. `音频/启用Listener游戏音`或`音频/启用主动PCM`演示互斥输入；`音频/查询PCM输入错误`读取输入错误码。
5. `推流/停止直推`保留手动源；`推流/清源（不中断媒体）`只清源；`SDK/关闭连接`安全释放整个客户端。

### 07：中控流程推流

1. Inspector配置正式/测试入口和中控设备身份，Play自动连接；无需填写WHIP地址。
2. 等到Joined后执行`推流/向中控申请独立相机镜头`，通过`Director.Request(AutoCamera)`申请。
3. 受理≠选中≠开流；中控NotifyLive提供地址并控制开停，SDK在开流时接上自动相机。
4. `推流/取消本地导演等待及相机候选`不撤回远端命令、不停止已开始的流。正常停流由中控控制，本地退出使用`SDK/关闭连接`。
5. `音频/开关游戏音采集和测试音`演示Listener输入。首次无有效相机候选或手动源时拒绝本次开流并诊断，补源后需新的通知；不会从主相机或屏幕兜底取图。确需首次黑帧时才显式配置AllowBlackFrameStart。已开流后清源/丢源仍输出黑帧。

### 05/06：跨场景保持同一客户端

从任一场景进入Play均可建立真实中控会话。切换菜单先释放旧Rig绑定，新场景Awake绑定新节点，沿用同一RuntimeId、设备身份与会话参数，不重新初始化或注入区域。关闭整个客户端才结束该初始化周期；安全释放完成前拒绝创建下一实例。切到不含跨场景示例的场景时，持久所有者会关闭客户端。

### 源与音频所有权

- `CameraStreamSource` 使用**独立推流相机**直渲 SDK 输出 RT；不抓观察相机、屏幕或 XR eye buffer。
- `OwnedCameraTextureSource` 展示自定义 `IStreamSource`：示例拥有普通 RT，内部用 `RenderTextureStreamSource`；相机本体借用。
- `TrySetSource` 只有 Accepted 才接管适配器；拒绝时调用者 Dispose 候选。自有 RT 随已接管源的 Dispose 释放，不在 Shutdown 刚返回时提前 Release。
- 本地预览借用 SDK 输出，不拥有它；ClearSource 清源并清黑，不自动停止已有媒体会话。
- 游戏音有 Listener 采集与主动 PCM 两种互斥输入；测试信号为低音量 440Hz，不读取麦克风。需要 Editor 游戏音未静音和有效音频设备；输入 API 的成功不代表远端听到声音。
- 手动源跨普通停流保留；结束示例请关闭客户端。真实媒体的异步安全关闭可能等待 native，不能把 Stopping 当作已经释放。

## 读代码的顺序

每个入口独立展示 `SdkOptions → MVXRSDK.Create → 订阅/BindRig → Start → 域服务调用 → Shutdown`。复制接入逻辑时可替换显示层，不需要带上其他 Demo。

- `DemoGui`仅负责推流普通RT的只读预览，`DemoConsole`只输出菜单结果；均不拥有客户端。
- `DemoAudioSignal`和`OwnedCameraTextureSource`保留为有效的音频/普通RT接入示例；固定地块源与离线空间注入已删除。
- `CrossSceneOwner` 只服务跨场景这对示例，持久保存客户端并在离开该类场景时关闭；不是所有 Demo 的共同宿主。
- 跨场景通过 Inspector SceneAsset 引用加载，支持 Sample 导入后的路径变化；当前加载示例仅针对 Editor，不配置 Player Build Settings。

## 场景再生成

随包已有场景可直接打开。确需新副本时，用 `MyVerse → Editor接入Demo → 生成全新场景副本`，输出 `Assets/EditorDemoGenerated`。已有输出则拒绝覆盖；请先自行保存/关闭未保存场景，工具不会代存用户场景，也不修改 Build Settings。

## 验证边界

本组示例面向 Editor 业务接入。自动回归使用无 PICO/XRI 的独立工程；覆盖Inspector自动连接、显式命令、无离线注入、跨场景同客户端和源资源寿命；受控空间数据只在测试程序集的协议接收边界输入，不进入生产示例；不因此宣称真实中控、扣费、录屏文件、WHIP 接收端、Android 或头显已验收。
