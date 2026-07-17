using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 地块数据旧版结构迁移：旧平铺 entries → 测试环境组（既有数据当时从测试环境上传，归测试组符合事实）。
    /// 幂等：entries 空即不再触发。迁移后签名留空——运行时会拒用，需在验证面板"重新封签"或逐条重新保存。
    /// 触发点：编辑器启动（SdkAuthStartup）+ 验证面板/地块编辑器/资产 Inspector 惰性调用。
    /// </summary>
    internal static class RegionDataMigration
    {
        /// <summary>
        /// 旧 entries 非空时迁入 Test 组；发生迁移返回 true。
        /// 防呆：Test 组已有数据时不自动合并（重复追加会污染数据并毁掉有效签名）——告警留待人工处理。
        /// </summary>
        internal static bool MigrateIfNeeded(LocalRegionDataList dataList)
        {
            if (dataList == null || dataList.entries == null || dataList.entries.Count == 0) return false;

            var testSet = dataList.GetOrCreateSet(SdkEnvironment.Test);
            if (testSet.entries.Count > 0)
            {
                // 触发点多（启动/各 Inspector/窗口焦点），异常态每会话每资产只报一次，避免刷屏
                var conflictKey = "MVXRSDK_RegionMigrateConflict_" + AssetDatabase.GetAssetPath(dataList);
                if (!SessionState.GetBool(conflictKey, false))
                {
                    SessionState.SetBool(conflictKey, true);
                    Debug.LogError($"[MVXRSDK] 地块数据迁移冲突：旧平铺 entries（{dataList.entries.Count} 条）与测试环境组" +
                                   $"（{testSet.entries.Count} 条）并存——自动合并会产生重复条目并使签名失效，已跳过。" +
                                   "请人工确认后删除资产中的旧 entries（或清空测试组后重开面板迁移）。");
                }
                return false;
            }

            Undo.RecordObject(dataList, "迁移地块数据到测试环境组");
            testSet.entries.AddRange(dataList.entries);
            testSet.signature = null;   // 旧数据无签名：留空待补签（有签名字段的版本一律要求签名，否则删签即绕过）
            dataList.entries.Clear();
            EditorUtility.SetDirty(dataList);
            AssetDatabase.SaveAssets();
            Debug.LogWarning($"[MVXRSDK] 地块数据已迁移到测试环境组（{testSet.entries.Count} 条）。" +
                             "迁移数据尚未封签，运行时将拒用——请在 SDK 验证面板（测试环境）执行\"重新封签\"。");
            return true;
        }

        /// <summary>
        /// 全工程查找地块数据资产：多个时**优先运行时契约路径**（Resources/MVXRSDK/LocalRegionData.asset）——
        /// 环境同步/重新封签/出包校验都要落在运行时真正加载的那份资产上，盲取第一个会操作错资产。
        /// 不存在返回 null。
        /// </summary>
        internal static LocalRegionDataList FindDataList()
        {
            var guids = AssetDatabase.FindAssets("t:LocalRegionDataList");
            if (guids.Length == 0) return null;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(ContractAssetSuffix))
                    return AssetDatabase.LoadAssetAtPath<LocalRegionDataList>(path);
            }
            return AssetDatabase.LoadAssetAtPath<LocalRegionDataList>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        /// <summary>运行时契约资产路径后缀（由 ResourcesLoadPath 派生，改契约路径只动一处）。</summary>
        internal static string ContractAssetSuffix => $"/Resources/{LocalRegionDataList.ResourcesLoadPath}.asset";

        /// <summary>
        /// 环境标记同步进地块数据资产（运行时按它取环境组；设置资产不进包）。
        /// 唯一写入口——验证面板与地块编辑器共用，避免同步规则漂移。
        /// </summary>
        internal static void SyncActiveEnvironment(LocalRegionDataList dataList, SdkEnvironment env)
        {
            if (dataList == null || dataList.activeEnvironment == env) return;
            Undo.RecordObject(dataList, "同步地块激活环境");
            dataList.activeEnvironment = env;
            EditorUtility.SetDirty(dataList);
        }
    }
}
