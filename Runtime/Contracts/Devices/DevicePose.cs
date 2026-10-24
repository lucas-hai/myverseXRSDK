using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>设备最新房间位姿；当前网络协议仅使用XZ与yaw，不代表通用对象复制。</summary>
    public readonly struct DevicePose
    {
        public string DeviceId { get; }
        public string RoomId { get; }
        public string ModelId { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }

        public DevicePose(string deviceId, string roomId, string modelId, Vector3 position, Quaternion rotation)
        {
            DeviceId = deviceId;
            RoomId = roomId;
            ModelId = modelId;
            Position = position;
            Rotation = rotation;
        }
    }
}
