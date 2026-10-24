namespace MyVerseXRSDK
{
    /// <summary>SDK数据接受代次的房间快照；Epoch不是服务端房间实例标识。</summary>
    public readonly struct SessionSnapshot
    {
        public long Epoch { get; }
        public SessionState State { get; }
        public string RoomId { get; }
        public bool IsJoined => State == SessionState.Joined;

        public SessionSnapshot(long epoch, SessionState state, string roomId)
        {
            Epoch = epoch;
            State = state;
            RoomId = roomId ?? string.Empty;
        }
    }
}
