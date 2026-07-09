namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 地块工具远端服务的唯一切换点（单测/联调可注入替身实现）。
    /// （SDK 验证的切换点在 SdkAuthServices，属验证面板模块。）
    /// </summary>
    public static class RegionToolServices
    {
        public static IRegionSpecSource SpecSource = new HttpRegionSpecSource();
        public static IRegionDataUploader Uploader = new HttpRegionDataUploader();
    }
}
