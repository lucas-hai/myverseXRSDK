using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    // 别名必须在 namespace 内：父命名空间的 internal MyVerseXRSDK.MessageType（IVT 后可见）优先于文件顶层 using
    using MessageType = UnityEditor.MessageType;

    /// <summary>
    /// 地块数据资产只读 Inspector：本资产由地块编辑器管理，不提供直接编辑入口——
    /// 绕过工具修改会使防篡改签名失效、运行时整组拒用（这是刻意的：本地数据必须与平台上传一致）。
    /// 封签校验结果在 OnEnable 缓存（HMAC 别跟着每次重绘算），"刷新"按钮手动重验。
    /// </summary>
    [CustomEditor(typeof(LocalRegionDataList))]
    internal sealed class LocalRegionDataListInspector : UnityEditor.Editor
    {
        private readonly List<string> m_SetSummaries = new List<string>();

        private void OnEnable()
        {
            var dataList = (LocalRegionDataList)target;
            RegionDataMigration.MigrateIfNeeded(dataList);   // 一次性迁移（幂等），别放每帧
            RefreshSealSummaries(dataList);
        }

        // 缓存各环境组的"条目数 + 封签状态"展示行（含 HMAC 校验，只在进入/手动刷新时算）
        private void RefreshSealSummaries(LocalRegionDataList dataList)
        {
            m_SetSummaries.Clear();
            foreach (var set in dataList.sets)
            {
                if (set == null) continue;
                string sealState = set.env == SdkEnvironment.Offline
                    ? "本地直存（无封签）"
                    : RegionDataSeal.Verify(set) ? "封签有效" : "无有效封签（运行时拒用）";
                m_SetSummaries.Add($"{SdkAuthStore.EnvDisplayName(set.env)}｜{set.entries.Count} 条 ｜ {sealState}");
            }
        }

        public override void OnInspectorGUI()
        {
            var dataList = (LocalRegionDataList)target;

            EditorGUILayout.HelpBox(
                "本资产由地块编辑器管理（Hierarchy 右键 → MyVerse → 生成本地区域地块编辑器），请勿手改。\n" +
                "绕过编辑器修改会使防篡改签名失效，运行时将整组拒用。", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("激活环境", SdkAuthStore.EnvDisplayName(dataList.activeEnvironment));
            if (GUILayout.Button("刷新", GUILayout.Width(44f)))
                RefreshSealSummaries(dataList);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("各环境地块组", EditorStyles.boldLabel);
            if (m_SetSummaries.Count == 0)
                EditorGUILayout.HelpBox("暂无任何环境组", MessageType.None);
            foreach (var summary in m_SetSummaries)
                EditorGUILayout.LabelField("  " + summary);

            // 只读展示序列化细节（折叠），便于排查但不可编辑
            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(true))
            {
                DrawDefaultInspector();
            }
        }
    }
}
