using System;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 远端区域规格，创建地块条目时的 id 候选。id 原样取自远端 tagId（宽X长，如 "6X12"），
    /// 同时是运行时匹配键（契约：tagId == RegionIdUtil.MakeRuntimeId(实际长, 实际宽)）；
    /// len/width 为远端下发尺寸（len ↔ 接口字段 height），作为地块绘制默认值。
    /// </summary>
    [Serializable]
    public sealed class RegionSpec
    {
        public string id;
        public float len;
        public float width;
    }
}
