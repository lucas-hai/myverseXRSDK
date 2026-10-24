using UnityEngine;

namespace MyVerseXRSDK
{
    public enum SpatialMode { XrCalibration, ComputeOnly, Disabled }
    public enum CalibrationMode { HorizontalYaw, Rigid3D }

    /// <summary>空间计算与显示配置；TileSource为空时由运行时加载标准地块资产。</summary>
    public sealed class SpatialOptions
    {
        public SpatialMode Mode { get; set; } = SpatialMode.XrCalibration;
        public CalibrationMode Calibration { get; set; } = CalibrationMode.HorizontalYaw;
        public IRegionTileSource TileSource { get; set; }
        public float HeightTolerance { get; set; } = 0.001f;
        public float TiltToleranceDegrees { get; set; } = 0.5f;
        public bool ShowObstacles { get; set; } = true;
        public int MaximumObstacles { get; set; } = 4096;
        public bool ShowFloor { get; set; } = true;
        public GameObject BoxPrefab { get; set; }
        public GameObject EllipsePrefab { get; set; }
        public float ObstacleDistance { get; set; } = 1.5f;
        public float VisibilityHysteresis { get; set; } = 0.1f;

        internal SpatialOptions Capture()
        {
            if (!System.Enum.IsDefined(typeof(SpatialMode), Mode) || !System.Enum.IsDefined(typeof(CalibrationMode), Calibration) ||
                !ValidNonnegative(HeightTolerance) || !ValidNonnegative(TiltToleranceDegrees) ||
                !ValidNonnegative(ObstacleDistance) || !ValidNonnegative(VisibilityHysteresis) || MaximumObstacles <= 0)
                throw new System.ArgumentException("空间模式或容差/距离配置无效");
            return new SpatialOptions
            {
                Mode = Mode, Calibration = Calibration, TileSource = TileSource,
                HeightTolerance = HeightTolerance, TiltToleranceDegrees = TiltToleranceDegrees,
                ShowObstacles = ShowObstacles, MaximumObstacles = MaximumObstacles, ShowFloor = ShowFloor, BoxPrefab = BoxPrefab, EllipsePrefab = EllipsePrefab,
                ObstacleDistance = ObstacleDistance, VisibilityHysteresis = VisibilityHysteresis
            };
        }

        internal static bool ValidNonnegative(float value) => value >= 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>设备位姿与虚影策略，独立于业务网络角色。</summary>
    public sealed class DeviceOptions
    {
        public bool ReportingEnabled { get; set; } = true;
        public bool AvatarsEnabled { get; set; } = true;
        public int MaximumDevices { get; set; } = 1024;
        public GameObject AvatarPrefab { get; set; }
        public bool SameRoomAvatars { get; set; }
        public float SameRoomDistance { get; set; } = 2f;
        public float OtherRoomDistance { get; set; } = 2f;
        public float SamplingIntervalSeconds { get; set; } = 0.2f;
        public float PositionThreshold { get; set; } = 0.001f;
        public float YawThresholdDegrees { get; set; } = 0.5f;

        internal DeviceOptions Capture()
        {
            if (MaximumDevices <= 0 || !SpatialOptions.ValidNonnegative(SameRoomDistance) || !SpatialOptions.ValidNonnegative(OtherRoomDistance) ||
                !SpatialOptions.ValidNonnegative(SamplingIntervalSeconds) || SamplingIntervalSeconds <= 0 ||
                !SpatialOptions.ValidNonnegative(PositionThreshold) || !SpatialOptions.ValidNonnegative(YawThresholdDegrees))
                throw new System.ArgumentException("设备采样间隔、阈值或显示距离无效");
            return new DeviceOptions
            {
                ReportingEnabled = ReportingEnabled, AvatarsEnabled = AvatarsEnabled, MaximumDevices = MaximumDevices, SameRoomAvatars = SameRoomAvatars, AvatarPrefab = AvatarPrefab,
                SameRoomDistance = SameRoomDistance, OtherRoomDistance = OtherRoomDistance,
                SamplingIntervalSeconds = SamplingIntervalSeconds, PositionThreshold = PositionThreshold,
                YawThresholdDegrees = YawThresholdDegrees
            };
        }
    }
}
