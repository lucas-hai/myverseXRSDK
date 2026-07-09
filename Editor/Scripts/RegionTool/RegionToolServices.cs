namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 地块工具远端服务的唯一切换点：规格列表/上传接口就绪后，
    /// 把对应字段换成 Http 实现即可，业务代码无需改动。
    /// （SDK 验证的切换点在 SdkAuthServices，属验证面板模块。）
    /// </summary>
    public static class RegionToolServices
    {
        public static IRegionSpecSource SpecSource = new MockRegionSpecSource();
        public static IRegionDataUploader Uploader = new MockRegionDataUploader();
    }
}
