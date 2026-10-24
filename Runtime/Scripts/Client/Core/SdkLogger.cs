using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>主线程实例出口；sink不在领域调用栈中执行，排队和递归都有界。</summary>
    internal sealed class SdkLogger
    {
        private readonly long runtimeId;
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly SdkLogOptions options;
        private readonly Queue<SdkLogEntry> pending = new Queue<SdkLogEntry>();
        private Action<SdkLogEntry> sink;
        private bool draining;
        private bool closing;
        private bool closed;
        private long unreportedDrops;
        internal long DroppedCount { get; private set; }

        internal SdkLogger(long runtimeId, SdkLogOptions options = null)
        {
            this.runtimeId = runtimeId;
            this.options = (options ?? new SdkLogOptions()).Capture();
            sink = this.options.Sink;
            this.options.Sink = null;
        }

        internal void Write(SdkLogLevel level, string domain, string message, SdkErrorCode code = SdkErrorCode.None,
            long requestId = 0, long sessionEpoch = 0) => Emit(level, domain, message, code, requestId, sessionEpoch, null);
        internal void Exception(string domain, Exception error) => Emit(SdkLogLevel.Error, domain,
            error.ToString(), SdkErrorCode.InternalError, 0, 0, error);

        private void Emit(SdkLogLevel level, string domain, string message, SdkErrorCode code,
            long requestId, long sessionEpoch, Exception exception)
        {
            EnsureThread();
            if (closed || level < options.MinimumLevel || level == SdkLogLevel.None) return;
            var entry = new SdkLogEntry(level, LogSanitizer.Clean(domain), LogSanitizer.Clean(message), runtimeId, requestId, sessionEpoch, code);
            if (options.WriteToUnityConsole || draining) WriteUnity(entry, exception, message);
            if (sink == null || draining) return;
            if (pending.Count >= options.MaximumQueuedEntries) { DroppedCount++; unreportedDrops++; return; }
            pending.Enqueue(entry);
        }

        internal void Drain()
        {
            EnsureThread();
            if (draining || closed) return;
            draining = true;
            try
            {
                if (unreportedDrops > 0)
                {
                    long dropped = unreportedDrops;
                    unreportedDrops = 0;
                    Debug.LogWarning($"[MVXRSDK][Logging] 日志队列超限，丢弃{dropped}条；错误结果和事件不受影响");
                }
                int count = pending.Count;
                for (int index = 0; index < count && (closing || index < options.EntriesPerFrame) && pending.Count > 0; index++)
                {
                    var entry = pending.Dequeue();
                    try { sink?.Invoke(entry); }
                    catch (Exception error)
                    {
                        // sink的失败不再进入同一sink，避免观察者故障递归；不包含任意异常消息中的凭据。
                        Debug.LogError("[MVXRSDK][Logging] 日志sink异常：" + error.GetType().Name);
                    }
                }
            }
            finally
            {
                draining = false;
                if (closing) { pending.Clear(); sink = null; closed = true; }
            }
        }

        internal void Close()
        {
            EnsureThread();
            closing = true;
            Drain();
        }

        private void EnsureThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("实例日志必须在客户端主线程输出；音频错误须先交回主线程");
        }

        private static void WriteUnity(SdkLogEntry entry, Exception exception, string original)
        {
            if (exception != null)
            {
                if (entry.Message == original) Debug.LogException(exception);
                else Debug.unityLogger.Log(LogType.Exception, entry.Message);
                return;
            }
            string message = $"[MVXRSDK][{entry.Domain}] {entry.Message}";
            if (entry.Level == SdkLogLevel.Error) Debug.LogError(message);
            else if (entry.Level == SdkLogLevel.Warning) Debug.LogWarning(message);
            else Debug.Log(message);
        }
    }
}
