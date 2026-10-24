using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>实例推流入口；正式开停来自当前Session通知，不把导演受理当作媒体连接成功。</summary>
    public sealed class StreamingService
    {
        private readonly SdkRuntimeContext context;
        private readonly DirectorService director;
        private readonly SessionService session;
        private MediaOptions configuration;
        private readonly GameAudioBuffer audio;
        private readonly StreamSourceSlot sources;
        private readonly StreamSessionController controller;
        private readonly IStreamOutput output;
        private bool stopping;
        private AudioCaptureHandle audioCapture;
        internal bool IsReleased { get; private set; }
        public StreamingState State => controller.State;
        public StreamingStatistics Statistics => controller.Statistics;
        public IStreamSource CurrentSource => sources.Current;
        /// <summary>接受新的控制开流命令后触发；自动恢复不重复发布，也不消费新的导演相机候选。</summary>
        public event Action Starting;
        public event Action<StreamingState> Changed;
        public event Action<StreamingStatistics> StatisticsUpdated;

        internal StreamingService(SdkRuntimeContext context, SessionService session, DirectorService director,
            Func<IMediaPeer> createPeer = null, IWhipExchange exchange = null, IStreamOutput output = null)
        {
            this.context = context; this.session = session; this.director = director;
            configuration = context.Options.Media;
            int rate = AudioSettings.outputSampleRate;
            audio = new GameAudioBuffer(rate > 0 ? rate : 48000, configuration.AudioBufferSamples);
            this.output = output ?? new StreamOutputSurface(configuration.OutputLongSide);
            sources = new StreamSourceSlot(this.output.Ensure, this.output.Clear, Report, context.Log);
            controller = new StreamSessionController(session, sources, this.output.Ensure,
                createPeer ?? (() => new WebRtcMediaPeer(context.Host, configuration, audio)),
                exchange ?? new UnityWhipExchange(context.Host), configuration, () => Time.realtimeSinceStartupAsDouble, Report);
            controller.Starting += OnStarting;
            controller.Changed += OnChanged;
            controller.StatisticsUpdated += OnStatistics;
        }

        /// <summary>返回独立配置副本；修改此副本不会改变当前实例或已协商会话。</summary>
        public MediaOptions GetConfiguration() => configuration.Capture();

        /// <summary>
        /// 整体应用更高版本配置。FPS与重试策略即时生效，协商参数用于下次会话；
        /// 改输出尺寸须先停流并ClearSource。音频缓冲容量在实例创建时固定。
        /// </summary>
        public SdkResult ApplyConfiguration(MediaOptions value)
        {
            context.EnsureAlive();
            if (stopping) return SdkResult.Fail(SdkErrorCode.Disposed, "推流域正在关闭");
            MediaOptions captured;
            try { captured = value?.Capture() ?? throw new ArgumentException("媒体配置不能为空"); }
            catch (ArgumentException error) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, error.Message); }
            if (captured.Version <= configuration.Version)
                return SdkResult.Fail(SdkErrorCode.StaleGeneration, "媒体配置版本必须递增");
            if (captured.AudioBufferSamples != configuration.AudioBufferSamples)
                return SdkResult.Fail(SdkErrorCode.InvalidArgument, "音频缓冲容量仅能在创建客户端时设置");
            bool resize = captured.OutputLongSide != configuration.OutputLongSide;
            if (resize && (!controller.CanReconfigureOutput || sources.Current != null))
                return SdkResult.Fail(SdkErrorCode.Busy, "变更输出尺寸前须停止媒体并清除画面源");
            if (resize)
            {
                try { output.Reconfigure(captured.OutputLongSide); }
                catch (Exception error) { return SdkResult.Fail(SdkErrorCode.MediaFailed, "重建输出规格失败：" + error.Message); }
            }
            configuration = captured;
            controller.UpdateOptions(captured);
            return SdkResult.Ok;
        }

        /// <summary>接受才接管源；Busy/Invalid不Dispose传入对象。相机与外部RT本体始终借用。</summary>
        public SourceSetResult TrySetSource(IStreamSource source)
        {
            context.EnsureAlive();
            if (stopping) return SourceSetResult.Invalid;
            return sources.TrySet(source, false);
        }

        /// <summary>释放SDK已接管的画面源，保留输出RT并清黑；不等同于停流。</summary>
        public SdkResult ClearSource()
        {
            context.EnsureAlive();
            if (stopping) return SdkResult.Fail(SdkErrorCode.Disposed, "推流域正在关闭");
            return sources.Clear();
        }

        /// <summary>输入mono/stereo游戏PCM，允许音频线程调用；不持有数组，不在音频线程输出日志。</summary>
        public SdkResult PushGameAudioPcm(float[] pcm, int sampleRate, int channels) => audio.Push(pcm, sampleRate, channels);

        /// <summary>在指定Listener上采集游戏音；替换旧绑定，不采麦克风，不修改扬声器PCM或Listener状态。</summary>
        public AudioCaptureHandle CaptureGameAudio(AudioListener listener)
        {
            context.EnsureAlive();
            if (stopping) throw new ObjectDisposedException(nameof(StreamingService));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (audioCapture != null && audioCapture.Listener == listener && audioCapture.Tap != null) return audioCapture;
            // Destroy到帧末才生效，已Detach的旧组件不能阻止本帧的新租约；仍只允许一个活跃采集者。
            foreach (var existing in listener.GetComponents<GameAudioInputTap>())
                if (existing.IsBound) throw new InvalidOperationException("Listener已由其他采集绑定占用");
            var tap = listener.gameObject.AddComponent<GameAudioInputTap>();
            tap.Initialize(audio);
            audioCapture?.Dispose();
            audioCapture = new AudioCaptureHandle(listener, tap, ReleaseCapture, context.EnsureMainThread);
            return audioCapture;
        }

        private void ReleaseCapture(AudioCaptureHandle handle)
        {
            if (ReferenceEquals(audioCapture, handle)) audioCapture = null;
            if (handle.Tap == null) return;
            handle.Tap.Detach();
            if (Application.isPlaying) UnityEngine.Object.Destroy(handle.Tap);
            else UnityEngine.Object.DestroyImmediate(handle.Tap);
        }

        private void OnStarting()
        {
            Notify(Starting);
            if (stopping || controller.State != StreamingState.Starting) return;
            var camera = director.TakeCamera();
            if (camera == null || sources.Current != null) return;
            var source = new CameraStreamSource(camera);
            if (sources.TrySet(source, true) != SourceSetResult.Accepted) source.Dispose();
        }

        private void OnChanged(StreamingState state) => Notify(Changed, state);
        private void OnStatistics(StreamingStatistics value) => Notify(StatisticsUpdated, value);
        private void Report(SdkResult result) => context.Diagnostics.Report("Streaming", result, 0, session.Current.Epoch);

        internal void Tick()
        {
            if (audioCapture != null)
            {
                if (audioCapture.Tap == null) audioCapture.Dispose();
                else
                {
                    var error = audioCapture.Tap.TakeError();
                    if (!error.Success) Report(error);
                }
            }
            controller.Tick();
            if (stopping) TryRelease();
        }

        internal void TickSource() { if (!stopping) sources.Tick(Time.unscaledTimeAsDouble, configuration.FramesPerSecond); }

        internal void BeginShutdown()
        {
            if (stopping) return;
            stopping = true;
            audioCapture?.Dispose();
            audio.Dispose();
            controller.Dispose();
            sources.Dispose();
            TryRelease();
        }

        private void TryRelease()
        {
            if (IsReleased || !controller.IsReleased || !sources.IsReleased) return;
            output.Dispose();
            controller.Starting -= OnStarting; controller.Changed -= OnChanged; controller.StatisticsUpdated -= OnStatistics;
            Starting = null; Changed = null; StatisticsUpdated = null;
            IsReleased = true;
        }

        private void Notify(Action callbacks)
        {
            if (callbacks == null) return;
            foreach (Action callback in callbacks.GetInvocationList())
            { try { callback(); } catch (Exception error) { context.Log.Exception("Streaming", error); } }
        }

        private void Notify<T>(Action<T> callbacks, T value)
        {
            if (callbacks == null) return;
            foreach (Action<T> callback in callbacks.GetInvocationList())
            { try { callback(value); } catch (Exception error) { context.Log.Exception("Streaming", error); } }
        }
    }
}
