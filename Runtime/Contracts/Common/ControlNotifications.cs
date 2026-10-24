namespace MyVerseXRSDK
{
    public enum AdmissionSource { RoomState, DeviceCommand }

    /// <summary>中控允许开始业务的信号，不表示SDK执行过积分扣除。</summary>
    public readonly struct GameStartRequest
    {
        public string DeviceId { get; }
        public int TeamTag { get; }
        public AdmissionSource Source { get; }
        public GameStartRequest(string deviceId, int teamTag, AdmissionSource source)
        { DeviceId = deviceId; TeamTag = teamTag; Source = source; }
    }

    /// <summary>导演选择通知的协议值，不作为本机会话启动事件。</summary>
    public readonly struct DirectorSelection
    {
        public string DeviceId { get; }
        public bool IsPrimary { get; }
        public int Slot { get; }
        public int DurationSeconds { get; }
        public DirectorSelection(string deviceId, bool isPrimary, int slot, int durationSeconds)
        { DeviceId = deviceId; IsPrimary = isPrimary; Slot = slot; DurationSeconds = durationSeconds; }
    }
}
