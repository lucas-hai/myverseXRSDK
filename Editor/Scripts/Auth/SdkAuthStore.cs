using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>SDK 设置资产的定位/创建/验证状态查询（Editor 专用；IsVerified/FindSettings 是对外模块契约）。</summary>
    public static class SdkAuthStore
    {
        public const string DefaultAssetDir = "Assets/MVXRSDK";
        public const string DefaultAssetPath = "Assets/MVXRSDK/MVXRSDKSettings.asset";

        /// <summary>全工程查找设置资产（不要求固定路径），不存在返回 null。</summary>
        public static MVXRSDKSettingsAsset FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:MVXRSDKSettingsAsset");
            if (guids.Length == 0) return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<MVXRSDKSettingsAsset>(path);
        }

        /// <summary>查找设置资产，不存在则在默认路径创建。</summary>
        public static MVXRSDKSettingsAsset GetOrCreateSettings()
        {
            var settings = FindSettings();
            if (settings != null) return settings;

            if (!AssetDatabase.IsValidFolder(DefaultAssetDir))
                AssetDatabase.CreateFolder("Assets", "MVXRSDK");
            settings = ScriptableObject.CreateInstance<MVXRSDKSettingsAsset>();
            AssetDatabase.CreateAsset(settings, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MVXRSDK] 已创建 SDK 设置文件：{DefaultAssetPath}");
            return settings;
        }

        /// <summary>当前项目是否已通过 SDK 验证（其他模块的拦截判据）。</summary>
        public static bool IsVerified
        {
            get
            {
                var settings = FindSettings();
                return settings != null && settings.verified;
            }
        }

        /// <summary>
        /// 取远端接口调用上下文（服务地址/appId/AccessToken）。未验证或凭据缺失返回 false 并给出原因，
        /// 供规格拉取/上传等受保护接口在发请求前统一拦截。
        /// </summary>
        public static bool TryGetApiContext(out string serverUrl, out string appId, out string accessToken, out string error)
        {
            serverUrl = null;
            appId = null;
            accessToken = null;
            var settings = FindSettings();
            if (settings == null || !settings.verified)
            {
                error = "尚未完成 SDK 验证，请先通过 Tools/MyVerse XRSDK/SDK 验证";
                return false;
            }
            if (string.IsNullOrWhiteSpace(settings.appId) || string.IsNullOrWhiteSpace(settings.accessToken))
            {
                error = "SDK 设置中的 appId 或 AccessToken 为空，请重新验证";
                return false;
            }
            serverUrl = settings.ResolveServerUrl();
            appId = settings.appId.Trim();
            accessToken = settings.accessToken.Trim();
            error = null;
            return true;
        }
    }
}
