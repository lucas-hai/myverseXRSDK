using System;

namespace MyVerseXRSDK
{
    /// <summary>持有一次POST及其Location。结束后短暂接管迟到响应，清理预算不控制native资源寿命。</summary>
    internal sealed class WhipSessionLease
    {
        private readonly IWhipExchange exchange;
        private readonly MediaOptions options;
        private readonly Func<double> clock;
        private readonly Action<SdkResult> report;
        private IDisposable post;
        private IDisposable delete;
        private Action<WhipReply> callback;
        private bool postPending;
        private bool deletePending;
        private bool started;
        private bool closed;
        private string location;
        private double deadline;
        internal bool IsReleased => closed && !postPending && !deletePending;

        internal WhipSessionLease(IWhipExchange exchange, MediaOptions options, Func<double> clock, Action<SdkResult> report)
        { this.exchange = exchange; this.options = options; this.clock = clock; this.report = report; }

        internal void Start(string url, string offer, Action<WhipReply> completed)
        {
            if (started || closed) return;
            started = true;
            callback = completed;
            postPending = true;
            try
            {
                var lease = exchange.Post(url, offer, options.HttpTimeoutSeconds, OnPost);
                if (postPending) post = lease;
                else lease?.Dispose();
            }
            catch
            {
                postPending = false;
                callback = null;
                throw;
            }
        }

        private void OnPost(WhipReply reply)
        {
            if (!postPending) return;
            postPending = false;
            post?.Dispose();
            post = null;
            location = reply.Location;
            var completed = callback;
            callback = null;
            if (closed) BeginDelete();
            else completed?.Invoke(reply);
        }

        internal void Close()
        {
            if (closed) return;
            closed = true;
            callback = null;
            deadline = clock() + options.RemoteCleanupSeconds;
            BeginDelete();
        }

        private void BeginDelete()
        {
            if (string.IsNullOrEmpty(location) || deletePending) return;
            string ownedLocation = location;
            location = null;
            deletePending = true;
            try
            {
                var lease = exchange.Delete(ownedLocation, Math.Max(1, (int)Math.Ceiling(options.RemoteCleanupSeconds)), result =>
                {
                    if (!deletePending) return;
                    deletePending = false;
                    delete?.Dispose();
                    delete = null;
                    if (!result.Success) report(result);
                });
                if (deletePending) delete = lease;
                else lease?.Dispose();
            }
            catch (Exception error)
            {
                deletePending = false;
                report(SdkResult.Fail(SdkErrorCode.TransportFailed, "WHIP清理请求失败：" + error.Message));
            }
        }

        internal void Tick()
        {
            if (!closed || IsReleased || clock() < deadline) return;
            postPending = deletePending = false;
            post?.Dispose(); delete?.Dispose();
            post = delete = null;
            report(SdkResult.Fail(SdkErrorCode.Timeout, "WHIP远端清理预算耗尽，远端结果未知"));
        }
    }
}
