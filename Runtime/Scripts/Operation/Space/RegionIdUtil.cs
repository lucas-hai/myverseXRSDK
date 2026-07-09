using System.Globalization;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 地块 id 生成契约实现：运行时用远端 RegionInfoPush 的长宽生成临时 id 与本地条目匹配。
    /// 地块编辑工具不调用本类生成 id（条目 id 原样取自远端规格列表下发），
    /// 但远端规格 id 格式必须与本约定一致（长宽去尾零以 x 拼接，如 "12x6"），否则运行时匹配失败。
    /// </summary>
    public static class RegionIdUtil
    {
        /// <summary>长/宽格式化：InvariantCulture，最多 3 位小数，去尾零（12.0→"12"，12.50→"12.5"）。</summary>
        public static string FormatSize(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>由长宽生成地块 id，如 MakeId(12, 6) → "12x6"。</summary>
        public static string MakeId(float len, float width)
        {
            return $"{FormatSize(len)}x{FormatSize(width)}";
        }

        /// <summary>解析 id（"长x宽"）回长宽数值（编辑器新建条目的长宽默认值来源）；格式不符返回 false。</summary>
        public static bool TryParseId(string id, out float len, out float width)
        {
            len = 0f;
            width = 0f;
            if (string.IsNullOrEmpty(id)) return false;
            var parts = id.Split('x');
            if (parts.Length != 2) return false;
            return float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out len)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out width);
        }
    }
}
