using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 完整媒体配置资产，保留视频字段的序列化身份。
    /// 实例路径通过ToMediaOptions或ApplyTo应用完整快照，网络策略也明确包含在资产中。
    /// </summary>
    [CreateAssetMenu(menuName = "MyVerse XR SDK/Stream Config", fileName = "StreamConfig")]
    public sealed class StreamConfigAsset : ScriptableObject
    {
        [Header("视频编码")]
        [Tooltip("推流 InternalRT 长边像素（v3：固定 16:9，与画面源无关）。\n" +
                 "  默认 1280 → InternalRT 1280x720\n" +
                 "• 相机源（CameraStreamSource）按 RT 尺寸渲染，比例永远正确\n" +
                 "• RT 源（RenderTextureStreamSource）Blit 缩放适配，非 16:9 外部 RT 会拉伸\n" +
                 "• 越大画质越好但码率/CPU 占用越高；越小越省但接收端画面糊\n" +
                 "• 变更尺寸前需停止会话并ClearSource，再整体应用新配置")]
        [Min(146)]
        public int StreamMaxLongSide = 1280;

        [Tooltip("推流帧率上限（fps）。\n" +
                 "• RT 源按此节流 Blit；相机源每帧渲染，由 sender.maxFramerate 约束编码输出。\n" +
                 "• 建议 ≤ XR 主循环频率（PICO 4 = 72/90），> 60 时编码器会丢帧。\n" +
                 "• 越低 PTS 步长越大、画面卡顿；越高带宽消耗越大。\n" +
                 "• 默认 30，VR 场景建议 30；2D 录屏可上 60。")]
        [Range(1, 60)]
        public int Fps = 30;

        [Tooltip("视频码率上限（kbps）。\n" +
                 "• 同时落到 SDP b=AS:N 行 + sender.maxBitrate。\n" +
                 "• 不要超过实际上行带宽，否则丢包 + 重传炸链路。\n" +
                 "• 默认 3500（局域网 1920x1920@30fps 推流推荐区间）。")]
        [Min(100)]
        public int VideoBandwidthKbps = 3500;

        [Tooltip("视频码率下限（kbps）。落到 sender.minBitrate。\n" +
                 "【关键参数】跳过 libwebrtc GCC 慢启动——默认初始 BWE ~100kbps 会让编码器主动降帧到 2-5fps，\n" +
                 "接收端 ffmpeg 按 PTS 推算可能 dup 帧卡死。\n" +
                 "• 局域网：1500 推荐（首秒即按目标码率输出）\n" +
                 "• 公网/弱网：300-800（防带宽不足时强发包丢包）\n" +
                 "约束：minBitrate ≤ VideoBandwidthKbps；超过会被 libwebrtc 拒绝。")]
        [Min(100)]
        public int VideoMinBitrateKbps = 1500;

        [Tooltip("是否强制 H.264 编码（删除 SDP 中其他视频 codec）。\n" +
                 "• 局域网 + PICO 4U：true（VP8/VP9 在 PICO 上软编 CPU 占用高）\n" +
                 "• PC/Editor 调试：可设 false 让 webrtc 自选\n" +
                 "• com.unity.webrtc 未编译 H.264 时设 true 会握手期报 CodecNegotiationFailed")]
        public bool ForceH264 = true;

        [Header("配置版本与网络策略")]
        [Min(1)] public int ConfigurationVersion = 1;
        [Min(1)] public int IceGatheringTimeoutSeconds = 3;
        [Min(1)] public int HttpTimeoutSeconds = 30;
        [Min(0.1f)] public float HandshakeTimeoutSeconds = 150;
        [Min(0.1f)] public float DisconnectedGraceSeconds = 5;
        [Min(0.1f)] public float StatisticsIntervalSeconds = 1;
        [Min(0.1f)] public float RemoteCleanupSeconds = 2;
        [Min(0.1f)] public float AudioDrainWarningSeconds = 5;
        [Min(1)] public int AudioBufferSamples = 32768;
        public int[] RetryDelaysMilliseconds = { 2000, 5000, 10000 };

        /// <summary>捕获完整媒体配置，不引用可变资产数组；供创建SdkOptions.Media时使用。</summary>
        public MediaOptions ToMediaOptions()
        {
            return new MediaOptions
            {
                Version = ConfigurationVersion, FramesPerSecond = Fps, OutputLongSide = StreamMaxLongSide,
                VideoBandwidthKbps = VideoBandwidthKbps, VideoMinBitrateKbps = VideoMinBitrateKbps, ForceH264 = ForceH264,
                IceGatheringTimeoutSeconds = IceGatheringTimeoutSeconds, HttpTimeoutSeconds = HttpTimeoutSeconds,
                HandshakeTimeoutSeconds = HandshakeTimeoutSeconds, DisconnectedGraceSeconds = DisconnectedGraceSeconds,
                StatisticsIntervalSeconds = StatisticsIntervalSeconds, RemoteCleanupSeconds = RemoteCleanupSeconds,
                AudioDrainWarningSeconds = AudioDrainWarningSeconds, AudioBufferSamples = AudioBufferSamples,
                RetryDelaysMilliseconds = RetryDelaysMilliseconds
            }.Capture();
        }

        /// <summary>显式将完整资产配置应用到指定客户端，分配更高运行版本；尺寸/音频容量仍受服务的生命周期约束。</summary>
        public SdkResult ApplyTo(StreamingService streaming)
        {
            if (streaming == null) return SdkResult.Fail(SdkErrorCode.InvalidArgument, "推流服务不能为空");
            MediaOptions options;
            try { options = ToMediaOptions(); }
            catch (System.ArgumentException error) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, error.Message); }
            int current = streaming.GetConfiguration().Version;
            if (current == int.MaxValue) return SdkResult.Fail(SdkErrorCode.InvalidState, "配置版本已耗尽，请创建新客户端");
            options.Version = System.Math.Max(options.Version, current + 1);
            return streaming.ApplyConfiguration(options);
        }
    }
}
