using System;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// Mock 验证器：任意非空 appid+密钥即通过（本地开发不被接口进度阻塞）。
    /// 【临时假数据】正式接入后端验证接口时整类删除，换 HttpCredentialVerifier。
    /// </summary>
    public sealed class MockCredentialVerifier : IAppCredentialVerifier
    {
        public void Verify(string appId, string secretKey, Action<bool, string> onComplete)
        {
            bool ok = !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(secretKey);
            onComplete?.Invoke(ok, ok ? null : "appid 与验证密钥不能为空");
        }
    }
}
