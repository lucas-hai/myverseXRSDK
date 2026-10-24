using UnityEngine;

namespace MyVerseXRSDK
{
    public enum ObstacleShape { Box, Ellipse }

    /// <summary>房间坐标中的障碍物数据；形状尺寸按协议适配器的资源基准映射。</summary>
    public readonly struct ObstacleUpdate
    {
        public string Id { get; }
        public ObstacleShape Shape { get; }
        public Vector3 Position { get; }
        public float Length { get; }
        public float Width { get; }
        public float Radius { get; }

        public ObstacleUpdate(string id, ObstacleShape shape, Vector3 position, float length, float width, float radius = 0f)
        {
            Id = id;
            Shape = shape;
            Position = position;
            Length = length;
            Width = width;
            Radius = radius;
        }
    }
}
