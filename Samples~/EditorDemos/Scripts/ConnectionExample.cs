using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>真实中控接入方式；不包含外部数据注入。</summary>
    public enum ControlConnectionMode
    {
        [InspectorName("正式：本机目录发现")] Directory,
        [InspectorName("测试：中控地址直连")] ControlServer
    }

    /// <summary>基础接入：配置→Create→订阅→Start→Shutdown；Inspector配置，Play自动连接真实中控。</summary>
    public sealed class ConnectionExample : MonoBehaviour
    {
        public string deviceId = "editor-connection";
        [Header("中控接入（进入Play自动连接）")]
        public ControlConnectionMode connectionMode = ControlConnectionMode.ControlServer;
        public string controlServerUrl = "";
        public SdkClient Client { get; private set; }

        private void Start()
        {
            if (Client == null) ConnectFromInspector();
        }
        [ContextMenu("SDK/连接中控")]
        private void ConnectFromInspector()
        {
            if (!DemoConsole.RequirePlay(this)) return;
            DemoConsole.Result(this, "启动", StartClient());
        }

        /// <summary>创建并启动真实中控会话；成功仅表示本地Running。</summary>
        public SdkResult StartClient()
        {
            if (Client != null) return SdkResult.Fail(SdkErrorCode.Busy, "等待旧客户端关闭完成");
            var options = new SdkOptions
            {
                DeviceId = deviceId,
                Session = new SessionOptions { Mode = connectionMode == ControlConnectionMode.Directory ? SessionConnectionMode.Directory : SessionConnectionMode.ControlServer, ControlServerUrl = controlServerUrl },
                Spatial = new SpatialOptions { Mode = SpatialMode.Disabled, ShowObstacles = false, ShowFloor = false },
                Devices = new DeviceOptions { ReportingEnabled = false, AvatarsEnabled = false },
                ShowNetworkFailureHud = false,
            };
            try { Client = MVXRSDK.Create(options); }
            catch (ArgumentException) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, "检查设备标识、模式与中控地址"); }
            catch (InvalidOperationException) { return SdkResult.Fail(SdkErrorCode.Busy, "已有其他客户端尚未释放"); }
            Client.Session.Changed += OnSessionChanged;
            var result = Client.Start();
            if (!result.Success) Shutdown();
            return result;
        }
        private void OnSessionChanged(SessionSnapshot snapshot) => Debug.Log("会话状态：" + snapshot.State + "；本地Running不代表已登录", this);

        /// <summary>异步关闭；安全释放完成前拒绝创建新实例。</summary>
        [ContextMenu("SDK/关闭连接")]
        public void Shutdown()
        {
            var closing = Client;
            if (closing == null || closing.State == SdkClientState.Stopping) return;
            closing.Session.Changed -= OnSessionChanged;
            closing.Shutdown(result =>
            {
                if (ReferenceEquals(Client, closing)) Client = null;
                DemoConsole.Result(this, "安全关闭", result);
            });
        }
        private void OnDestroy()
        {
            if (Client != null) { Client.Session.Changed -= OnSessionChanged; Client.Dispose(); }
        }
    }
}
