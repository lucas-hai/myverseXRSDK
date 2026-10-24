using UnityEngine;

namespace MyVerseXRSDK
{
    internal static class CalibrationSolver
    {
        internal static SdkResult Solve(RegionUpdate region, RegionTile tile, SpatialOptions options,
            out Pose gameToRoom, out Pose roomToGame)
        {
            gameToRoom = default;
            roomToGame = default;
            if (!SpatialValueValidation.Region(region) || !SpatialValueValidation.Finite(tile.Position))
                return Invalid("区域或地块位姿包含无效值");
            var regionRotation = region.Rotation.normalized;
            var rotation = (regionRotation * region.GameOffsetRotation.normalized).normalized;
            var position = region.Center + regionRotation * region.GameOffset - tile.Position;
            if (!SpatialValueValidation.Finite(position)) return Invalid("区域计算结果溢出");
            if (options.Calibration == CalibrationMode.HorizontalYaw)
            {
                var euler = rotation.eulerAngles;
                if (Mathf.Abs(position.y) > options.HeightTolerance ||
                    Mathf.Abs(Mathf.DeltaAngle(0, euler.x)) > options.TiltToleranceDegrees ||
                    Mathf.Abs(Mathf.DeltaAngle(0, euler.z)) > options.TiltToleranceDegrees)
                    return Invalid("校准结果超过水平模式的高度或倾斜容差");
                position.y = 0;
                rotation = Quaternion.Euler(0, Mathf.DeltaAngle(0, euler.y), 0);
            }
            var inverse = Quaternion.Inverse(rotation);
            // 地图保持游戏坐标，完整逆平移必须由逆旋转作用，不能只取位置负值。
            gameToRoom = new Pose(position, rotation);
            roomToGame = new Pose(inverse * -position, inverse);
            return SdkResult.Ok;
        }

        private static SdkResult Invalid(string message) => SdkResult.Fail(SdkErrorCode.InvalidCalibration, message);
    }
}
