using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>直连WHIP：提交WHIP地址后准备客户端和独立相机，不经过中控；仍支持仅本地预览。</summary>
    public sealed class StreamingExample : MonoBehaviour
    {
        public Camera sourceCamera;
        public AudioListener gameListener;
        public DemoAudioSignal audioSignal;
        [Tooltip("场景只填写无凭据示例地址；含鉴权的现场地址请进入 Play 后使用临时输入框。")]
        public string whipUrl = "";
        [Header("推流配置（可选）"), Tooltip("无需创建配置资产。留空使用 SDK 默认值：1280×720、30fps、H.264；指定资产则在创建客户端时读取整份配置。")]
        public StreamConfigAsset streamConfigAsset;
        public SdkClient Client { get; private set; }
        public RenderTexture Preview => preview;
        private ExternalSessionWriter writer;
        private long liveRevision;
        private AudioCaptureHandle audioCapture;
        private RenderTexture preview;
        private readonly DemoGui ui = new DemoGui();

        /// <summary>仅创建External媒体客户端；不自行开流或伪造空间数据。</summary>
        public SdkResult StartDemo() => StartDemo(null);

        /// <summary>使用显式媒体参数创建 External 客户端；未传时读取 Inspector 资产，再留空则使用 SDK 默认值。不启动 WHIP。</summary>
        public SdkResult StartDemo(MediaOptions media)
        {
            if (Client != null) return SdkResult.Fail(SdkErrorCode.Busy, "等待客户端释放");
            var options = new SdkOptions
            {
                EnableStreaming = true, // 显式接入媒体能力，开流仍由菜单触发。
                DeviceId = "editor-streaming", Session = new SessionOptions { Mode = SessionConnectionMode.External },
                Spatial = new SpatialOptions { Mode = SpatialMode.Disabled, ShowObstacles = false, ShowFloor = false },
                Devices = new DeviceOptions { ReportingEnabled = false, AvatarsEnabled = false },
                ShowNetworkFailureHud = false,
            };
            try
            {
                if (media != null) options.Media = media;
                else if (streamConfigAsset != null) options.Media = streamConfigAsset.ToMediaOptions();
                Client = MVXRSDK.Create(options);
            }
            catch (ArgumentException error) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, error.Message); }
            catch (InvalidOperationException) { return SdkResult.Fail(SdkErrorCode.Busy, "其他客户端尚未安全释放"); }
            Client.Streaming.Changed += OnStreamingChanged;
            var result = Client.Start();
            if (!result.Success) { Shutdown(); return result; }
            writer = Client.Session.OpenExternalSession("editor-media"); liveRevision = 0;
            return SdkResult.Ok;
        }
        /// <summary>提交独立相机源；只有Accepted由SDK接管适配器，否则本方法释放候选。</summary>
        public SourceSetResult UseCamera()
        {
            if (!Running || sourceCamera == null) return SourceSetResult.Invalid;
            var source = new CameraStreamSource(sourceCamera);
            var result = Client.Streaming.TrySetSource(source);
            if (result == SourceSetResult.Accepted) preview = sourceCamera.targetTexture;
            else source.Dispose();
            return result;
        }
        /// <summary>提交示例自有普通RT源；被接受后的RT随SDK释放源时回收。</summary>
        public SourceSetResult UseExternalTexture()
        {
            if (!Running || sourceCamera == null) return SourceSetResult.Invalid;
            var source = new OwnedCameraTextureSource(sourceCamera);
            var result = Client.Streaming.TrySetSource(source);
            if (result == SourceSetResult.Accepted) preview = source.Output;
            else source.Dispose();
            return result;
        }
        /// <summary>清除手动源，不主动结束已有媒体会话。</summary>
        public SdkResult ClearSource() => Running ? Client.Streaming.ClearSource() : SdkResult.Fail(SdkErrorCode.InvalidState, "客户端未运行");
        private bool Running => Client != null && Client.State == SdkClientState.Running;
        /// <summary>仅准备External客户端与画面源，不发起WHIP；已有手动源保持不变。</summary>
        public SdkResult PrepareDirectStream()
        {
            if (Client != null && !Running) return SdkResult.Fail(SdkErrorCode.Busy, "等待客户端安全释放");
            if (Running && Client.Streaming.CurrentSource != null) return SdkResult.Ok;
            if (sourceCamera == null) return SdkResult.Fail(SdkErrorCode.InvalidBinding, "需要独立推流相机");
            if (Client == null)
            {
                var start = StartDemo();
                if (!start.Success) return start;
            }
            var accepted = UseCamera();
            return accepted == SourceSetResult.Accepted ? SdkResult.Ok : SdkResult.Fail(
                accepted == SourceSetResult.Busy ? SdkErrorCode.Busy : SdkErrorCode.MediaFailed, "独立相机接源失败");
        }
        /// <summary>显式向填写的WHIP地址开流；自动完成本地准备，不需要先连接中控。</summary>
        public SdkResult StartWhip() => RequestWhip(whipUrl);

        private SdkResult RequestWhip(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.Scheme != "http" && address.Scheme != "https")
                return SdkResult.Fail(SdkErrorCode.InvalidArgument, "需要完整HTTP(S) WHIP地址");
            var prepared = PrepareDirectStream();
            if (!prepared.Success) return prepared;
            return writer.NotifyLive(++liveRevision, url, true);
        }
        /// <summary>停止直推控制会话；手动源保持至清源或关闭。</summary>
        public SdkResult StopWhip() => Running ? writer.NotifyLive(++liveRevision, "", false) : SdkResult.Fail(SdkErrorCode.InvalidState, "客户端未运行");
        /// <summary>改用Listener采集游戏音并播放测试信号；不采麦克风。</summary>
        [ContextMenu("音频/启用Listener游戏音")]
        public void EnableListenerAudio()
        {
            if (!Running || audioSignal == null || gameListener == null) return;
            DisableAudio();
            audioCapture = Client.Streaming.CaptureGameAudio(gameListener);
            audioSignal.StartTone(null);
        }
        /// <summary>改用测试信号主动提交PCM，先撤销Listener采集。</summary>
        [ContextMenu("音频/启用主动PCM")]
        public void EnableDirectPcm()
        {
            if (!Running || audioSignal == null) return;
            DisableAudio(); audioSignal.StartTone(Client.Streaming);
        }
        /// <summary>停止测试信号并释放游戏音采集绑定。</summary>
        [ContextMenu("音频/关闭游戏音")]
        public void DisableAudio() { audioSignal?.StopTone(); audioCapture?.Dispose(); audioCapture = null; }
        /// <summary>异步关闭媒体客户端；不提前释放SDK仍借用的输出纹理。</summary>
        [ContextMenu("SDK/关闭连接")]
        public void Shutdown()
        {
            var closing = Client;
            if (closing == null || closing.State == SdkClientState.Stopping) return;
            closing.Streaming.Changed -= OnStreamingChanged;
            DisableAudio(); writer = null;
            // 自有RT在已接受的源适配器中释放；不能在这里抢先Release仍被SDK借用的纹理。
            closing.Shutdown(result => { if (ReferenceEquals(Client, closing)) Client = null; preview = null; DemoConsole.Result(this, "关闭", result); });
        }
        [ContextMenu("推流/开始直推")]
        private void StartFromMenu() => SubmitWhip(whipUrl);

        /// <summary>在 Play 模式提交指定地址，报告请求结果，不将地址写回场景字段。</summary>
        public SdkResult SubmitWhip(string url)
        {
            if (!DemoConsole.RequirePlay(this)) return SdkResult.Fail(SdkErrorCode.InvalidState, "请先进入 Play");
            var result = RequestWhip(url);
            if (result.Success) Debug.Log("已提交开流请求", this);
            else DemoConsole.Result(this, "提交开流请求", result);
            return result;
        }

        private void OnStreamingChanged(StreamingState state)
        {
            Debug.Log(state == StreamingState.Streaming ? "本机媒体已连接；请到接收端确认画面" : "本机媒体状态：" + state, this);
        }

        [ContextMenu("推流/查询本机发送统计")]
        private void ReportStatistics()
        {
            var stats = Running ? Client.Streaming.Statistics : default;
            Debug.Log(stats.IsValid ? $"本机发送：视频 {stats.VideoBitrateKbps:F0}、音频 {stats.AudioBitrateKbps:F0} kbps；不代表接收端出画" : "暂无有效发送统计", this);
        }
        [ContextMenu("推流/仅准备本地画面")]
        private void PrepareFromMenu()
        {
            if (!DemoConsole.RequirePlay(this)) return;
            DemoConsole.Result(this, "本地准备", PrepareDirectStream());
        }
        [ContextMenu("推流/接独立相机源")]
        private void CameraFromMenu() => Debug.Log("相机接源：" + UseCamera(), this);
        [ContextMenu("推流/接普通RT源")]
        private void TextureFromMenu() => Debug.Log("RT接源：" + UseExternalTexture(), this);
        [ContextMenu("推流/清源（不中断媒体）")]
        private void ClearFromMenu() => DemoConsole.Result(this, "清源", ClearSource());
        [ContextMenu("推流/停止直推")]
        private void StopFromMenu() => DemoConsole.Result(this, "停流", StopWhip());
        [ContextMenu("音频/查询PCM输入错误")]
        private void QueryAudioError() => Debug.Log("PCM输入错误：" + (audioSignal != null ? audioSignal.LastPcmError.ToString() : "未配置音频信号"), this);
        private void OnGUI() => ui.DrawPreview(preview);
        private void OnDestroy()
        {
            if (Client != null) Client.Streaming.Changed -= OnStreamingChanged;
            DisableAudio(); Client?.Dispose(); preview = null; ui.Dispose();
        }
    }
}
