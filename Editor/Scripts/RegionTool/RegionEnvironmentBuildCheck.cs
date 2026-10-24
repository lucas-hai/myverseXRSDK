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
            var issue = RegionBuildPolicy.Evaluate(SdkAuthStore.FindSettings(), RegionDataMigration.FindDataList());
            if (issue.Severity == BuildIssueSeverity.Error)
                throw new BuildFailedException("[MVXRSDK] 出包检查未通过：" + issue.Message);
            if (issue.Severity == BuildIssueSeverity.Warning)
                Debug.LogWarning("[MVXRSDK] 出包检查：" + issue.Message);
        }
    }
}
