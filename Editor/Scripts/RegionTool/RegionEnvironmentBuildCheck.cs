using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 按SDK设置执行空间构建门禁；错误始终阻止构建，告警只记录，不提供交互式绕过。
    /// </summary>
    internal sealed class RegionEnvironmentBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SdkAuthStore.FindSettings();
            if (settings != null && !settings.requireSpatialDataForBuild) return;
            var issue = RegionBuildPolicy.Evaluate(settings, FindPlayerTiles());
            if (issue.Severity == BuildIssueSeverity.Error)
                throw new BuildFailedException("[MVXRSDK] 出包检查未通过：" + issue.Message);
            if (issue.Severity == BuildIssueSeverity.Warning)
                Debug.LogWarning("[MVXRSDK] 出包检查：" + issue.Message);
        }

        private static LocalRegionDataList FindPlayerTiles()
        {
            // 编辑器可浏览备份资产，构建必须与默认 Resources.Load 的键一致。
            var paths = AssetDatabase.FindAssets("t:LocalRegionDataList").Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var matches = paths.Where(IsPlayerTilePath).ToArray();
            if (matches.Length == 1) return AssetDatabase.LoadAssetAtPath<LocalRegionDataList>(matches[0]);
            string expected = LocalRegionDataList.ResourcesLoadPath;
            if (matches.Length > 1)
                throw new BuildFailedException($"[MVXRSDK] 地块资源键 {expected} 不唯一：{string.Join("；", matches)}");
            string candidates = paths.Length == 0 ? "无" : string.Join("；", paths);
            throw new BuildFailedException($"[MVXRSDK] 缺少可用于Player的 {expected}；现有地块：{candidates}。纯媒体工程可显式关闭空间构建要求。");
        }

        private static bool IsPlayerTilePath(string path) =>
            Path.ChangeExtension(path, null).EndsWith("/Resources/" + LocalRegionDataList.ResourcesLoadPath, StringComparison.OrdinalIgnoreCase) &&
            path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0 &&
            !path.StartsWith("Assets/Editor Default Resources/", StringComparison.OrdinalIgnoreCase) &&
            !path.StartsWith("Assets/StreamingAssets/", StringComparison.OrdinalIgnoreCase);
    }
}
