namespace MyVerseXRSDK
{
    public enum StreamingState { Idle, Starting, Streaming, Recovering, Stopping, Failed }

    /// <summary>媒体会话统计。无有效采样或会话结束时IsValid为false，码率单位为kbps。</summary>
    public readonly struct StreamingStatistics
    {
        public long SessionEpoch { get; }
        public double Timestamp { get; }
        public bool IsValid { get; }
        public double VideoBitrateKbps { get; }
        public double AudioBitrateKbps { get; }
        public double TotalBitrateKbps => VideoBitrateKbps + AudioBitrateKbps;
        public double RttMilliseconds { get; }
        public long PacketsLost { get; }

        public StreamingStatistics(long epoch, double timestamp, double videoKbps, double audioKbps, double rttMilliseconds, long packetsLost)
        {
            SessionEpoch = epoch; Timestamp = timestamp; IsValid = true;
            VideoBitrateKbps = videoKbps; AudioBitrateKbps = audioKbps;
            RttMilliseconds = rttMilliseconds; PacketsLost = packetsLost;
        }
    }
}
