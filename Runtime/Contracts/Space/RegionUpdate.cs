using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>协议房间坐标中的区域全量值，不包含生成的协议类型。</summary>
    public readonly struct RegionUpdate
    {
        public float Length { get; }
        public float Width { get; }
        public Vector3 Center { get; }
        public Quaternion Rotation { get; }
        public Vector3 GameOffset { get; }
        public Quaternion GameOffsetRotation { get; }
        public bool ShowFloor { get; }

        public RegionUpdate(float length, float width, Vector3 center, Quaternion rotation,
            Vector3 gameOffset, Quaternion gameOffsetRotation, bool showFloor = false)
        {
            Length = length;
            Width = width;
            Center = center;
            Rotation = rotation;
            GameOffset = gameOffset;
            GameOffsetRotation = gameOffsetRotation;
            ShowFloor = showFloor;
        }
    }

    /// <summary>本地地块只读值；Rotation保留制作数据，不额外叠加到现行校准公式。</summary>
    public readonly struct RegionTile
    {
        public string Id { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float Width { get; }
        public float Length { get; }

        public RegionTile(string id, Vector3 position, Quaternion rotation, float width, float length)
        {
            Id = id;
            Position = position;
            Rotation = rotation;
            Width = width;
            Length = length;
        }
    }

    /// <summary>地块查询口；调用限主线程，失败必须区分无匹配与资产无效。</summary>
    public interface IRegionTileSource
    {
        int Version { get; }
        SdkResult Find(string id, out RegionTile tile);
    }
}
