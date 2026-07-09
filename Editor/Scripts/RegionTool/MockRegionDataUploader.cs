using System;
using UnityEditor;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// Mock 上传：默认直接成功；可用调试菜单开"模拟失败"验证报错与不落盘路径。
    /// 【临时假数据】正式接入后端上传接口时整类删除（连同 RegionToolDebugMenu），换 HttpRegionDataUploader。
    /// </summary>
    public sealed class MockRegionDataUploader : IRegionDataUploader
    {
        /// <summary>调试开关：Tools/MyVerse XRSDK/调试/模拟上传失败。</summary>
        public static bool SimulateFailure;

        public void Upload(LocalRegionData data, Action onDone, Action<string> onError)
        {
            if (SimulateFailure)
            {
                onError?.Invoke("模拟上传失败（调试开关开启：Tools/MyVerse XRSDK/调试/模拟上传失败）");
                return;
            }
            onDone?.Invoke();
        }
    }

    /// <summary>模拟上传失败的调试菜单开关。</summary>
    internal static class RegionToolDebugMenu
    {
        private const string MenuPath = "Tools/MyVerse XRSDK/调试/模拟上传失败";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            MockRegionDataUploader.SimulateFailure = !MockRegionDataUploader.SimulateFailure;
            Menu.SetChecked(MenuPath, MockRegionDataUploader.SimulateFailure);
        }
    }
}
