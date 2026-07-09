using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>单条本地地块数据（由地块编辑器工具产出，运行时按 id 匹配参与根节点计算）。</summary>
    [Serializable]
    public class LocalRegionData
    {
        public string id;           // 来自远端规格列表，形态 "长x宽"（与 RegionIdUtil 格式约定一致）
        public float len;           // 长（米），SceneView 底框沿本地 Z 轴
        public float width;         // 宽（米），SceneView 底框沿本地 X 轴
        public Vector3 position;    // 场景内坐标
        public Vector3 rotation;    // 欧拉角（度）

        public LocalRegionData Clone()
        {
            return new LocalRegionData { id = id, len = len, width = width, position = position, rotation = rotation };
        }
    }

    /// <summary>
    /// 本地区域数据列表文件（ScriptableObject 资产）。
    /// 运行时按 <see cref="ResourcesLoadPath"/> 固定路径 Resources.Load，放错路径/改名将加载不到。
    /// </summary>
    [CreateAssetMenu(menuName = "MyVerse XR SDK/Local Region Data List", fileName = "LocalRegionData")]
    public class LocalRegionDataList : ScriptableObject
    {
        /// <summary>运行时加载契约路径（相对任一 Resources 根）。</summary>
        public const string ResourcesLoadPath = "MVXRSDK/LocalRegionData";

        public List<LocalRegionData> entries = new List<LocalRegionData>();

        /// <summary>按 id 精确匹配（Ordinal），未命中或空输入返回 null。不做数值容差匹配。</summary>
        public LocalRegionData FindById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e != null && string.Equals(e.id, id, StringComparison.Ordinal)) return e;
            }
            return null;
        }

        public bool ContainsId(string id) => FindById(id) != null;
    }
}
