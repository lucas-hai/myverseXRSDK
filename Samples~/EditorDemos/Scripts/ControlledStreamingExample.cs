using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>中控推流接入：连接入房→导演请求→中控NotifyLive开停；不提供手填WHIP或外部伪造通知。</summary>
    public sealed class ControlledStreamingExample : MonoBehaviour
    {
        public ControlConnectionMode connectionMode = ControlConnectionMode.ControlServer;
        [Tooltip("设备标识须属于中控设备管理；先由网页控制台开房并拉入")]
        public string deviceId = "editor-controlled-stream", controlServerUrl = "";
        [Tooltip("仅 Source 为 unity 时需要：申请本地机位使用的独立相机。其他来源不附带相机。")]
        public Camera sourceCamera;
        public AudioListener gameListener;
        public DemoAudioSignal audioSignal;
        [Header("中控机位申请参数")]
        public DirectorRequestSettings streamingRequest = new DirectorRequestSettings();
        [Header("推流配置（可选）"), Tooltip("无需创建配置资产。留空使用 SDK 默认值：1280×720、30fps、H.264；指定资产则在创建客户端时读取整份配置。")]
        public StreamConfigAsset streamConfigAsset;
        public SdkClient Client { get; private set; }
        public OperationHandle DirectorOperation { get; private set; }
        private AudioCaptureHandle audioCapture;
        private readonly DemoGui ui = new DemoGui();
        private bool Running => Client != null && Client.State == SdkClientState.Running;

        private void Start()
        {
            if (Client == null) ConnectFromInspector();
        }
        [ContextMenu("SDK/连接中控")]
        private void ConnectFromInspector()
        {
            if (!DemoConsole.RequirePlay(this)) return;
            DemoConsole.Result(this, "启动", Connect());
        }

        /// <summary>显式创建中控客户端；返回成功只表示本地启动，不表示入房、导演受理或开流。</summary>
        public SdkResult Connect()
        {
            if (Client != null) return SdkResult.Fail(SdkErrorCode.Busy, "请先等待旧客户端安全关闭");
            var options = new SdkOptions
            {
                EnableStreaming = true, // 本示例包含Unity独立机位接入；不等于立即开流。
                DeviceId = deviceId,
                Session = new SessionOptions
                {
                    Mode = connectionMode == ControlConnectionMode.Directory ? SessionConnectionMode.Directory : SessionConnectionMode.ControlServer,
                    ControlServerUrl = controlServerUrl
                },
                Spatial = new SpatialOptions { Mode = SpatialMode.Disabled, ShowObstacles = false, ShowFloor = false },
                Devices = new DeviceOptions { ReportingEnabled = false, AvatarsEnabled = false },
                ShowNetworkFailureHud = false,
            };
            try
            {
                if (streamConfigAsset != null) options.Media = streamConfigAsset.ToMediaOptions();
            }
            catch (ArgumentException error) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, error.Message); }
            try { Client = MVXRSDK.Create(options); }
            catch (ArgumentException) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, "检查设备标识和完整中控HTTP(S)地址"); }
            catch (InvalidOperationException) { return SdkResult.Fail(SdkErrorCode.Busy, "其他客户端尚未释放"); }
            Client.Streaming.Changed += OnStreamingChanged;
            var result = Client.Start();
            if (!result.Success) Shutdown();
            return result;
        }

        /// <summary>按Inspector配置向中控申请机位；受理后仍等待NotifyLive，不能在回调中自行开流。</summary>
        public OperationHandle RequestDirector()
        {
            if (!Running || !Client.Session.Current.IsJoined) { Debug.Log("等待真实中控分配房间并登录为Joined。", this); return null; }
            if (DirectorOperation != null && !DirectorOperation.IsCompleted) return DirectorOperation;
            if (streamingRequest == null) { Debug.LogWarning("请先配置中控机位申请参数。", this); return null; }
            if (streamingRequest.source == "unity" && sourceCamera == null)
            { Debug.LogWarning("Source 为 unity 时需要配置独立推流相机。", this); return null; }
            var owner = Client;
            // AutoCamera由SDK在中控通知开流时接管，不能把请求受理当成相机开始渲染。
            DirectorOperation = Client.Director.Request(streamingRequest.CreateRequest(sourceCamera), result =>
            {
                if (this == null || !ReferenceEquals(Client, owner)) return;
                Debug.Log($"导演请求#{result.RequestId}：{result.Result.Code}；远端未知={result.RemoteOutcomeUnknown}。\n受理不等于中选/开流，等待中控NotifyLive。", this);
            });
            return DirectorOperation;
        }

        /// <summary>仅取消本地等待和未消费的相机候选，不撤回中控命令，也不停止已开始的推流。</summary>
        [ContextMenu("推流/取消本地导演等待及相机候选")]
        public void CancelDirectorWait()
        {
            if (!Running) return;
            DirectorOperation?.Cancel(); Client.Director.CancelPendingCamera();
            Debug.Log("已撤销本地等待/相机候选；中控已受理的动作不因此撤回。", this);
        }
        /// <summary>开关Listener游戏音采集及测试信号，不采麦克风。</summary>
        [ContextMenu("音频/开关游戏音采集和测试音")]
        public void ToggleGameAudio()
        {
            if (!Running || gameListener == null || audioSignal == null) return;
            if (audioCapture != null) { StopAudio(); return; }
            audioCapture = Client.Streaming.CaptureGameAudio(gameListener);
            audioSignal.StartTone(null);
        }
        private void StopAudio() { audioSignal?.StopTone(); audioCapture?.Dispose(); audioCapture = null; }
        private void OnStreamingChanged(StreamingState state) => Debug.Log("实际媒体状态：" + state + "；开停由中控通知驱动。", this);

        /// <summary>退出中控会话并异步停止媒体；不提前销毁仍由SDK借用的相机输出。</summary>
        [ContextMenu("SDK/关闭连接")]
        public void Shutdown()
        {
            var closing = Client;
            if (closing == null || closing.State == SdkClientState.Stopping) return;
            StopAudio(); closing.Streaming.Changed -= OnStreamingChanged;
            closing.Shutdown(result =>
            {
                if (ReferenceEquals(Client, closing)) { Client = null; DirectorOperation = null; }
                DemoConsole.Result(this, "断开并安全停流", result);
            });
        }
        [ContextMenu("推流/按配置申请中控机位")]
        private void RequestFromMenu() => RequestDirector();
        private void OnGUI() => ui.DrawPreview(sourceCamera != null ? sourceCamera.targetTexture : null);
        private void OnDestroy()
        {
            StopAudio();
            if (Client != null) { Client.Streaming.Changed -= OnStreamingChanged; Client.Dispose(); }
            ui.Dispose();
        }
    }
}
