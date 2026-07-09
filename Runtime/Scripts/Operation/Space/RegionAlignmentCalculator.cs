using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 区域对齐公式（纯函数，无场景依赖，可直接单测）。逐分量约定算术（业务方拍板，非刚体对齐）：
    ///   基础位置 = 区域Center − 地片position + 区域Offset
    ///   基础旋转 = 区域Rotation − 地片rotation + 区域OffsetRotation   （欧拉角逐分量）
    ///   最终位姿 = 基础 + GameOffset / GameOffsetRotation
    /// 边界：地片带非零旋转且不在原点时，地片矩形不严格落在区域矩形上（差绕轴弧线），按约定执行。
    /// </summary>
    internal static class RegionAlignmentCalculator
    {
        public static (Vector3 position, Vector3 eulerAngles) Compute(in RegionSnapshot snapshot, LocalRegionData tile)
        {
            Vector3 basePosition = snapshot.Center - tile.position + snapshot.Offset;
            Vector3 baseRotation = snapshot.Rotation - tile.rotation + snapshot.OffsetRotation;
            return (basePosition + snapshot.GameOffset, baseRotation + snapshot.GameOffsetRotation);
        }
    }
}
