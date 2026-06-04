using System;
using System.Collections;
using System.Text;
using UnityEngine.Networking;

namespace MyVerseXRSDK
{
    /// <summary>
    /// HTTP 传输实现，基于 <see cref="UnityWebRequest"/> + 协程（不阻塞主线程）。
    /// </summary>
    /// <remarks>
    /// **关于死锁顾虑（已澄清）**：早期版本曾用 <c>HttpWebRequest.GetResponse()</c> 同步阻塞，
    /// 理由是"避免 UnityWebRequest 死锁"。但 UnityWebRequest 标准协程写法
    /// （<c>yield return req.SendWebRequest()</c>）不会死锁——只有
    /// <c>while(!op.isDone) Thread.Sleep(10)</c> 这类反协程写法才会。
    /// 同步阻塞 HttpWebRequest 反而会在 WHIP 握手期间把主线程冻 30s。
    ///
    /// **关于代理**：UnityWebRequest 默认走系统代理。开发环境（Clash/V2Ray/杀软）下
    /// 推局域网 mediamtx 可能被代理劫走，表现为 ConnectionError + "Request timeout"。
    /// 排查时确认 Windows 系统代理已关或对 192.168.x.x 做了 bypass。
    ///
    /// **鉴权**：whipUrl 已含 <c>?authToken=</c>，mediamtx 从 query 解析；故不设 Authorization 头。
    /// </remarks>
    internal class UnityWebRequestHttpTransport : IHttpTransport
    {
        public IEnumerator Post(string url, string contentType, string body, int timeoutSec, Action<HttpResult> onDone)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
            // contentType 仅通过 UploadHandlerRaw 设置，避免 UnityWebRequest 部分版本双重 Content-Type 头
            var upload = new UploadHandlerRaw(bytes) { contentType = contentType };
            return SendRequest(
                verb: UnityWebRequest.kHttpVerbPOST,
                url: url,
                uploadHandler: upload,
                acceptHeader: contentType,  // WHIP 期望 response 也是 application/sdp
                logExtra: $"bodyLen={bytes.Length} contentType={contentType}",
                isSuccess: code => code >= 200 && code < 300,
                timeoutSec: timeoutSec,
                onDone: onDone);
        }

        public IEnumerator Delete(string url, int timeoutSec, Action<HttpResult> onDone)
        {
            // 200/204/404 视为成功（WhipClient.Delete 内部也按此处理：mediamtx 已清掉 session）
            return SendRequest(
                verb: UnityWebRequest.kHttpVerbDELETE,
                url: url,
                uploadHandler: null,
                acceptHeader: null,
                logExtra: null,
                isSuccess: code => code == 200 || code == 204 || code == 404,
                timeoutSec: timeoutSec,
                onDone: onDone);
        }

        /// <summary>
        /// 公共 UnityWebRequest 协程：组装 → SendWebRequest → BuildResult → 按 isSuccess 决定 log 级别。
        /// POST / DELETE / 未来 PATCH / PUT 共享此方法。
        /// </summary>
        private static IEnumerator SendRequest(
            string verb, string url, UploadHandler uploadHandler, string acceptHeader,
            string logExtra, Func<int, bool> isSuccess, int timeoutSec, Action<HttpResult> onDone)
        {
            using (var req = new UnityWebRequest(url, verb))
            {
                if (uploadHandler != null) req.uploadHandler = uploadHandler;
                req.downloadHandler = new DownloadHandlerBuffer();
                if (!string.IsNullOrEmpty(acceptHeader)) req.SetRequestHeader("Accept", acceptHeader);
                req.timeout = timeoutSec;
                req.redirectLimit = 0;  // 不主动 follow 30x，由调用方判断状态码

                // 推流重连场景 HTTP 请求反复发送；开始/成功降为 Debug 减噪，失败到 Warning
                MVXRSDKLog.Debug($"HTTP {verb} {url}{(logExtra != null ? " " + logExtra : "")} timeout={timeoutSec}s");

                yield return req.SendWebRequest();

                var result = BuildResult(req);
                bool ok = isSuccess(result.StatusCode);
                string msg = $"HTTP {verb} done: status={result.StatusCode} resultEnum={req.result} error={req.error} bodyLen={(result.Body?.Length ?? 0)} location={result.Location}";
                if (ok) MVXRSDKLog.Debug(msg); else MVXRSDKLog.Warning(msg);
                onDone?.Invoke(result);
            }
        }

        private static HttpResult BuildResult(UnityWebRequest req)
        {
            var result = new HttpResult
            {
                StatusCode = (int)req.responseCode,
                Body = req.downloadHandler != null ? req.downloadHandler.text : null,
                Location = req.GetResponseHeader("Location")
            };

            switch (req.result)
            {
                case UnityWebRequest.Result.Success:
                    // 状态码 / Body / Location 已填好
                    break;
                case UnityWebRequest.Result.ProtocolError:
                    // HTTP 4xx/5xx：状态码有效，仍带 req.error 描述
                    result.ErrorMsg = req.error;
                    break;
                case UnityWebRequest.Result.ConnectionError:
                case UnityWebRequest.Result.DataProcessingError:
                    // 连接级错误（DNS / 拒绝 / 超时 / 系统代理劫持）；status 通常为 0
                    result.ErrorMsg = req.error;
                    if (req.error != null && req.error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.TimedOut = true;
                    }
                    break;
            }
            return result;
        }
    }
}
