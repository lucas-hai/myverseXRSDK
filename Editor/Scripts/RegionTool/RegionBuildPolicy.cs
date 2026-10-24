namespace MyVerseXRSDK.Editor
{
    internal enum BuildIssueSeverity { None, Warning, Error }

    internal readonly struct RegionBuildIssue
    {
        internal BuildIssueSeverity Severity { get; }
        internal string Message { get; }
        internal RegionBuildIssue(BuildIssueSeverity severity, string message) { Severity = severity; Message = message; }
    }

    /// <summary>同一份显式配置同时服务CI和交互构建，不因运行模式自动放行错误。</summary>
    internal static class RegionBuildPolicy
    {
        internal static RegionBuildIssue Evaluate(MVXRSDKSettingsAsset settings, LocalRegionDataList data)
        {
            if (settings != null && !settings.requireSpatialDataForBuild) return default;
            if (data == null) return Error("空间构建需要LocalRegionData资产；不使用空间时请显式关闭设置中的空间构建要求");
            var environment = data.activeEnvironment;
            if (settings != null && settings.activeEnvironment != environment)
                return Error("SDK设置与地块资产的激活环境不一致，请先同步环境");
            var set = data.GetSet(environment);
            if (set == null || set.entries == null || set.entries.Count == 0)
                return Error("空间构建的激活地块组不存在或为空");
            if (!RegionDataSeal.Verify(set)) return Error("激活地块组封签无效，请通过编辑工具重新上传并封签");
            if (environment == SdkEnvironment.Production) return default;
            string message = "本次构建使用非正式地块环境：" + SdkAuthStore.EnvDisplayName(environment);
            return new RegionBuildIssue(settings != null && settings.failOnNonProductionBuild
                ? BuildIssueSeverity.Error : BuildIssueSeverity.Warning, message);
        }

        private static RegionBuildIssue Error(string message) => new RegionBuildIssue(BuildIssueSeverity.Error, message);
    }
}
