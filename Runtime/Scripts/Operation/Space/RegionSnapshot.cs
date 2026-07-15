using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// Region 全量快照（协议 RegionInfoPush 的归一化产物，与 PB 类型解耦）。
    /// 由 SpaceStateStore.ApplyRegionPush 构造：缺省的 gameInfo / 向量字段归零，缺省 showFloor 为 false。
    /// 注：RegionInfo.offset/offsetRotation 已废弃，不读入本快照（PB 仍保留字段供老客户端）。
    /// </summary>
    internal readonly struct RegionSnapshot
    {
        public readonly float Len;                     // 区域长（米）
        public readonly float Width;                   // 区域宽（米）
        public readonly Vector3 Center;                // 区域中心点
        public readonly Vector3 Rotation;              // 区域自身旋转（欧拉角，度）
        public readonly Vector3 GameOffset;            // 当前游戏（B）相对区域（A）的偏移坐标
        public readonly Vector3 GameOffsetRotation;    // 当前游戏（B）相对区域（A）的偏移旋转（欧拉角，度）
        public readonly bool ShowFloor;                // GameInfo.showFloor：是否显示本地区域地块可视物

        public RegionSnapshot(float len, float width, Vector3 center, Vector3 rotation,
                              Vector3 gameOffset, Vector3 gameOffsetRotation, bool showFloor)
        {
            Len = len;
            Width = width;
            Center = center;
            Rotation = rotation;
            GameOffset = gameOffset;
            GameOffsetRotation = gameOffsetRotation;
            ShowFloor = showFloor;
        }
    }
}
