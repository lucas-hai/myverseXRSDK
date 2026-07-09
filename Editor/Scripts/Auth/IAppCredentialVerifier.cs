using System;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// SDK 验证服务抽象。默认实现 <see cref="HttpAccessTokenVerifier"/>
    /// （开发者平台 POST /api/auth/accessToken/verify），切换点在 <see cref="SdkAuthServices.Verifier"/>。
    /// </summary>
    public interface IAppCredentialVerifier
    {
        /// <param name="serverUrl">开发者平台服务地址（如 http://192.168.1.220:7888）</param>
        /// <param name="appId">游戏 appId（Application 的 _id），仅本地非空校验并随通过一起保存</param>
        /// <param name="accessToken">开发者平台生成的长期 API 令牌</param>
        /// <param name="onComplete">(是否通过, 失败原因——通过时为 null)</param>
        void Verify(string serverUrl, string appId, string accessToken, Action<bool, string> onComplete);
    }
}
