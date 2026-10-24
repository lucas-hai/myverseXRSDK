using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>组件菜单的结果出口；SDK诊断继续使用自身Console日志，不在这里重复转发。</summary>
    internal static class DemoConsole
    {
        internal static bool RequirePlay(Object context)
        {
            if (Application.isPlaying) return true;
            Debug.LogWarning("请先进入Play；编辑态不创建SDK客户端或媒体资源。", context);
            return false;
        }

        internal static void Result(Object context, string action, SdkResult result)
        {
            if (result.Success) Debug.Log(action + "成功", context);
            else Debug.LogError($"{action}失败：{result.Code}；{result.Message}", context);
        }
    }
}
