using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// SDK 设置资产的定位/创建/环境与验证状态查询（Editor 专用；IsVerified/FindSettings/TryGetApiContext 是对外模块契约）。
    /// 环境模型：验证状态与凭据按环境独立（正式/测试各一份，离线无凭据）；
    /// 旧版单环境字段在 FindSettings 时惰性迁入测试环境（幂等，与既有凭据事实一致——旧默认地址即测试环境）。
    /// </summary>
    public static class SdkAuthStore
    {
        public const string DefaultAssetDir = "Assets/MVXRSDK";
        public const string DefaultAssetPath = "Assets/MVXRSDK/MVXRSDKSettings.asset";

        /// <summary>全工程查找设置资产（不要求固定路径），不存在返回 null。旧版字段惰性迁移在此收口。</summary>
        public static MVXRSDKSettingsAsset FindSettings()
        {
            var guids = AssetDatabase.FindAssets("t:MVXRSDKSettingsAsset");
            if (guids.Length == 0) return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var settings = AssetDatabase.LoadAssetAtPath<MVXRSDKSettingsAsset>(path);
            MigrateIfNeeded(settings);
            return settings;
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

        /// <summary>当前激活环境（无设置资产时默认正式环境）。</summary>
        public static SdkEnvironment ActiveEnvironment
        {
            get
            {
                var settings = FindSettings();
                return settings == null ? SdkEnvironment.Production : settings.activeEnvironment;
            }
        }

        /// <summary>
        /// 当前激活环境是否已通过 SDK 验证（其他模块的拦截判据）。
        /// 离线环境恒 false——远端功能（规格拉取/上传）在离线下本就不可用，调用方需先判环境给出针对性提示。
        /// </summary>
        public static bool IsVerified
        {
            get
            {
                var settings = FindSettings();
                if (settings == null) return false;
                var cred = settings.GetCredential(settings.activeEnvironment);
                return cred != null && cred.verified;
            }
        }

        /// <summary>
        /// 取远端接口调用上下文（当前激活环境的服务地址/appId/AccessToken）。
        /// 离线环境、未验证或凭据缺失返回 false 并给出原因，供规格拉取/上传等受保护接口在发请求前统一拦截。
        /// </summary>
        public static bool TryGetApiContext(out string serverUrl, out string appId, out string accessToken, out string error)
        {
            serverUrl = null;
            appId = null;
            accessToken = null;
            var settings = FindSettings();
            if (settings == null)
            {
                error = "尚未完成 SDK 验证，请先通过菜单 MyVerse/XRSDK/SDK 验证";
                return false;
            }
            var env = settings.activeEnvironment;
            if (env == SdkEnvironment.Offline)
            {
                error = "当前为离线环境，远端接口不可用（可在验证面板切换到正式/测试环境）";
                return false;
            }
            var cred = settings.GetCredential(env);
            if (cred == null || !cred.verified)
            {
                error = $"当前环境（{EnvDisplayName(env)}）尚未完成 SDK 验证，请先通过菜单 MyVerse/XRSDK/SDK 验证";
                return false;
            }
            if (string.IsNullOrWhiteSpace(cred.appId) || string.IsNullOrWhiteSpace(cred.accessToken))
            {
                error = $"当前环境（{EnvDisplayName(env)}）的 appId 或 AccessToken 为空，请重新验证";
                return false;
            }
            serverUrl = settings.GetServerUrl(env);
            appId = cred.appId.Trim();
            accessToken = cred.accessToken.Trim();
            error = null;
            return true;
        }

        /// <summary>环境显示名（面板/提示统一用词）。</summary>
        public static string EnvDisplayName(SdkEnvironment env)
        {
            switch (env)
            {
                case SdkEnvironment.Production: return "正式环境";
                case SdkEnvironment.Test:       return "测试环境";
                default:                        return "离线环境";
            }
        }

        // 旧版单环境字段（serverUrl/appId/accessToken/verified）迁入测试环境凭据：
        // 旧默认地址即测试环境（172.0.0.218:3888），归测试组符合凭据事实；幂等（test 已有凭据不重迁）
        private static void MigrateIfNeeded(MVXRSDKSettingsAsset settings)
        {
            if (settings == null) return;
            bool hasLegacy = !string.IsNullOrWhiteSpace(settings.appId) || !string.IsNullOrWhiteSpace(settings.accessToken);
            bool testEmpty = string.IsNullOrWhiteSpace(settings.test.appId) && string.IsNullOrWhiteSpace(settings.test.accessToken);
            if (!hasLegacy || !testEmpty) return;

            settings.test.appId = settings.appId;
            settings.test.accessToken = settings.accessToken;
            settings.test.verified = settings.verified;
            // 清空迁移源，避免再次触发与双份凭据并存
            settings.appId = null;
            settings.accessToken = null;
            settings.verified = false;
            settings.serverUrl = null;
            EditorUtility.SetDirty(settings);
            Debug.Log("[MVXRSDK] 旧版单环境凭据已迁入测试环境；正式环境需在验证面板重新验证");
        }
    }
}
