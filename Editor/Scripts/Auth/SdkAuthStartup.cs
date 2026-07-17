using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 导入 SDK / 打开工程时自动弹验证面板（未验证时）。
    /// 每个编辑器会话最多自动弹一次（SessionState）——域重载（每次编译脚本）不重复骚扰；
    /// 批处理/CI 无人值守不弹窗。验证通过后永不自动弹，可从菜单手动打开。
    /// </summary>
    [InitializeOnLoad]
    internal static class SdkAuthStartup
    {
        private const string SessionKey = "MVXRSDK_AuthPromptShown";

        static SdkAuthStartup()
        {
            if (Application.isBatchMode) return;
            // 延迟到首帧 update：域重载期间不能安全开窗口
            EditorApplication.update += ShowOnce;
        }

        private static void ShowOnce()
        {
            EditorApplication.update -= ShowOnce;
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            // 旧版地块数据迁移挂启动钩子（幂等）：不依赖用户打开某个面板才触发，
            // 否则"只出包不开面板"的工程会把未迁移数据（激活组为空）带进包
            RegionDataMigration.MigrateIfNeeded(RegionDataMigration.FindDataList());
            // 离线环境无验证概念，不自动弹窗骚扰
            if (SdkAuthStore.ActiveEnvironment == SdkEnvironment.Offline) return;
            if (SdkAuthStore.IsVerified) return;
            SdkAuthWindow.Open();
        }
    }
}
