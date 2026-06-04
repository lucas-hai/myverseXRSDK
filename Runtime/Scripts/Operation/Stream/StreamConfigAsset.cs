using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流"视频编码参数"的 Unity Asset 入口（ScriptableObject）。
    ///
    /// 范围：仅暴露使用者通常需要调整的视频编码参数（帧率 / 码率 / 编码格式）。
    /// 握手超时、重试节奏、自愈窗口、Stats 间隔等推流业务逻辑参数**不在此 Asset**，
    /// 走 SDK 内置默认；若确需调走 <see cref="MVXRSDK.SetStreamConfig"/> 代码入口。
    ///
    /// 用法：Project 右键 → Create → MyVerse XR SDK → Stream Config 生成 Asset，
    /// 拖到 <see cref="Streaming.MVXRStreamRig"/> 的 streamConfigAsset 字段，
    /// Rig OnEnable 时自动 <see cref="Apply"/> 写入生效配置。
    /// </summary>
    [CreateAssetMenu(menuName = "MyVerse XR SDK/Stream Config", fileName = "StreamConfig")]
    public sealed class StreamConfigAsset : ScriptableObject
    {
        [Header("视频编码")]
        [Tooltip("推流画面长边像素上限。按原画面比例同比例缩放保持不形变。\n" +
                 "  PICO 4U 单眼 ~1920x1920 (≈1:1)，1280 → 推流 1280x1280\n" +
                 "  非 XR 1920x1080 (16:9)，1280 → 推流 1280x720\n" +
                 "• 越大画质越好但码率/CPU 占用越高；越小越省但接收端画面糊\n" +
                 "• 0 = 不限（用源 RT 原始尺寸；PICO 单眼 1920+ 可能撑爆编码器）\n" +
                 "• 推流进行中改不会重建 RT，需 Stop → Start 才生效\n" +
                 "• 仅 MVXRStreamRig 路径生效；业务自管 IStreamSource 时此字段被忽略")]
        [Min(0)]
        public int StreamMaxLongSide = 1280;

        [Tooltip("推流帧率上限（fps）。\n" +
                 "• 同时写入 sender.maxFramerate，硬约束编码器输出。\n" +
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

        // ============================== 转换 / 应用 ==============================

        /// <summary>
        /// 把 Asset 的视频编码字段叠加到一份新的 <see cref="StreamConfig"/>。
        /// 握手/重试/容错/Stats 等未暴露字段保留 <see cref="StreamConfig"/> 默认值（SDK 内置策略）。
        /// </summary>
        public StreamConfig ToStreamConfig()
        {
            // 起点 = StreamConfig 默认实例（持有 SDK 内置握手/容错/Stats 策略）
            // 仅覆盖 Asset 暴露的视频编码 4 项
            return new StreamConfig
            {
                Fps = Fps,
                StreamMaxLongSide = StreamMaxLongSide,
                VideoBandwidthKbps = VideoBandwidthKbps,
                VideoMinBitrateKbps = VideoMinBitrateKbps,
                ForceH264 = ForceH264,
            };
        }

        /// <summary>把 Asset 配置写入 SDK 全局生效配置（等价 <see cref="MVXRSDK.SetStreamConfig"/>）。</summary>
        public void Apply()
        {
            MVXRSDK.SetStreamConfig(ToStreamConfig());
        }
    }
}
