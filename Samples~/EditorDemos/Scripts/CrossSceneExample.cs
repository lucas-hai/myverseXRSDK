using System;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>同一客户端跨场景重绑：两个场景都可独立创建；只在这对场景之间保留所有者。</summary>
    public sealed class CrossSceneExample : MonoBehaviour
    {
        public Transform motionRoot, calibrationRoot, trackingRoot, head;
        public UnityEngine.Object nextScene;
        public string deviceId = "editor-cross-scene", controlServerUrl = "";
        [Header("中控接入（进入Play自动连接）")]
        public ControlConnectionMode connectionMode = ControlConnectionMode.ControlServer;
        public SdkClient Client => CrossSceneOwner.Instance != null ? CrossSceneOwner.Instance.Client : null;
        private CrossSceneOwner owner;
        private SpatialBindingHandle binding;
        private bool switching;

        private void Awake()
        {
            owner = CrossSceneOwner.Instance;
            if (owner == null) return;
            connectionMode = owner.Mode; deviceId = owner.DeviceId; controlServerUrl = owner.ControlServerUrl;
            if (Client != null && Client.State == SdkClientState.Running) DemoConsole.Result(this, "新场景绑定", BindScene());
        }
        private void Start()
        {
            if (Client == null) ConnectFromInspector();
        }
        [ContextMenu("SDK/连接中控")]
        private void ConnectFromInspector()
        {
            if (!DemoConsole.RequirePlay(this)) return;
            DemoConsole.Result(this, "启动", StartSession());
        }

        /// <summary>连接真实中控；已有客户端只重绑本场景，区域数据完全来自会话。</summary>
        public SdkResult StartSession()
        {
            if (Client != null)
                return Client.State == SdkClientState.Running ? BindScene() : SdkResult.Fail(SdkErrorCode.Busy, "等待安全释放");
            if (CrossSceneOwner.Instance != null) return SdkResult.Fail(SdkErrorCode.Busy, "等待持久对象销毁");
            if (motionRoot == null || calibrationRoot == null || trackingRoot == null || head == null)
                return SdkResult.Fail(SdkErrorCode.InvalidBinding, "缺少普通四节点");
            SdkClient created;
            try
            {
                created = MVXRSDK.Create(new SdkOptions
                {
                    DeviceId = deviceId, Session = new SessionOptions { Mode = connectionMode == ControlConnectionMode.Directory ? SessionConnectionMode.Directory : SessionConnectionMode.ControlServer, ControlServerUrl = controlServerUrl },
                    Spatial = new SpatialOptions(),
                    ShowNetworkFailureHud = false
                });
            }
            catch (ArgumentException) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, "检查设备标识与完整中控HTTP(S)地址"); }
            catch (InvalidOperationException) { return SdkResult.Fail(SdkErrorCode.Busy, "其他客户端尚未释放"); }
            owner = new GameObject("CrossScene.ClientOwner").AddComponent<CrossSceneOwner>();
            owner.Mode = connectionMode; owner.DeviceId = deviceId; owner.ControlServerUrl = controlServerUrl;
            owner.Client = created;
            var result = BindScene();
            if (!result.Success) { Shutdown(); return result; }
            result = Client.Start();
            if (!result.Success) { Shutdown(); return result; }
            return SdkResult.Ok;
        }
        private SdkResult BindScene()
        {
            var result = Client.Space.BindRig(new SpatialRigBinding(motionRoot, calibrationRoot, trackingRoot, head));
            if (!result.Success) return result.Result;
            binding = result.Handle;
            return SdkResult.Ok;
        }
        /// <summary>只切换Inspector指定的Editor场景；先解绑旧节点，下一场景Awake绑定新节点。</summary>
        [ContextMenu("场景/保留客户端并切换场景")]
        public void SwitchScene()
        {
            if (switching || Client == null || Client.State != SdkClientState.Running) return;
#if UNITY_EDITOR
            string path = AssetDatabase.GetAssetPath(nextScene);
            if (!(nextScene is SceneAsset) || string.IsNullOrEmpty(path)) { Debug.Log("请指定下一场景SceneAsset", this); return; }
            binding?.Dispose(); binding = null; switching = true;
            EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Debug.Log("此Demo仅演示Editor场景；Player需要显式配置场景加载方式。", this);
#endif
        }
        /// <summary>解绑本场景节点并异步释放持久客户端。</summary>
        [ContextMenu("SDK/关闭连接")]
        public void Shutdown()
        {
            binding?.Dispose(); binding = null;
            owner?.Shutdown();
        }
        private void OnDestroy()
        {
            binding?.Dispose();
        }
    }
}
