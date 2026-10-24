using System;
using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>单次WebRTC会话的native所有者；音频供给退出前保持本地资源与调度宿主存活。</summary>
    internal sealed partial class WebRtcMediaPeer : IMediaPeer
    {
        private readonly SdkRuntimeHost host;
        private readonly MediaOptions options;
        private readonly GameAudioBuffer audio;
        private RTCPeerConnection peer;
        private VideoStreamTrack videoTrack;
        private AudioStreamTrack audioTrack;
        private MediaAudioPump pump;
        private MediaAudioFeeder feeder;
        private Coroutine update;
        private Coroutine negotiate;
        private Coroutine answer;
        private Coroutine statistics;
        private bool started;
        private bool closing;
        private bool failureSent;
        private bool releaseErrorReported;
        private double disconnectedAt = -1;
        private long epoch;
        public bool IsReleased { get; private set; }
        public event Action<string> LocalOffer;
        public event Action Connected;
        public event Action<SdkResult, bool> Failed;
        public event Action<StreamingStatistics> Statistics;

        internal WebRtcMediaPeer(SdkRuntimeHost host, MediaOptions options, GameAudioBuffer audio)
        { this.host = host; this.options = options; this.audio = audio; }

        public void Start(RenderTexture output, long sessionEpoch)
        {
            if (closing || started) throw new InvalidOperationException("媒体会话不可复用");
            started = true;
            epoch = sessionEpoch;
            if (output == null)
            { Fail("推流输出纹理不能为空", false, SdkErrorCode.InvalidArgument); return; }
            if (output.width < 145 || output.height < 49 || output.width > 4096 || output.height > 4096)
            { Fail($"推流输出尺寸{output.width}x{output.height}无效，编码器允许145–4096 x 49–4096", false, SdkErrorCode.InvalidArgument); return; }
            try
            {
                var configuration = new RTCConfiguration { iceServers = Array.Empty<RTCIceServer>() };
                peer = new RTCPeerConnection(ref configuration);
                peer.OnConnectionStateChange = OnConnection;
                peer.OnIceConnectionChange = OnIceConnection;
                videoTrack = new VideoStreamTrack(output);
                peer.AddTrack(videoTrack);
                audioTrack = new AudioStreamTrack();
                peer.AddTrack(audioTrack);
                ApplyCodecPreference();
                audio.Clear();
                AudioSettings.GetDSPBufferSize(out int frames, out _);
                pump = new MediaAudioPump(audio, SendAudio, frames);
                var root = new GameObject("MVXRSDK.Audio") { hideFlags = HideFlags.DontSave };
                root.transform.SetParent(host.transform, false);
                feeder = root.AddComponent<MediaAudioFeeder>();
                feeder.Initialize(pump, audio.SampleRate);
                update = host.Run(WebRTC.Update());
                negotiate = host.Run(Negotiate());
            }
            catch (Exception error) { Fail("初始化WebRTC失败：" + error.Message, false); }
        }

        private void SendAudio(float[] samples, int frames, int sampleRate)
            => audioTrack.SetData(new ReadOnlySpan<float>(samples, 0, frames), 1, sampleRate);

        private void OnConnection(RTCPeerConnectionState state)
        {
            if (closing) return;
            if (state == RTCPeerConnectionState.Connected)
            {
                disconnectedAt = -1;
                if (statistics == null) statistics = host.Run(CollectStatistics());
                Connected?.Invoke();
            }
            else if (state == RTCPeerConnectionState.Disconnected)
            {
                if (disconnectedAt < 0) disconnectedAt = Time.realtimeSinceStartupAsDouble;
            }
            else if (state == RTCPeerConnectionState.Failed) Fail("WebRTC连接失败", true);
        }

        private void OnIceConnection(RTCIceConnectionState state)
        {
            if (!closing && state == RTCIceConnectionState.Failed && disconnectedAt < 0)
                Fail("ICE连接失败", true);
        }

        private void Fail(string message, bool retryable, SdkErrorCode code = SdkErrorCode.MediaFailed)
        {
            if (closing || failureSent) return;
            failureSent = true;
            Failed?.Invoke(SdkResult.Fail(code, message), retryable);
        }

        public void Tick()
        {
            var audioError = pump?.TakeError();
            if (audioError != null)
            {
                if (closing) host.Log.Write(SdkLogLevel.Error, "Streaming", "[MVXRSDK][Audio] 音频关闭期间发生错误：" + audioError);
                else Fail("音频供给失败：" + audioError.Message, false);
            }
            if (closing) { ReleaseWhenSafe(); return; }
            if (disconnectedAt >= 0 && Time.realtimeSinceStartupAsDouble - disconnectedAt >= options.DisconnectedGraceSeconds)
                Fail("WebRTC断流自愈窗口超时", true);
        }

        public void Dispose()
        {
            if (closing) return;
            closing = true;
            pump?.Close();
            if (feeder != null) feeder.StopFeeding();
            host.Cancel(update); host.Cancel(negotiate); host.Cancel(answer); host.Cancel(statistics);
            update = negotiate = answer = statistics = null;
            if (peer != null) { peer.OnConnectionStateChange = null; peer.OnIceConnectionChange = null; }
            LocalOffer = null; Connected = null; Failed = null; Statistics = null;
            ReleaseWhenSafe();
        }

        private void ReleaseWhenSafe()
        {
            if (IsReleased || (pump != null && !pump.IsDrained)) return;
            if (pendingStats != null)
            {
                if (!pendingStats.IsDone) return;
                pendingStats.Value?.Dispose();
                pendingStats = null;
            }
            try
            {
                if (feeder != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(feeder.gameObject);
                    else UnityEngine.Object.DestroyImmediate(feeder.gameObject);
                    feeder = null;
                }
                peer?.Close();
                videoTrack?.Dispose(); videoTrack = null;
                audioTrack?.Dispose(); audioTrack = null;
                peer?.Dispose(); peer = null;
                pump = null;
                IsReleased = true;
            }
            catch (Exception error)
            {
                if (releaseErrorReported) return;
                releaseErrorReported = true;
                host.Log.Write(SdkLogLevel.Error, "Streaming", "[MVXRSDK][Media] native资源尚未安全释放，保持Stopping：" + error);
            }
        }
    }
}
