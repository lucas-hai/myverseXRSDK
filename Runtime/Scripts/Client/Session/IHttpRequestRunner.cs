using System;

namespace MyVerseXRSDK
{
    internal readonly struct HttpReply
    {
        internal SdkResult Result { get; }
        internal string Body { get; }
        internal long StatusCode { get; }
        internal HttpReply(SdkResult result, string body, long statusCode = 200)
        {
            Result = result;
            Body = body ?? string.Empty;
            StatusCode = statusCode;
        }
    }

    internal interface IHttpRequestRunner : IDisposable
    {
        IDisposable Begin(string method, string url, byte[] body, Action<HttpReply> completed);
    }
}
