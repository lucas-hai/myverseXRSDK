using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>直连WHIP：Inspector填写URL并从组件菜单开始即可准备客户端和独立相机，不经过中控；仍支持仅本地预览。</summary>
    public sealed class StreamingExample : MonoBehaviour
    {
        public Camera sourceCamera;
        public AudioListener gameListener;
        public DemoAudioSignal audioSignal;
        public string whipUrl = "";
        public SdkClient Client { get; private set; }
        public RenderTexture Preview => preview;
        private ExternalSessionWriter writer;
        private long liveRevision;
        private AudioCaptureHandle audioCapture;
        private RenderTexture preview;
        private readonly DemoGui ui = new DemoGui();

        /// <summary>仅创建External媒体客户端；不自行开流或伪造空间数据。</summary>
        public SdkResult StartDemo()
        {
            if (Client != null) return SdkResult.Fail(SdkErrorCode.Busy, "等待客户端释放");
            var options = new SdkOptions
            {
                DeviceId = "editor-streaming", Session = new SessionOptions { Mode = SessionConnectionMode.External },
                Spatial = new SpatialOptions { Mode = SpatialMode.Disabled, ShowObstacles = false, ShowFloor = false },
                Devices = new DeviceOptions { ReportingEnabled = false, AvatarsEnabled = false },
                Media = new MediaOptions { OutputLongSide = 640 }, ShowNetworkFailureHud = false,
            };
            try { Client = MVXRSDK.Create(options); }
            catch (InvalidOperationException) { return SdkResult.Fail(SdkErrorCode.Busy, "其他客户端尚未安全释放"); }
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
        public SdkResult StartWhip()
        {
            if (!Uri.TryCreate(whipUrl, UriKind.Absolute, out var address) || address.Scheme != "http" && address.Scheme != "https")
                return SdkResult.Fail(SdkErrorCode.InvalidArgument, "需要完整HTTP(S) WHIP地址");
            var prepared = PrepareDirectStream();
            if (!prepared.Success) return prepared;
            return writer.NotifyLive(++liveRevision, whipUrl, true);
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
            DisableAudio(); writer = null;
            // 自有RT在已接受的源适配器中释放；不能在这里抢先Release仍被SDK借用的纹理。
            closing.Shutdown(result => { if (ReferenceEquals(Client, closing)) Client = null; preview = null; DemoConsole.Result(this, "关闭", result); });
        }
        [ContextMenu("推流/开始直推")]
        private void StartFromMenu()
        {
            if (!DemoConsole.RequirePlay(this)) return;
            DemoConsole.Result(this, "开流", StartWhip());
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
        private void OnDestroy() { DisableAudio(); Client?.Dispose(); preview = null; ui.Dispose(); }
    }
}
