using System;

namespace MyVerseXRSDK.Editor
{
    /// <summary>一次编辑器操作固定的环境、端点与凭据快照。不得把凭据写入日志或报告。</summary>
    public sealed class EditorApiContext
    {
        public SdkEnvironment Environment { get; }
        public string ServerUrl { get; }
        public string AppId { get; }
        public string AccessToken { get; }
        public bool IsRemote => Environment != SdkEnvironment.Offline;

        private EditorApiContext(SdkEnvironment environment, string serverUrl, string appId, string accessToken)
        { Environment = environment; ServerUrl = serverUrl; AppId = appId; AccessToken = accessToken; }

        /// <summary>捕获明确指定的环境；不重新读取激活环境。离线上下文不含凭据，不能用于远端请求。</summary>
        public static EditorApiContext Capture(MVXRSDKSettingsAsset settings, SdkEnvironment environment)
        {
            if (!Enum.IsDefined(typeof(SdkEnvironment), environment)) throw new ArgumentOutOfRangeException(nameof(environment));
            if (environment == SdkEnvironment.Offline) return new EditorApiContext(environment, null, null, null);
            if (settings == null) throw new InvalidOperationException("尚未创建SDK设置，请先完成SDK验证");
            var credential = settings.GetCredential(environment);
            if (credential == null || !credential.verified || string.IsNullOrWhiteSpace(credential.appId) || string.IsNullOrWhiteSpace(credential.accessToken))
                throw new InvalidOperationException("指定环境尚未完成SDK验证或凭据不完整");
            string server = settings.GetServerUrl(environment);
            if (!Uri.TryCreate(server, UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("指定环境的服务地址无效");
            return new EditorApiContext(environment, server.TrimEnd('/'), credential.appId.Trim(), credential.accessToken.Trim());
        }

        public override string ToString() => "EditorApiContext(" + Environment + ")";
    }
}
