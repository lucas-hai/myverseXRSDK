# MyVerse XR SDK (`com.myverse.xrsdk`)

MyVerse XR SDK 帮 Unity 应用把多台设备接入同一个中控房间，把头显位置对齐到真实场地，并按需把独立相机的画面推给接收端。只需要连房间的项目，不必同时接空间、推流或扣费。

**第一次用，从 [第一次接入：从安装到看到 Joined](Documentation~/getting-started.md) 开始。** 里面有 Package Manager 安装步骤、无需写代码的 `01_Connection` 操作，以及可以挂到自己场景上的完整 C# 组件。若目的是先看看视频能否推通，不必先连房间，直接看下表的 04。

**4.x 是不兼容3.x C# API的主版本。本包版本为 `4.0.0-preview.3`（预览版），适合按下述流程接入验证；可安装版本以独立包仓实际Tag为准，不将预览版本视为正式稳定版。**

## 环境与安装

- SDK最低Unity版本仍为2022.3 LTS，URP必需；宿主开发工程已升级Unity 6.3 LTS `6000.3.15f1`。
- 清单依赖保留兼容基线：URP 14.0.11、Unity WebRTC 3.0.0-pre.8、Newtonsoft.Json 3.0.2。
- Unity 6宿主可显式使用URP `17.3.0`及Unity WebRTC `3.0.0`；稳定版WebRTC要求Unity 6000.0+，不要给2022.3工程安装该版本。本轮已执行两代Editor的SDK自动测试，未替代Android/PICO/Vulkan真机验收。
- 目标设备 PICO 4/4U（Android）；SDK Runtime 不直接引用 PICO 程序集。
- 可将完整包放到工程 `Packages/com.myverse.xrsdk/`，或通过 Package Manager 的本地 `package.json` 导入。
- Git 分发仓库为 `https://github.com/lucas-hai/myverseXRSDK.git`；接入时锁定**实际已发布**的 tag/commit，不要根据本地版本号猜测远端tag。

## 接入时先分清三件事

| 问题 | 应该看什么 | 不代表什么 |
|---|---|---|
| SDK 启动了吗？ | `client.Start()` 成功，客户端进入 `Running` | 本地服务能工作，还不等于房间登录成功。 |
| 中控认出这台设备了吗？ | `client.Session.Current.IsJoined` 为 `true` | 已进房，还不等于头显已和场地对齐。 |
| 能用房间坐标了吗？ | `client.Space.Status.IsApplied` 为 `true` | 说明有效校准已写入绑定节点；真实场地是否摆放正确还要现场核对。 |

这三项不是每个业务的必经步骤：只连房间看到第二项即可；04 直推走独立的媒体路径，不以中控入房为前提。

通过客户端的 Session、Space、Devices、Admission、Billing、Director、Streaming、Recording、Diagnostics 域访问能力。关闭使用 `Shutdown` / `Dispose`；同一进程只允许一个尚未安全释放的客户端。

SDK只写专用 `CalibrationRoot` 和自有表现对象，不移动地图、业务移动根或头手追踪节点。要做空间校准时，场景需明确配置 `MotionRoot → CalibrationRoot → TrackingRoot → Head`；只验证入房不需要这套层级。缺失时 SDK 不会猜测节点角色或自动移动地图。

推流是可选能力，`SdkOptions.EnableStreaming`默认false；需要媒体的客户端须在Create前显式开启。仅设置中控地址、应用StreamConfigAsset或绑定Rig不会开启能力。启用后首次无有效源默认拒绝；`Media.AllowBlackFrameStart=true`才允许首次黑帧开流，已开流后清源仍保持黑帧。从`4.0.0-preview.1`升级时，已有推流宿主需补上显式启用并准备有效源；本版本变更见CHANGELOG，旧Tag内容不变。

## 文档与示例

- [接入与3.x迁移指南](Documentation~/index.md)
- [API参考与配置默认值](Documentation~/api-reference.md)
- [Editor验证与地块工具](Documentation~/editor-tools-guide.md)
- [变更记录](CHANGELOG.md)

接入示例统一从EditorDemos目录内的 `README.md` 开始；旧联合 `Samples/Demo` 及专用程序集已移除。

| 场景 | 主要用途 |
|---|---|
| `01_Connection` | 创建客户端、真实会话状态与安全关闭 |
| `02_Spatial` | Inspector配置后Play自动连接；校准、区域地面、柱子、同房/跨房虚影 |
| `03_Business` | 准入通知与独立远端录制；不启用推流，无扣费入口 |
| `04_Streaming` | 手填完整WHIP URL直推；独立相机/普通RT、游戏音 |
| `05_CrossSceneA` / `06_CrossSceneB` | 同一客户端跨场景，旧Rig解绑、新Rig绑定 |
| `07_ControlledStreaming` | 连接中控，按配置申请机位，等待NotifyLive控制开停 |

开发工作区直接打开 `Samples/EditorDemos/Scenes/`，不要在同一工程重复导入。发布脚本只在分发副本中转换为 `Samples~/EditorDemos`，分发包再通过 Package Manager → Samples → **Editor Integration Demos** 导入。除04外均在Inspector配置正式Directory或测试ControlServer，Play自动连接；业务动作通过组件菜单显式触发。04保留手动直推，具体步骤见示例说明。

分发包不包含宿主工程的测试或真机验证场景。**当前源码只有 04、07 两个推流示例**：04 自己填推流地址，07 等中控给地址；03 的远端录制不启动本机推流。若所装标签的 03 仍有机位菜单，那是旧示例布局，请以该标签的文档为准。遇到问题可从[按现象排查](Documentation~/index.md#troubleshooting)开始。

推流仅服务局域网，使用独立相机普通RT或外部Tex2D，不抓取主相机eye buffer。发布前须完成独立工程导入、后端联调及PICO/Vulkan真机验收。
