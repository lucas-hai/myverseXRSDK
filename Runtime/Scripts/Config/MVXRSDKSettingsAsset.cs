using UnityEngine;
using UnityEngine.Serialization;

namespace MyVerseXRSDK
{
    /// <summary>
    /// SDK 设置配置文件：保存开发者平台地址与验证凭据（appId + AccessToken）及验证状态。
    /// 由验证面板（Tools/MyVerse XRSDK/SDK 验证）自动创建与写入；也可 Project 右键 Create 手建。
    /// 刻意不放 Resources/：凭据仅编辑器工具链使用，避免令牌打进真机包。
    /// 注意：资产随项目入 git，团队共享凭据、验证一次全员生效。
    /// </summary>
    [CreateAssetMenu(menuName = "MyVerse XR SDK/Settings", fileName = "MVXRSDKSettings")]
    public sealed class MVXRSDKSettingsAsset : ScriptableObject
    {
        /// <summary>开发者平台默认地址（联调临时环境，正式环境就绪后在验证面板/设置资产改）。</summary>
        public const string DefaultServerUrl = "http://192.168.1.220:7888";

        [Tooltip("开发者平台服务地址，空则用默认值")]
        public string serverUrl = DefaultServerUrl;

        [Tooltip("游戏 appId（开发者平台 Application 的 _id）")]
        public string appId;

        [Tooltip("AccessToken（开发者平台生成的长期 API 令牌）")]
        [FormerlySerializedAs("secretKey")]
        public string accessToken;

        [Tooltip("是否已通过 SDK 验证（验证面板写入，勿手改）")]
        public bool verified;

        /// <summary>规整后的服务地址：空回退默认值、去掉末尾斜杠，便于拼接 /api/... 路径。</summary>
        public string ResolveServerUrl()
        {
            var url = string.IsNullOrWhiteSpace(serverUrl) ? DefaultServerUrl : serverUrl.Trim();
            return url.TrimEnd('/');
        }
    }
}
