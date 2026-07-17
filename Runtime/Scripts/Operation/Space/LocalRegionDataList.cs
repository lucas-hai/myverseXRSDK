using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>单条本地地块数据（由地块编辑器工具产出，运行时按 id 匹配参与根节点计算）。</summary>
    [Serializable]
    public class LocalRegionData
    {
        public string id;           // 来自远端规格列表 tagId，形态"宽x长"小写（与 RegionIdUtil.MakeRuntimeId 一致）
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
    /// 单环境的地块条目组 + 防篡改封签。签名由地块编辑器在保存/删除后写入
    /// （规范化序列化的 HMAC，见 RegionDataSeal）；运行时验签失败该组拒用。
    /// Offline 环境组无签名（本地直存，无防篡改）。
    /// </summary>
    [Serializable]
    public class EnvironmentTileSet
    {
        public SdkEnvironment env;
        public List<LocalRegionData> entries = new List<LocalRegionData>();
        public string signature;

        /// <summary>
        /// 组内按 id 精确匹配（Ordinal），未命中或空输入返回 null。不做数值容差匹配。
        /// 匹配规则唯一实现——编辑器查重与运行时匹配共用，避免两套循环漂移。
        /// </summary>
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
    }

    /// <summary>
    /// 本地区域数据列表文件（ScriptableObject 资产），按环境分组存储。
    /// 运行时按 <see cref="ResourcesLoadPath"/> 固定路径 Resources.Load，放错路径/改名将加载不到；
    /// 运行时取 <see cref="activeEnvironment"/> 对应组并验签（见 LocalRegionTileStore）。
    /// 本资产由地块编辑器管理，请勿手改——绕过工具修改会使签名失效、运行时整组拒用。
    /// </summary>
    [CreateAssetMenu(menuName = "MyVerse XR SDK/Local Region Data List", fileName = "LocalRegionData")]
    public class LocalRegionDataList : ScriptableObject
    {
        /// <summary>运行时加载契约路径（相对任一 Resources 根）。</summary>
        public const string ResourcesLoadPath = "MVXRSDK/LocalRegionData";

        [Tooltip("激活环境（验证面板/地块编辑器同步写入；运行时按此取对应环境组）")]
        public SdkEnvironment activeEnvironment = SdkEnvironment.Production;

        [Tooltip("各环境地块组（由地块编辑器管理，勿手改）")]
        public List<EnvironmentTileSet> sets = new List<EnvironmentTileSet>();

        /// <summary>旧版单环境平铺列表：仅作迁移源（编辑器迁入 Test 组后清空），运行时不读。</summary>
        [HideInInspector] public List<LocalRegionData> entries = new List<LocalRegionData>();

        /// <summary>取指定环境组，无则返回 null。</summary>
        public EnvironmentTileSet GetSet(SdkEnvironment env)
        {
            for (int i = 0; i < sets.Count; i++)
            {
                if (sets[i] != null && sets[i].env == env) return sets[i];
            }
            return null;
        }

        /// <summary>取指定环境组，无则创建（编辑器工具用）。</summary>
        public EnvironmentTileSet GetOrCreateSet(SdkEnvironment env)
        {
            var set = GetSet(env);
            if (set == null)
            {
                set = new EnvironmentTileSet { env = env };
                sets.Add(set);
            }
            return set;
        }

        /// <summary>指定环境组内按 id 精确匹配（规则见 <see cref="EnvironmentTileSet.FindById"/>），无该组返回 null。</summary>
        public LocalRegionData FindById(SdkEnvironment env, string id)
        {
            var set = GetSet(env);
            return set == null ? null : set.FindById(id);
        }

        public bool ContainsId(SdkEnvironment env, string id) => FindById(env, id) != null;
    }
}
