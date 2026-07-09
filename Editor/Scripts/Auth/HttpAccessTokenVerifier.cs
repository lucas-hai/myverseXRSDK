using System;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// AccessToken 校验器：POST /api/auth/accessToken/verify（公开接口，令牌走请求体不走鉴权头）。
    /// appId 只做本地非空校验——归属校验发生在上传地块接口（错误码 10007），此处无法提前验证。
    /// 无效令牌服务端不报业务错误（code 恒 200），以 data.valid=false + reason 表达。
    /// </summary>
    public sealed class HttpAccessTokenVerifier : IAppCredentialVerifier
    {
        [Serializable]
        private class VerifyRequest
        {
            public string accessToken;
        }

        [Serializable]
        private class VerifyEnvelope
        {
            public int code;
            public string message;
            public VerifyData data;
        }

        [Serializable]
        private class VerifyData
        {
            public bool valid;
            public string reason;
            public string userId;
            public long expiresAt;
        }

        public void Verify(string serverUrl, string appId, string accessToken, Action<bool, string> onComplete)
        {
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(accessToken))
            {
                onComplete?.Invoke(false, "appId 与 AccessToken 不能为空");
                return;
            }
            var url = $"{serverUrl.TrimEnd('/')}/api/auth/accessToken/verify";
            var body = JsonUtility.ToJson(new VerifyRequest { accessToken = accessToken.Trim() });
            EditorHttp.PostJson(url, bearerToken: null, body,
                onDone: json =>
                {
                    if (!EditorHttp.TryCheckEnvelope(json, out var envelopeError))
                    {
                        onComplete?.Invoke(false, envelopeError);
                        return;
                    }
                    var envelope = JsonUtility.FromJson<VerifyEnvelope>(json);
                    if (envelope.data == null)
                    {
                        onComplete?.Invoke(false, "响应缺少校验结果 data");
                        return;
                    }
                    if (!envelope.data.valid)
                    {
                        onComplete?.Invoke(false, DescribeReason(envelope.data.reason));
                        return;
                    }
                    onComplete?.Invoke(true, null);
                },
                onError: error => onComplete?.Invoke(false, error));
        }

        // reason 枚举 → 中文提示（开发者 API 文档 §三：invalid / expired / revoked）
        private static string DescribeReason(string reason)
        {
            switch (reason)
            {
                case "expired": return "AccessToken 已过期，请到开发者平台重新生成";
                case "revoked": return "AccessToken 已被重新生成或作废，请使用最新令牌";
                default: return "AccessToken 无效（签名/格式错误或非 API 令牌）";
            }
        }
    }
}
