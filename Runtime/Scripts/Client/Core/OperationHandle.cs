using System;

namespace MyVerseXRSDK
{
    /// <summary>一次命令的完成权。取消只结束本地等待，不撤销已经送达服务端的副作用。</summary>
    public sealed class OperationHandle
    {
        private readonly object gate = new object();
        private Action<Action> dispatch;
        private Action<OperationResult> callback;
        private Action ensureCancelThread;
        private readonly SdkLogger log;
        private bool completed;
        private bool sent;
        private OperationResult outcome;

        public long RequestId { get; }
        public string TargetId { get; }
        public bool IsCompleted { get { lock (gate) return completed; } }
        public OperationResult? Outcome { get { lock (gate) return completed ? outcome : (OperationResult?)null; } }
        internal event Action<OperationHandle> Finished;
        internal bool WasSent { get { lock (gate) return sent; } }

        internal OperationHandle(long requestId, string targetId, Action<Action> dispatch, Action<OperationResult> callback)
            : this(requestId, targetId, dispatch, callback, null) { }

        internal OperationHandle(long requestId, string targetId, Action<Action> dispatch, Action<OperationResult> callback, Action ensureCancelThread, SdkLogger log = null)
        {
            this.log = log ?? new SdkLogger(0);
            this.ensureCancelThread = ensureCancelThread;
            RequestId = requestId;
            TargetId = targetId ?? string.Empty;
            this.dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            this.callback = callback;
        }

        /// <summary>在客户端主线程取消等待；已发送的请求返回远端结果未知，已完成请求不受影响。</summary>
        public void Cancel()
        {
            Action verifyThread;
            lock (gate)
            {
                if (completed) return;
                verifyThread = ensureCancelThread;
            }
            verifyThread?.Invoke();
            TryComplete(SdkResult.Fail(SdkErrorCode.Cancelled, "操作已取消"));
        }

        internal bool MarkSent()
        {
            lock (gate)
            {
                if (completed) return false;
                sent = true;
                return true;
            }
        }

        internal bool TryComplete(SdkResult result, bool wasCached = false, bool remoteOutcomeUnknown = false)
        {
            Action<OperationResult> completion;
            Action<OperationHandle> cleanup;
            Action<Action> enqueue;
            OperationResult value;
            lock (gate)
            {
                if (completed) return false;
                if (result.Code == SdkErrorCode.Cancelled) remoteOutcomeUnknown |= sent;
                value = new OperationResult(RequestId, TargetId, result, wasCached, remoteOutcomeUnknown);
                outcome = value;
                completed = true;
                completion = callback;
                callback = null;
                cleanup = Finished;
                Finished = null;
                enqueue = dispatch;
                dispatch = null;
                ensureCancelThread = null;
            }
            // 观察者可能同步关闭Runtime；先把完成通知交给宿主，关闭排空才能覆盖它。
            if (completion != null) enqueue(() => completion(value));
            if (cleanup != null)
                foreach (Action<OperationHandle> observer in cleanup.GetInvocationList())
                {
                    try { observer(this); }
                    catch (Exception error) { log.Exception("Operation", error); }
                }
            return true;
        }
    }
}
