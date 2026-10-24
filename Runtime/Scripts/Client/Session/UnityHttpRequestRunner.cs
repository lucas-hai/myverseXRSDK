using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace MyVerseXRSDK
{
    internal sealed class UnityHttpRequestRunner : IHttpRequestRunner
    {
        private sealed class Lease : IDisposable
        {
            private readonly UnityHttpRequestRunner owner;
            internal UnityWebRequest Request;
            internal Coroutine Routine;
            internal Action<HttpReply> Callback;
            internal bool Done;
            internal Lease(UnityHttpRequestRunner owner, UnityWebRequest request, Action<HttpReply> callback)
            { this.owner = owner; Request = request; Callback = callback; }
            public void Dispose() => owner.Cancel(this);
        }

        private readonly SdkRuntimeHost host;
        private readonly float timeout;
        private readonly HashSet<Lease> active = new HashSet<Lease>();
        private bool disposed;
        internal UnityHttpRequestRunner(SdkRuntimeHost host, float timeout)
        { this.host = host; this.timeout = timeout; }

        public IDisposable Begin(string method, string url, byte[] body, Action<HttpReply> completed)
        {
            if (disposed) throw new ObjectDisposedException(nameof(UnityHttpRequestRunner));
            var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(), timeout = Mathf.Max(1, Mathf.CeilToInt(timeout))
            };
            if (method != UnityWebRequest.kHttpVerbGET)
            {
                request.uploadHandler = new UploadHandlerRaw(body ?? Array.Empty<byte>());
                request.SetRequestHeader("Content-Type", "application/json");
            }
            var lease = new Lease(this, request, completed);
            active.Add(lease);
            lease.Routine = host.Run(Execute(lease));
            return lease;
        }

        private IEnumerator Execute(Lease lease)
        {
            UnityWebRequestAsyncOperation operation;
            try { operation = lease.Request.SendWebRequest(); }
            catch (Exception error)
            {
                Complete(lease, new HttpReply(SdkResult.Fail(SdkErrorCode.TransportFailed, error.Message), "", 0));
                yield break;
            }
            yield return operation;
            if (lease.Done) yield break;
            var request = lease.Request;
            var result = request.result == UnityWebRequest.Result.Success
                ? SdkResult.Ok : SdkResult.Fail(SdkErrorCode.TransportFailed, request.error);
            var reply = new HttpReply(result, request.downloadHandler.text, request.responseCode);
            Complete(lease, reply);
        }

        private void Complete(Lease lease, HttpReply reply)
        {
            if (lease.Done) return;
            lease.Done = true;
            active.Remove(lease);
            var callback = lease.Callback;
            lease.Callback = null;
            lease.Request.Dispose();
            lease.Request = null;
            try { callback?.Invoke(reply); }
            catch (Exception error) { host.Log.Exception("Http", error); }
        }

        private void Cancel(Lease lease)
        {
            if (lease.Done) return;
            lease.Done = true;
            active.Remove(lease);
            lease.Callback = null;
            lease.Request.Abort();
            lease.Request.Dispose();
            lease.Request = null;
            if (host != null) host.Cancel(lease.Routine);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var lease in new List<Lease>(active)) lease.Dispose();
            active.Clear();
        }
    }
}
