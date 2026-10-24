using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>仅跨场景示例使用的持久所有者；不负责创建SDK或装配Rig，其他Demo不依赖它。</summary>
    public sealed class CrossSceneOwner : MonoBehaviour
    {
        public static CrossSceneOwner Instance { get; private set; }
        public SdkClient Client { get; internal set; }
        internal ControlConnectionMode Mode { get; set; }
        internal string DeviceId { get; set; }
        internal string ControlServerUrl { get; set; }
        private void Awake()
        {
            if (Instance != null) throw new InvalidOperationException("跨场景示例已有持久所有者");
            Instance = this; DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<CrossSceneExample>(true) != null) return;
            Shutdown();
        }
        /// <summary>异步关闭；释放完成前保留唯一所有者，不允许新建客户端。</summary>
        public void Shutdown()
        {
            var closing = Client;
            if (closing == null) { Destroy(gameObject); return; }
            if (closing.State == SdkClientState.Stopping) return;
            closing.Shutdown(result =>
            {
                Client = null;
                if (this != null) Destroy(gameObject);
            });
        }
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (Instance == this) Instance = null;
            Client?.Dispose();
        }
    }
}
