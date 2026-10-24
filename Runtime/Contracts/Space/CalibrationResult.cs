using UnityEngine;

namespace MyVerseXRSDK
{
    public enum SpatialState
    {
        Disabled, Unbound, WaitingForRegion, Unavailable, Computed,
        Ready, StaleHold, Invalidated, BindingInvalid
    }

    /// <summary>有明确坐标方向和数据归属的校准值，计算成功不自动表示已写入当前绑定。</summary>
    public readonly struct CalibrationResult
    {
        public long RuntimeId { get; }
        public long SessionEpoch { get; }
        public long RegionRevision { get; }
        public string TileId { get; }
        public int TileVersion { get; }
        public Pose GameToRoom { get; }
        public Pose RoomToGame { get; }
        public CalibrationMode Mode { get; }

        public CalibrationResult(long runtimeId, long sessionEpoch, long regionRevision, string tileId,
            int tileVersion, Pose gameToRoom, Pose roomToGame, CalibrationMode mode)
        {
            RuntimeId = runtimeId;
            SessionEpoch = sessionEpoch;
            RegionRevision = regionRevision;
            TileId = tileId;
            TileVersion = tileVersion;
            GameToRoom = gameToRoom;
            RoomToGame = roomToGame;
            Mode = mode;
        }
    }

    /// <summary>当前空间能力状态；必须同时检查当前代次及应用标志，不能用联网状态代替。</summary>
    public readonly struct SpatialStatus
    {
        public SpatialState State { get; }
        public bool HasCurrentResult { get; }
        public bool IsApplied { get; }
        public bool IsCurrent { get; }
        public SdkResult Reason { get; }

        public SpatialStatus(SpatialState state, bool hasCurrentResult, bool isApplied, bool isCurrent, SdkResult reason)
        {
            State = state;
            HasCurrentResult = hasCurrentResult;
            IsApplied = isApplied;
            IsCurrent = isCurrent;
            Reason = reason;
        }
    }
}
