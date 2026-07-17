namespace MyVerseXRSDK
{
    /// <summary>
    /// SDK 开发者平台环境。编辑器侧事实源为 <see cref="MVXRSDKSettingsAsset.activeEnvironment"/>（验证面板切换），
    /// 运行时事实源为 <see cref="LocalRegionDataList.activeEnvironment"/>（面板/编辑器同步写入的构建期快照——
    /// 设置资产不进包，运行时读不到）。
    /// </summary>
    public enum SdkEnvironment
    {
        /// <summary>正式环境（默认）。</summary>
        Production = 0,

        /// <summary>测试环境（内网联调）。</summary>
        Test = 1,

        /// <summary>离线环境：无开发者平台。验证/上传不可用，地块仅本地保存（无防篡改封签）。</summary>
        Offline = 2,
    }
}
