using System;
using System.Collections;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>HTTP 请求结果（同步字段，由协程在完成回调里 invoke）。</summary>
    internal class HttpResult
    {
        public bool TimedOut;
        public int StatusCode;
        public string Body;
        public string Location;
        public string ErrorMsg;
    }

    /// <summary>
    /// 抽象 HTTP 传输层，便于测试注入 fake transport。
    /// **协程化签名**：调用方 yield return 等待 IEnumerator 跑完，通过 onDone 回调拿结果——
    /// 这样不阻塞 Unity 主线程（基于 UnityWebRequest 的实现是协程友好的）。
    /// </summary>
    internal interface IHttpTransport
    {
        // 播控协议：整 URL（含 ?authToken=）直接发请求；mediamtx 从 URL query 解析鉴权，不加 Authorization 头
        IEnumerator Post(string url, string contentType, string body, int timeoutSec, Action<HttpResult> onDone);
        IEnumerator Delete(string url, int timeoutSec, Action<HttpResult> onDone);
    }

    internal sealed class WhipResponse
    {
        public bool Success;
        public string SdpAnswer;
        public string Location;
        public MVXRSDKErrorCode ErrorCode;
        public string ErrorMsg;
    }

    /// <summary>
    /// WHIP 协议客户端：POST SDP offer 拿 SDP answer + Location；DELETE 停流。
    /// 状态码映射到 <see cref="MVXRSDKErrorCode"/>（4xxx 推流段）。
    ///
    /// 协程化：Post 与 Delete 均返回 IEnumerator，由调用方通过 MonoSystem.Start_Coroutine
    /// 驱动；过程中 yield 等 UnityWebRequest 完成，**不阻塞主线程**。
    ///
    /// DELETE 内置 3 次重试（间隔 1s / 3s / 8s），200/204/404 视为成功；
    /// 调用方一般通过 Start_Coroutine 启动 Delete 协程后立刻返回（fire-and-forget），无需等待。
    /// </summary>
    internal class WhipClient
    {
        private readonly IHttpTransport m_Transport;

        public WhipClient(IHttpTransport transport)
        {
            m_Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        /// <summary>
        /// 协程化 WHIP POST。完成时通过 <paramref name="onDone"/> 回调返回 <see cref="WhipResponse"/>。
        /// </summary>
        public IEnumerator Post(string whipUrl, string sdpOffer, Action<WhipResponse> onDone)
        {
            HttpResult raw = null;
            yield return m_Transport.Post(whipUrl, "application/sdp", sdpOffer, StreamConfig.Active.WhipHttpTimeoutSec, r => raw = r);

            if (raw == null)
            {
                // 极端情况：transport 没回调；按超时处理
                onDone?.Invoke(new WhipResponse
                {
                    Success = false,
                    ErrorCode = MVXRSDKErrorCode.WhipPostFailed,
                    ErrorMsg = "transport returned no result"
                });
                yield break;
            }

            if (raw.TimedOut)
            {
                onDone?.Invoke(new WhipResponse
                {
                    Success = false,
                    ErrorCode = MVXRSDKErrorCode.WhipPostFailed,
                    ErrorMsg = $"WHIP POST timeout: {raw.ErrorMsg}"
                });
                yield break;
            }

            onDone?.Invoke(BuildResponse(raw));
        }

        /// <summary>
        /// 协程化 WHIP DELETE，内置重试。Caller 一般 fire-and-forget：
        /// <c>MonoSystem.Start_Coroutine(whipClient.Delete(loc));</c>
        /// </summary>
        public IEnumerator Delete(string location)
        {
            if (string.IsNullOrEmpty(location)) yield break;
            // 取 snapshot：协程期间 cfg 被 Apply 也不会影响当前重试节奏
            var delays = StreamConfig.Active.DeleteRetryDelaysMs;
            int timeoutSec = StreamConfig.Active.WhipHttpTimeoutSec;
            if (delays == null || delays.Length == 0)
            {
                delays = new[] { 1000, 3000, 8000 };
            }

            for (int attempt = 0; attempt < delays.Length; attempt++)
            {
                HttpResult raw = null;
                yield return m_Transport.Delete(location, timeoutSec, r => raw = r);

                int code = raw?.StatusCode ?? 0;
                // 200/204/404 均视为成功——404 表示 mediamtx 已清掉 session，目的已达
                if (code == 200 || code == 204 || code == 404)
                {
                    if (attempt > 0)
                        MVXRSDKLog.Info($"WhipClient.Delete: 成功（重试第 {attempt + 1} 次）location={location} code={code}");
                    yield break;
                }

                MVXRSDKLog.Warning(
                    $"WhipClient.Delete: 失败 attempt={attempt + 1}/{delays.Length} " +
                    $"location={location} code={code} msg={raw?.ErrorMsg}");

                // 最后一次失败不再等
                if (attempt < delays.Length - 1)
                {
                    yield return new WaitForSeconds(delays[attempt] / 1000f);
                }
            }
            // 全部重试耗尽 = mediamtx 真正不可达，升 Error；mediamtx 侧需依赖 stream 空闲超时清理
            MVXRSDKLog.Error($"WhipClient.Delete: {delays.Length} 次重试均失败 location={location}（mediamtx 不可达，依赖空闲超时清理 session）");
        }

        private static WhipResponse BuildResponse(HttpResult raw)
        {
            switch (raw.StatusCode)
            {
                case 201:
                    return new WhipResponse
                    {
                        Success = true,
                        SdpAnswer = raw.Body,
                        Location = raw.Location
                    };
                case 401:
                case 403:
                    return new WhipResponse
                    {
                        Success = false,
                        ErrorCode = MVXRSDKErrorCode.WhipAuthFailed,
                        ErrorMsg = $"WHIP auth failed (HTTP {raw.StatusCode})"
                    };
                case 404:
                    return new WhipResponse
                    {
                        Success = false,
                        ErrorCode = MVXRSDKErrorCode.WhipStreamNotFound,
                        ErrorMsg = "WHIP stream not found (HTTP 404)"
                    };
                case 415:
                    return new WhipResponse
                    {
                        Success = false,
                        ErrorCode = MVXRSDKErrorCode.CodecNegotiationFailed,
                        ErrorMsg = "WHIP codec not supported (HTTP 415)"
                    };
                default:
                    return new WhipResponse
                    {
                        Success = false,
                        ErrorCode = MVXRSDKErrorCode.WhipPostFailed,
                        // 带上底层 errorMsg 便于定位（HTTP 0 = DNS/连接失败，5xx = 服务端异常等）
                        ErrorMsg = $"WHIP POST failed (HTTP {raw.StatusCode}) {raw.ErrorMsg}"
                    };
            }
        }
    }
}
