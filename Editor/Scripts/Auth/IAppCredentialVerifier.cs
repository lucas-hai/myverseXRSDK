using System;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// SDK 验证服务抽象。Http 实现待远端接口定义后补（HttpCredentialVerifier 占位），
    /// 当前默认走 <see cref="MockCredentialVerifier"/>，切换点在 <see cref="SdkAuthServices.Verifier"/>。
    /// 正式接入后端接口时删除所有 Mock 假数据实现。
    /// </summary>
    public interface IAppCredentialVerifier
    {
        /// <param name="onComplete">(是否通过, 失败原因——通过时为 null)</param>
        void Verify(string appId, string secretKey, Action<bool, string> onComplete);
    }
}
