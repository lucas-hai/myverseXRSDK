using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// SDK 设置配置文件：保存 SDK 验证凭据与验证状态。
    /// 由验证面板（Tools/MyVerse XRSDK/SDK 验证）自动创建与写入；也可 Project 右键 Create 手建。
    /// 刻意不放 Resources/：凭据仅编辑器工具链使用，避免密钥打进真机包。
    /// 注意：资产随项目入 git，团队共享凭据、验证一次全员生效。
    /// </summary>
    [CreateAssetMenu(menuName = "MyVerse XR SDK/Settings", fileName = "MVXRSDKSettings")]
    public sealed class MVXRSDKSettingsAsset : ScriptableObject
    {
        [Tooltip("SDK 验证 appid")]
        public string appId;

        [Tooltip("SDK 验证密钥")]
        public string secretKey;

        [Tooltip("是否已通过 SDK 验证（验证面板写入，勿手改）")]
        public bool verified;
    }
}
