using System;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 远端区域规格（长宽组合），创建地块条目时的 id 候选。
    /// id 以远端下发为准，工具不生成 id；远端 id 格式须与运行时临时 id 约定
    /// （RegionIdUtil：长x宽去尾零）一致，否则运行时匹配失败。
    /// </summary>
    [Serializable]
    public sealed class RegionSpec
    {
        public string id;
        public float len;
        public float width;
    }
}
