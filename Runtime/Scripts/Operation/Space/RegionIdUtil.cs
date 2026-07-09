using System.Globalization;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 地块 id 契约实现。id 形态与远端区域规格 tagId 完全一致：宽X长、大写 X、整数（如 "6X12"）。
    /// 依据《开发者开放 API 文档》§四：tagId 首分量为宽（width）、次分量为长（height）。
    /// 地块编辑工具不生成 id（条目 id 原样取自远端规格列表 tagId）；
    /// 运行时用 RegionInfoPush 的长宽经 MakeRuntimeId 生成临时 id 与本地条目匹配。
    /// 【联调待确认】RegionInfoPush.len ↔ 平台 height（长）、.width ↔ 平台 width（宽）的对应关系。
    /// </summary>
    public static class RegionIdUtil
    {
        /// <summary>
        /// 运行时临时 id：长宽取绝对值后截断取整（不四舍五入，12.9→12），按远端 tagId 形态拼接
        /// "宽X长"，如 (len:12.35, width:6.18) → "6X12"。
        /// 服务端契约：tagId == MakeRuntimeId(实际长, 实际宽)——实测值截断后必须落回 tagId
        /// 的整数（如名义 6 实测 5.95 会截成 5 导致匹配失败，服务端须保证不发生）。
        /// </summary>
        public static string MakeRuntimeId(float len, float width)
        {
            return $"{(int)Mathf.Abs(width)}X{(int)Mathf.Abs(len)}";
        }

        /// <summary>解析 id（"宽X长"，兼容小写 x）回长宽数值（编辑器新建条目缺省长宽的回退来源）；格式不符返回 false。</summary>
        public static bool TryParseId(string id, out float len, out float width)
        {
            len = 0f;
            width = 0f;
            if (string.IsNullOrEmpty(id)) return false;
            var parts = id.Split('x', 'X');
            if (parts.Length != 2) return false;
            return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out len);
        }
    }
}
