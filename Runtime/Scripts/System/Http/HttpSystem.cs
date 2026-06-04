using System;
using System.Collections;
using UnityEngine.Networking;

namespace MyVerseXRSDK
{
    /// <summary>
    /// Http 发送数据的回调委托
    /// </summary>
    internal delegate void HttpSendDataCallBack(HttpCallBackArgs args);

    internal enum HttpRequestType
    {
        GET,
        POST,
        PUT
    }

    /// <summary>
    /// Http 请求统一入口（无状态、支持任意并发）。
    ///
    /// v2 PR-9：body 入参从 Dictionary&lt;string, object&gt; 改为 byte[] —— SDK 不再依赖
    /// LitJson 序列化字典；调用方负责把 POCO 自己用 Newtonsoft.Json + UTF8 编码后传入。
    /// </summary>
    internal static class HttpSystem
    {
        private const int REQUEST_TIMEOUT_SEC = 10;

        /// <summary>
        /// 发送 HTTP 请求
        /// </summary>
        /// <param name="body">POST/PUT 请求体（UTF8 编码的 JSON 字节）；GET 时忽略。</param>
        /// <param name="isLog">是否输出请求开始与成功日志（失败日志始终输出，便于排查）</param>
        public static void SendData(
            string url,
            HttpSendDataCallBack callBack,
            HttpRequestType requestType = HttpRequestType.GET,
            byte[] body = null,
            bool isLog = true)
        {
            url = url?.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MVXRSDKLog.Error("HttpSystem:URL 为空");
                callBack?.Invoke(new HttpCallBackArgs { HasError = true, Value = "url is empty" });
                return;
            }

            UnityWebRequest request = BuildRequest(url, requestType, body);
            if (isLog) MVXRSDKLog.Info($"HttpSystem:[{requestType}] {url}");
            MonoSystem.Start_Coroutine(SendCoroutine(request, callBack, isLog));
        }

        private static UnityWebRequest BuildRequest(string url, HttpRequestType type, byte[] body)
        {
            UnityWebRequest req;
            switch (type)
            {
                case HttpRequestType.GET:
                    req = UnityWebRequest.Get(url);
                    break;

                case HttpRequestType.POST:
                    req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
                    {
                        uploadHandler = new UploadHandlerRaw(body ?? Array.Empty<byte>()),
                        downloadHandler = new DownloadHandlerBuffer()
                    };
                    break;

                case HttpRequestType.PUT:
                    req = UnityWebRequest.Put(url, body ?? Array.Empty<byte>());
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }

            req.timeout = REQUEST_TIMEOUT_SEC;
            req.SetRequestHeader("Authorization", "");
            req.SetRequestHeader("Token", "");
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }

        private static IEnumerator SendCoroutine(UnityWebRequest request, HttpSendDataCallBack callBack, bool isLog)
        {
            using (request)
            {
                yield return request.SendWebRequest();

                var args = new HttpCallBackArgs();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    args.HasError = true;
                    args.Value = request.error;
                    // 失败日志始终输出
                    MVXRSDKLog.Warning($"HttpSystem:请求失败 {request.url} → {request.error}");
                }
                else
                {
                    args.HasError = false;
                    args.Value = request.downloadHandler.text;
                    if (isLog) MVXRSDKLog.Debug($"HttpSystem:请求成功 {request.url}");
                }

                try
                {
                    callBack?.Invoke(args);
                }
                catch (Exception ex)
                {
                    MVXRSDKLog.Error($"HttpSystem:回调异常 → {ex.Message}");
                }
            }
        }
    }
}
