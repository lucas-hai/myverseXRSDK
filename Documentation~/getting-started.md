# 第一次接入：从安装到看到 Joined

**目标：在 Unity Editor 里确认“这台设备已经登录中控分配的房间”。** 第一次只做这一件事：不绑头手、不配地块、不启用摄像机、不扣费。这样即使没有画面、没有区域数据，也不会干扰你判断网络是否接通。

## 1. 准备三样东西

| 准备什么 | 去哪里拿 | 第一次要注意什么 |
|---|---|---|
| Unity 工程和 SDK | 使用 URP 的 Unity 工程；从团队指定的包目录或包仓安装 | SDK 最低 Unity 2022.3；本仓库的开发工程是 Unity 6.3。不要为了打开开发工程而要求所有接入方升级 Unity。 |
| `Device Id`（设备标识） | 向中控管理员确认，在网页控制台的设备管理中核对 | 是中控认识的那台设备的标识，不是随便起的玩家名，也不是开发者平台的 AppId。场景里的 `editor-connection` 只是示例值。 |
| 中控地址和房间分配 | 联调时请管理员提供可从 Editor 电脑访问的中控 **HTTP(S) 基地址**，并在控制台开房、将同一设备拉入 | 没分房时客户端可以一直等，这是正常行为。此次不需要房间 WebSocket 地址、WHIP 地址或账务地址。 |

## 2. 安装并找到示例（二选一）

- **使用分发包的业务工程：** 打开 Unity 的 **Package Manager**（Window 菜单下，部分版本在 Package Management 子菜单）。从 `+` 菜单选 **Install/Add package from git URL**，填包仓 URL 并锁定已发布的标签。本版本的安装地址为 `https://github.com/lucas-hai/myverseXRSDK.git#v4.0.0-preview.3`，以包仓出现该标签为准；发布前可用 **Install/Add package from tarball** 导入本地 `.tgz`。也可以选择 **Install/Add package from disk**，指向分发包的 `package.json`。等编译完成，在 **MyVerse XR SDK → Samples → Editor Integration Demos → Import** 导入示例。到 Project 的 `Assets/Samples/` 下，打开导入副本的 `Scenes/01_Connection.unity`。
- **已经打开本仓库的开发工程：** 包已嵌入，不再安装也不再 Import。直接打开 `Packages/com.myverse.xrsdk/Samples/EditorDemos/Scenes/01_Connection.unity`。开发目录是 `Samples`，分发包里是 `Samples~`；前者已参与编译，再拷贝一份可能会出现同名程序集。

**安装检查：** Package Manager 中能看到 `MyVerse XR SDK`，Console 没有红色编译错误，打开 01 后能选到带 `ConnectionExample` 的对象。做这个 Editor 示例不需要头显或 PICO 插件。若弹出“SDK 验证”面板，那是**开发者平台地块工具**的凭据验证，不是把设备拉进中控房间的登录窗口。

> 本文描述当前源码。固定标签安装的是该次发布的快照，不会随本地文档自动更新。例如 `[未发布]` 变更中将 03 的机位申请移到了 07；较旧示例出现不同菜单时，先看所装包的版本和随包 README。

## 3. 不写代码，连一次中控

1. **退出 Play 后填写。** 在 Hierarchy 选中挂有 `ConnectionExample` 的场景对象（不是 Project 中的 `.cs` 文件）。在 Inspector 填好 **Device Id**。
2. **选「测试：中控地址直连」**（`ControlServer`），在 **Control Server Url** 填管理员给的地址，格式如 `http://<中控主机>:<端口>`。尖括号是占位符，要替换；不要把 `/v1/open/api/device`、`ws://…` 或推流路径接在后面。SDK 会自己拼轮询接口。
3. **点击 ▶ Play，打开 Console。** 01 会自动启动，不用再点连接。看到本地“启动成功”后，继续看 `会话状态：…`：`Allocating` 表示正在等待或查询分配；`Joined` 表示房间登录成功。中间状态可能很快，不要求肉眼看到每一条。
4. **验证收尾。** Play 中点 `ConnectionExample` **组件标题右上角 ⋮ → SDK → 关闭连接**。需要再连时，等安全关闭完成再选 **SDK → 连接中控**。组件菜单不在 Game 画面里。直接退出 Play 也会释放示例持有的客户端。

**正式部署和测试直连的区别，是第一步怎么找到中控。** 选择 `Directory`（正式：本机目录发现）时不填 IP，SDK 访问运行设备自身的 `http://localhost:8868/v1/vr/service/config`。在 Editor 中，“本机”是开发电脑；装到头显后才是头显。设备拿到中控地址后，轮询分配、连房间和登录的步骤与 `ControlServer` 相同。不要为了测试直连在开发电脑上伪造目录服务。

## 4. 换成你自己的场景

不必把 01 的地面和相机一起复制进项目。下面是一个**只负责测试直连**的完整组件。将它保存为 `RoomConnection.cs`，挂到自己场景里的一个空对象，Inspector 填同一组设备标识和中控地址后进入 Play。**不要和 01 的入口同时运行**：一个进程只能有一个尚未安全释放的客户端。业务脚本若放在自己的 asmdef 中，引用 `MVXRSDK.Runtime` 和 `MVXRSDK.Contracts`。

```csharp
using System;
using MyVerseXRSDK;
using UnityEngine;

public sealed class RoomConnection : MonoBehaviour
{
    [SerializeField] private string deviceId;
    [SerializeField] private string controlServerUrl;
    private SdkClient client;

    private void Start()
    {
        var options = new SdkOptions
        {
            DeviceId = deviceId,
            Session = new SessionOptions
            {
                Mode = SessionConnectionMode.ControlServer,
                ControlServerUrl = controlServerUrl
            },
            // 只验证入房，不要求 Rig、地块或设备表现。推流默认关闭。
            Spatial = new SpatialOptions { Mode = SpatialMode.Disabled, ShowObstacles = false, ShowFloor = false },
            Devices = new DeviceOptions { ReportingEnabled = false, AvatarsEnabled = false }
        };
        try { client = MVXRSDK.Create(options); }
        catch (ArgumentException) { Debug.LogError("请检查设备标识和中控 HTTP(S) 地址。"); return; }
        catch (InvalidOperationException) { Debug.LogError("已有客户端尚未安全释放。"); return; }

        client.Session.Changed += OnSessionChanged; // 先订阅，避免错过启动期间的状态变化。
        var result = client.Start();
        if (!result.Success)
        {
            Debug.LogError($"本地启动失败：{result.Code}");
            Close();
        }
    }

    private void OnSessionChanged(SessionSnapshot session) => Debug.Log($"会话状态：{session.State}");

    private void OnDestroy() => Close();

    private void Close()
    {
        if (client == null) return;
        client.Session.Changed -= OnSessionChanged;
        client.Dispose(); // 发起非阻塞关闭，不表示此刻就能创建下一实例。
        client = null;
    }
}
```

在自己的业务代码中，用 `client.Session.Current.IsJoined` 判断是否已入房，而不是用一秒延时猜测。切换为正式发现时改 `Session.Mode`，不要把 `controlServerUrl` 改为 `localhost` 来模拟正式模式。若业务需要跨场景保持这个客户端，参考 05/06 的持久对象，不要照搬这个单场景组件的销毁时机。

## 5. 下一步只选你需要的功能

| 你的目标 | 下一站 | 怎样才算验证到这一步 |
|---|---|---|
| 房间里的人和柱子要与真实场地对齐 | `02_Spatial`；[空间接线与坐标](index.md#space-rig) | 已入房，`Space.Status.IsApplied` 为 `true`；再用已知现场参照物核对位置和朝向。 |
| 接中控开局通知，或命令某台设备录制 | `03_Business`；[业务命令](index.md#business-commands) | 通知回调或录制受理只是客户端证据；实际开局由业务决定，录制文件要在接收命令的一侧核对。 |
| 已有推流地址，先试画面 | `04_Streaming`；[两种推流方式](index.md#streaming-routes) | 手动开始后在**接收端**看到独立相机的画面；不需先跑 01、03 或 07。 |
| 由中控选中某个机位再播 | `07_ControlledStreaming`；[两种推流方式](index.md#streaming-routes) | 入房 → 手动申请 → 中控下发开流通知 → 媒体连接 → 接收端有画面。 |
| 切换关卡不重建客户端 | `05_CrossSceneA`、`06_CrossSceneB` | 换场景后仍是原来的 `RuntimeId`，空间绑定改为新场景的节点。 |

如果卡在中途，按你**最后看到的状态**查 [常见问题与排查顺序](index.md#troubleshooting)，不必从安装重来。
