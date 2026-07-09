using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// Editor 工具链专用异步 HTTP：UnityWebRequest + EditorApplication.update 轮询驱动，
    /// 不阻塞主线程，回调在主线程执行。平台响应为统一信封 { code, message, data }
    /// （HTTP 状态码恒 200，业务成败看 code），用 TryCheckEnvelope 先行校验。
    /// </summary>
    internal static class EditorHttp
    {
        private const int TimeoutSeconds = 10;

        public static void Get(string url, string bearerToken, Action<string> onDone, Action<string> onError)
        {
            Send(UnityWebRequest.Get(url), bearerToken, onDone, onError);
        }

        public static void PostJson(string url, string bearerToken, string jsonBody, Action<string> onDone, Action<string> onError)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            Send(request, bearerToken, onDone, onError);
        }

        private static void Send(UnityWebRequest request, string bearerToken, Action<string> onDone, Action<string> onError)
        {
            if (!string.IsNullOrEmpty(bearerToken))
                request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
            request.timeout = TimeoutSeconds;
            var operation = request.SendWebRequest();

            void Tick()
            {
                if (!operation.isDone) return;
                EditorApplication.update -= Tick;
                using (request)
                {
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        onError?.Invoke($"网络请求失败：{request.error}（{request.url}）");
                        return;
                    }
                    onDone?.Invoke(request.downloadHandler.text);
                }
            }
            EditorApplication.update += Tick;
        }

        [Serializable]
        private class EnvelopeHead
        {
            public int code = -1;   // 缺省 -1 用于识别"响应里根本没有 code 字段"
            public string message;
        }

        /// <summary>校验响应信封 code==200；业务错误返回 false 并组装 "[code] message"。</summary>
        public static bool TryCheckEnvelope(string json, out string error)
        {
            EnvelopeHead head = null;
            try
            {
                head = JsonUtility.FromJson<EnvelopeHead>(json);
            }
            catch (Exception)
            {
                // 非法 JSON 走下方统一报错
            }
            if (head == null || head.code == -1)
            {
                error = $"响应格式异常，无法解析信封：{Truncate(json)}";
                return false;
            }
            if (head.code != 200)
            {
                error = $"[{head.code}] {head.message}";
                return false;
            }
            error = null;
            return true;
        }

        private static string Truncate(string text)
        {
            if (string.IsNullOrEmpty(text)) return "(空响应)";
            return text.Length <= 200 ? text : text.Substring(0, 200) + "…";
        }
    }
}
