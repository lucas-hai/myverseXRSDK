using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 出包前地块环境校验：运行时按 LocalRegionDataList.activeEnvironment（构建期快照）取地块组，
    /// 编辑器切环境调试后忘切回就会把错误环境打进包——在这里拦一道。
    /// 交互模式弹窗确认（可取消出包）；批处理/CI 只告警不阻塞。工程未使用地块功能（无资产）不打扰。
    /// </summary>
    internal sealed class RegionEnvironmentBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var dataList = RegionDataMigration.FindDataList();
            if (dataList == null) return;

            var env = dataList.activeEnvironment;
            var envName = SdkAuthStore.EnvDisplayName(env);

            // 0) 两资产环境标记不一致：settings 被绕过面板直接改（Inspector 手改），dataList 没同步——
            //    运行时只认 dataList，先把分歧摆出来
            var settings = SdkAuthStore.FindSettings();
            if (settings != null && settings.activeEnvironment != env)
            {
                Confirm($"设置资产的激活环境（{SdkAuthStore.EnvDisplayName(settings.activeEnvironment)}）与地块数据资产（{envName}）不一致——" +
                        "运行时以地块数据资产为准。请打开 SDK 验证面板重新选择环境完成同步。");
            }

            // 1) 非正式环境出包：大概率是切环境调试后忘了切回
            if (env != SdkEnvironment.Production)
            {
                Confirm($"地块数据激活环境为【{envName}】（非正式环境），真机将使用该环境的地块组。\n" +
                        "若是调试后忘了切回，请取消出包并在 SDK 验证面板切回正式环境。");
            }

            var set = dataList.GetSet(env);

            // 2) 激活组缺失/空：运行时无匹配（环境切了但该环境没配地块）——告警不拦（可能本包不用地块）
            if (set == null || set.entries.Count == 0)
            {
                Debug.LogWarning($"[MVXRSDK] 出包检查：激活环境（{envName}）无地块条目，" +
                                 "运行时 Region 对齐/地块显示不生效（未使用地块功能可忽略）");
                return;
            }

            // 3) 封签无效：运行时会整组拒用，出包前必须知道
            if (!RegionDataSeal.Verify(set))
            {
                Confirm($"激活环境（{envName}）地块组封签无效（被绕过编辑器修改，或迁移后未补签），" +
                        "运行时将整组拒用（对齐/地块显示不生效）。\n" +
                        "请取消出包并在 SDK 验证面板执行\"重新封签\"。");
            }
        }

        // 交互模式：弹窗确认，选"取消出包"抛 BuildFailedException 中止构建；批处理/CI：告警不阻塞
        private static void Confirm(string message)
        {
            if (Application.isBatchMode)
            {
                Debug.LogWarning("[MVXRSDK] 出包检查：" + message);
                return;
            }
            if (!EditorUtility.DisplayDialog("MVXRSDK 出包检查", message + "\n\n确认继续出包？", "继续出包", "取消出包"))
                throw new BuildFailedException("[MVXRSDK] 出包检查未通过（用户取消）：" + message);
        }
    }
}
