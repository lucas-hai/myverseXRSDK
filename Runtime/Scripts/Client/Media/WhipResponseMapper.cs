using System;

namespace MyVerseXRSDK
{
    internal static class WhipResponseMapper
    {
        internal static SdkResult MapDelete(long status, bool connectionFailed)
            => !connectionFailed && (status == 200 || status == 204 || status == 404) ? SdkResult.Ok :
                SdkResult.Fail(SdkErrorCode.TransportFailed, "WHIP DELETE未成功，HTTP状态码：" + status);

        internal static WhipReply Map(int status, string body, string location, string url, bool connectionFailed)
        {
            string resolvedLocation = null;
            if (!string.IsNullOrWhiteSpace(location) && Uri.TryCreate(url, UriKind.Absolute, out var origin) &&
                Uri.TryCreate(origin, location, out var resolved) &&
                (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps))
                resolvedLocation = resolved.AbsoluteUri;
            if (status == 201 && !connectionFailed)
            {
                var result = string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(resolvedLocation)
                    ? SdkResult.Fail(SdkErrorCode.InvalidResponse, "WHIP创建响应缺少有效SDP或Location") : SdkResult.Ok;
                return new WhipReply(result, body, resolvedLocation);
            }
            bool retryable = connectionFailed || status == 0 || status >= 500 || status == 408;
            return new WhipReply(SdkResult.Fail(retryable ? SdkErrorCode.TransportFailed : SdkErrorCode.RemoteRejected,
                "WHIP请求失败，HTTP状态码：" + status), null, resolvedLocation, retryable);
        }
    }
}
