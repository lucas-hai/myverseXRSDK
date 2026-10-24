using UnityEngine;

namespace MyVerseXRSDK
{
    internal static class SpatialValueValidation
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        internal static bool Rotation(Quaternion value)
        {
            if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z) || !Finite(value.w)) return false;
            float magnitude = value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w;
            return Finite(magnitude) && magnitude > 0.000001f;
        }

        internal static bool Region(RegionUpdate value) =>
            Finite(value.Length) && Mathf.Abs(value.Length) >= 1f &&
            Finite(value.Width) && Mathf.Abs(value.Width) >= 1f &&
            Finite(value.Center) && Rotation(value.Rotation) &&
            Finite(value.GameOffset) && Rotation(value.GameOffsetRotation);

        internal static bool Obstacle(ObstacleUpdate value)
        {
            if (string.IsNullOrWhiteSpace(value.Id) || !Finite(value.Position)) return false;
            if (value.Shape == ObstacleShape.Ellipse) return Finite(value.Radius) && value.Radius > 0f;
            return value.Shape == ObstacleShape.Box && Finite(value.Length) && value.Length > 0f &&
                   Finite(value.Width) && value.Width > 0f;
        }

        internal static bool Device(DevicePose value) => !string.IsNullOrWhiteSpace(value.DeviceId) &&
            Finite(value.Position) && Rotation(value.Rotation);
    }
}
