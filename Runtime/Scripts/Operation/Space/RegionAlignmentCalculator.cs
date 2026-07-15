using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 区域对齐公式（纯函数，无场景依赖，可直接单测）。返回值是根节点的**世界**位姿。
    ///
    /// 层级模型（见关系图）：A（远端区域）为父节点、B（远端游戏偏移 GameInfo）为其子节点；
    /// C（本地区域地片 tile）与 A **同级**（世界层级）。计算分两步：
    ///   1. 先求 B 的世界位姿（B 是 A 的子节点，被 A 旋转带动）：
    ///        B.worldRot = Rotate(Rotation) · Rotate(GameOffsetRotation)
    ///        B.worldPos = Center + Rotate(Rotation) · GameOffset
    ///   2. 再对位置减去 C（C 与 A 同级、在世界层级，**不被 A 旋转带动**；只减位置，不参与旋转）：
    ///        pos = B.worldPos − tile.position
    ///        rot = B.worldRot
    /// 结果作为世界位姿赋给场景根节点。先算旋转再算位置。
    ///
    /// 修正点：旧式旋转用欧拉逐分量相减、位置直接相减且不区分 B/C 的旋转归属，区域带旋转即错位。
    /// 地片 rotation 与 RegionInfo.offset/offsetRotation 均不参与计算。
    /// </summary>
    internal static class RegionAlignmentCalculator
    {
        public static (Vector3 position, Vector3 eulerAngles) Compute(in RegionSnapshot snapshot, LocalRegionData tile)
        {
            Quaternion regionRot = Quaternion.Euler(snapshot.Rotation);

            // 1) 先求 B（游戏偏移，A 的子节点）的世界位姿——被 A 旋转带动
            Quaternion rotationB = regionRot * Quaternion.Euler(snapshot.GameOffsetRotation);
            Vector3    bWorldPos = snapshot.Center + regionRot * snapshot.GameOffset;

            // 2) 再对位置减去 C（地片，与 A 同级；世界层级直接相减，不被旋转带动，不参与旋转）
            Vector3 position = bWorldPos - tile.position;

            return (position, rotationB.eulerAngles);
        }
    }
}
