using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MyVerseXRSDK
{
    internal sealed class UnityWhipExchange : IWhipExchange
    {
        private sealed class Request : IDisposable
        {
            internal UnityWhipExchange Owner;
            internal UnityWebRequest Http;
            internal Coroutine Routine;
            internal Action<UnityWebRequest> Completed;
            internal bool Done;
            public void Dispose() => Owner.Cancel(this);
        }

        private readonly SdkRuntimeHost host;
        private readonly HashSet<Request> pending = new HashSet<Request>();
        private bool disposed;
        internal UnityWhipExchange(SdkRuntimeHost host) => this.host = host;

        public IDisposable Post(string url, string offer, int timeoutSeconds, Action<WhipReply> completed)
        {
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(offer)),
                downloadHandler = new DownloadHandlerBuffer(), timeout = timeoutSeconds
            };
            request.SetRequestHeader("Content-Type", "application/sdp");
            return Begin(request, result => completed(WhipResponseMapper.Map((int)result.responseCode,
                result.downloadHandler.text, result.GetResponseHeader("Location"), url,
                result.result == UnityWebRequest.Result.ConnectionError || result.result == UnityWebRequest.Result.DataProcessingError)));
        }

        public IDisposable Delete(string location, int timeoutSeconds, Action<SdkResult> completed)
        {
            var request = UnityWebRequest.Delete(location);
            request.timeout = timeoutSeconds;
            return Begin(request, result => completed(WhipResponseMapper.MapDelete(result.responseCode,
                result.result == UnityWebRequest.Result.ConnectionError || result.result == UnityWebRequest.Result.DataProcessingError)));
        }

        private IDisposable Begin(UnityWebRequest http, Action<UnityWebRequest> completed)
        {
            if (disposed) { http.Dispose(); throw new ObjectDisposedException(nameof(UnityWhipExchange)); }
            var request = new Request { Owner = this, Http = http, Completed = completed };
            pending.Add(request);
            try { request.Routine = host.Run(Send(request)); }
            catch { Cancel(request); throw; }
            return request;
        }

        private IEnumerator Send(Request request)
        {
            yield return request.Http.SendWebRequest();
            if (request.Done) yield break;
            request.Done = true;
            pending.Remove(request);
            var completed = request.Completed;
            request.Completed = null;
            // 回调可重入关闭；Done先提交，避免取消路径再次释放正在读取的响应。
            try { completed(request.Http); }
            finally { request.Http.Dispose(); request.Http = null; request.Routine = null; }
        }

        private void Cancel(Request request)
        {
            if (request.Done) return;
            request.Done = true;
            pending.Remove(request);
            request.Completed = null;
            request.Http.Abort();
            request.Http.Dispose(); request.Http = null;
            host.Cancel(request.Routine); request.Routine = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var request in new List<Request>(pending)) Cancel(request);
            pending.Clear();
        }
    }
}
