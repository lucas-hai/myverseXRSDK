namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 验证模块远端服务的唯一切换点：验证接口就绪后把 Verifier 换成 Http 实现即可。
    /// 正式接入后端接口时删除 Mock 实现（见 MockCredentialVerifier 注释）。
    /// </summary>
    public static class SdkAuthServices
    {
        public static IAppCredentialVerifier Verifier = new MockCredentialVerifier();
    }
}
