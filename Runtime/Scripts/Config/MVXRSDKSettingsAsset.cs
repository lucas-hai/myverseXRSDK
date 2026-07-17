using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace MyVerseXRSDK
{
    /// <summary>
    /// SDK 设置配置文件：环境选择（正式/测试/离线）+ 各环境独立的验证凭据（appId + AccessToken）与验证状态。
    /// 由验证面板（菜单 MyVerse/XRSDK/SDK 验证）自动创建与写入；也可 Project 右键 Create 手建。
    /// 刻意不放 Resources/：凭据仅编辑器工具链使用，避免令牌打进真机包
    /// （运行时需要的环境标记同步写在 LocalRegionDataList.activeEnvironment 上）。
    /// 注意：资产随项目入 git，团队共享凭据、验证一次全员生效。
    /// </summary>
    [CreateAssetMenu(menuName = "MyVerse XR SDK/Settings", fileName = "MVXRSDKSettings")]
    public sealed class MVXRSDKSettingsAsset : ScriptableObject
    {
        /// <summary>正式环境开发者平台地址（内置常量，面板只读显示）。</summary>
        public const string ProductionUrl = "http://developer-platform.myverse.fans";

        /// <summary>测试环境开发者平台地址（内置常量，面板只读显示）。</summary>
        public const string TestUrl = "http://172.0.0.218:3888";

        /// <summary>单环境验证凭据（正式/测试各一份；离线环境无凭据概念）。</summary>
        [Serializable]
        public sealed class EnvCredential
        {
            [Tooltip("游戏 appId（该环境开发者平台 Application 的 _id）")]
            public string appId;

            [Tooltip("AccessToken（该环境开发者平台生成的长期 API 令牌）")]
            public string accessToken;

            [Tooltip("该环境是否已通过 SDK 验证（验证面板写入，勿手改）")]
            public bool verified;
        }

        [Tooltip("当前激活环境（验证面板切换；同步写入地块数据资产供运行时读取）")]
        public SdkEnvironment activeEnvironment = SdkEnvironment.Production;

        public EnvCredential production = new EnvCredential();
        public EnvCredential test = new EnvCredential();

        [Tooltip("临时联调地址覆盖（空 = 用当前环境内置地址）。仅应急用，正式流程勿配")]
        public string serverUrlOverride;

        // ------ 旧版单环境字段：仅作迁移源（SdkAuthStore.MigrateIfNeeded 迁入 test），不再读写 ------

        [HideInInspector] public string serverUrl;
        [HideInInspector] public string appId;
        [HideInInspector, FormerlySerializedAs("secretKey")] public string accessToken;
        [HideInInspector] public bool verified;

        /// <summary>取指定环境的服务地址（override 优先）；Offline 无地址返回 null。</summary>
        public string GetServerUrl(SdkEnvironment env)
        {
            if (env == SdkEnvironment.Offline) return null;
            if (!string.IsNullOrWhiteSpace(serverUrlOverride)) return serverUrlOverride.Trim().TrimEnd('/');
            return env == SdkEnvironment.Production ? ProductionUrl : TestUrl;
        }

        /// <summary>取指定环境的凭据；Offline 返回 null（无凭据概念）。</summary>
        public EnvCredential GetCredential(SdkEnvironment env)
        {
            switch (env)
            {
                case SdkEnvironment.Production: return production;
                case SdkEnvironment.Test:       return test;
                default:                        return null;
            }
        }
    }
}
