namespace MyVerseXRSDK.Editor
{
    /// <summary>验证模块远端服务的唯一切换点（单测/联调可注入替身实现）。</summary>
    public static class SdkAuthServices
    {
        public static IAppCredentialVerifier Verifier = new HttpAccessTokenVerifier();
    }
}
