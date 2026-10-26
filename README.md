# MyVerse XR SDK (`com.myverse.xrsdk`)

多人 XR 协作 SDK：房间接入、XR 逆校准、障碍物与设备虚影、WHIP/WebRTC 推流、导演仲裁及远端录制命令。

**4.x 是不兼容3.x C# API的主版本。本包版本为 `4.0.0-preview.2`（预览版），适合按下述流程接入验证；可安装版本以独立包仓实际Tag为准，不将预览版本视为正式稳定版。**

## 环境与安装

- Unity 2022.3 LTS，URP 必需。
- 清单依赖：URP 14.0.11、Unity WebRTC 3.0.0-pre.8、Newtonsoft.Json 3.0.2。
- 目标设备 PICO 4/4U（Android）；SDK Runtime 不直接引用 PICO 程序集。
- 可将完整包放到工程 `Packages/com.myverse.xrsdk/`，或通过 Package Manager 的本地 `package.json` 导入。
- Git 分发仓库为 `https://github.com/lucas-hai/myverseXRSDK.git`；接入时锁定**实际已发布**的 tag/commit，不要根据本地版本号猜测远端tag。

## 新入口

```csharp
var client = MVXRSDK.Create(new SdkOptions { DeviceId = deviceId });
var binding = client.Space.BindRig(
    new SpatialRigBinding(motionRoot, calibrationRoot, trackingRoot, head));
if (!binding.Success)
{
    Debug.LogError(binding.Result.Message);
    client.Dispose();
    return;
}
// Start成功只表示本地运行，联网与校准分别查询所属域状态。
var result = client.Start();
if (!result.Success) { Debug.LogError(result.Message); client.Dispose(); }
```

通过客户端的 Session、Space、Devices、Admission、Billing、Director、Streaming、Recording、Diagnostics 域访问能力。关闭使用 `Shutdown` / `Dispose`；同一进程只允许一个尚未安全释放的客户端。

SDK只写专用 `CalibrationRoot` 和自有表现对象，不移动地图、业务移动根或头手追踪节点。场景需明确配置 `MotionRoot → CalibrationRoot → TrackingRoot → Head`；缺失时不会猜测角色或自动移动地图。

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
| `03_Business` | 准入通知、中控导演与远端录制命令；无扣费入口 |
| `04_Streaming` | 手填完整WHIP URL直推；独立相机/普通RT、游戏音 |
| `05_CrossSceneA` / `06_CrossSceneB` | 同一客户端跨场景，旧Rig解绑、新Rig绑定 |
| `07_ControlledStreaming` | 连接中控、申请导演机位，等待NotifyLive控制开停 |

开发工作区直接打开 `Samples/EditorDemos/Scenes/`，不要在同一工程重复导入。发布脚本只在分发副本中转换为 `Samples~/EditorDemos`，分发包再通过 Package Manager → Samples → **Editor Integration Demos** 导入。除04外均在Inspector配置正式Directory或测试ControlServer，Play自动连接；业务动作通过组件菜单显式触发。04保留手动直推，具体步骤见示例说明。

宿主 `Assets/Tests/` 的自动回归、Acceptance专项场景，以及PICO/动捕测试资产均不属于SDK分发示例。

推流仅服务局域网，使用独立相机普通RT或外部Tex2D，不抓取主相机eye buffer。发布前须完成独立工程导入、后端联调及PICO/Vulkan真机验收。
