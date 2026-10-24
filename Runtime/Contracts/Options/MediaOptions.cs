namespace MyVerseXRSDK
{
    /// <summary>媒体配置整体快照；输出规格只在无会话时重建，协商参数在下次会话生效。</summary>
    public sealed class MediaOptions
    {
        public int Version { get; set; } = 1;
        public int OutputLongSide { get; set; } = 1280;
        public int FramesPerSecond { get; set; } = 30;
        public int VideoBandwidthKbps { get; set; } = 3500;
        public int VideoMinBitrateKbps { get; set; } = 1500;
        public bool ForceH264 { get; set; } = true;
        public int IceGatheringTimeoutSeconds { get; set; } = 3;
        public int HttpTimeoutSeconds { get; set; } = 30;
        public float HandshakeTimeoutSeconds { get; set; } = 150;
        public float DisconnectedGraceSeconds { get; set; } = 5;
        public float StatisticsIntervalSeconds { get; set; } = 1;
        public float RemoteCleanupSeconds { get; set; } = 2;
        public float AudioDrainWarningSeconds { get; set; } = 5;
        public int AudioBufferSamples { get; set; } = 32768;
        public int[] RetryDelaysMilliseconds { get; set; } = new[] { 2000, 5000, 10000 };

        internal MediaOptions Capture()
        {
            if (Version < 1 || OutputLongSide < 146 || OutputLongSide > 4096 || FramesPerSecond <= 0 ||
                VideoBandwidthKbps <= 0 || VideoMinBitrateKbps < 0 || VideoMinBitrateKbps > VideoBandwidthKbps ||
                IceGatheringTimeoutSeconds <= 0 || HttpTimeoutSeconds <= 0 || AudioBufferSamples <= 0 ||
                !Positive(HandshakeTimeoutSeconds) || !Positive(DisconnectedGraceSeconds) ||
                !Positive(StatisticsIntervalSeconds) || !Positive(RemoteCleanupSeconds) || !Positive(AudioDrainWarningSeconds))
                throw new System.ArgumentException("媒体配置无效：请检查尺寸、码率、帧率、超时及版本");
            if (RetryDelaysMilliseconds == null) throw new System.ArgumentException("重试计划不能为null；空数组表示禁用");
            foreach (int delay in RetryDelaysMilliseconds)
                if (delay < 0) throw new System.ArgumentException("媒体重试间隔不能为负数");
            var captured = (MediaOptions)MemberwiseClone();
            captured.RetryDelaysMilliseconds = (int[])RetryDelaysMilliseconds.Clone();
            return captured;
        }

        private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
