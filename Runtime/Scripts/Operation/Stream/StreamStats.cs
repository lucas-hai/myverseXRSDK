namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流实时统计快照。由 WebRTCSystem 每 <see cref="StreamConfig.StatsReportIntervalMs"/> 采集一次，
    /// 通过 <see cref="MVXRSDK.OnPushStreamStats"/> 事件回调 + <see cref="MVXRSDK.GetStreamStats"/> 同步查询暴露。
    ///
    /// 数据来源（com.unity.webrtc 3.0.0-pre.8）：
    /// - <c>RTCOutboundRTPStreamStats</c> → 发送字节 / 包数 / 编码帧数
    /// - <c>RTCRemoteInboundRtpStreamStats</c> → 远端反馈的丢包数 / jitter / RTT
    /// - <c>RTCIceCandidatePairStats</c> → 可用上行带宽 / current RTT
    ///
    /// 字段为 0 表示该指标在本周期未拿到（如握手刚完成时 remote-inbound 还没回报）。
    /// </summary>
    public sealed class StreamStats
    {
        /// <summary>累计发送字节数（视频 + 音频）。</summary>
        public long BytesSent;

        /// <summary>累计发送包数。</summary>
        public long PacketsSent;

        /// <summary>远端反馈：累计丢包数。</summary>
        public long PacketsLost;

        /// <summary>远端反馈：网络抖动（毫秒）。</summary>
        public double JitterMs;

        /// <summary>当前 ICE 链路 RTT（毫秒）。</summary>
        public double RttMs;

        /// <summary>视频累计编码帧数。</summary>
        public int FramesEncoded;

        /// <summary>近 1 秒视频发送码率（kbps），由 bytesSent 增量计算。</summary>
        public int VideoBitrateKbps;

        /// <summary>BWE 估算的可用上行带宽（kbps），mediamtx 端给出。0 = 未上报。</summary>
        public int AvailableOutgoingBitrateKbps;

        /// <summary>本次采样的时间戳（秒，Time.realtimeSinceStartup）。</summary>
        public float Timestamp;
    }
}
