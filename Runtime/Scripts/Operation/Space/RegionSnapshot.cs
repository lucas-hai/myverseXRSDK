using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// Region 全量快照（协议 RegionInfoPush 的归一化产物，与 PB 类型解耦）。
    /// 由 SpaceStateStore.ApplyRegionPush 构造：缺省的 gameInfo / 向量字段归零。
    /// </summary>
    internal readonly struct RegionSnapshot
    {
        public readonly float Len;                     // 区域长（米）
        public readonly float Width;                   // 区域宽（米）
        public readonly Vector3 Center;                // 区域中心点
        public readonly Vector3 Offset;                // 区域空间对齐微调位移
        public readonly Vector3 OffsetRotation;        // 区域空间对齐微调旋转（欧拉角，度）
        public readonly Vector3 Rotation;              // 区域自身旋转（欧拉角，度）
        public readonly Vector3 GameOffset;            // 当前游戏在该区域下的偏移坐标
        public readonly Vector3 GameOffsetRotation;    // 当前游戏在该区域下的偏移旋转（欧拉角，度）

        public RegionSnapshot(float len, float width, Vector3 center, Vector3 offset, Vector3 offsetRotation,
                              Vector3 rotation, Vector3 gameOffset, Vector3 gameOffsetRotation)
        {
            Len = len;
            Width = width;
            Center = center;
            Offset = offset;
            OffsetRotation = offsetRotation;
            Rotation = rotation;
            GameOffset = gameOffset;
            GameOffsetRotation = gameOffsetRotation;
        }
    }
}
