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
            if (SdkAuthStore.IsVerified) return;
            SdkAuthWindow.Open();
        }
    }
}
