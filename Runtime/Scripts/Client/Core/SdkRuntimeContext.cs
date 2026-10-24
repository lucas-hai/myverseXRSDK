using System;
using System.Threading;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class SdkRuntimeContext
    {
        private readonly int ownerThread;
        private readonly HashSet<OperationHandle> operations = new HashSet<OperationHandle>();
        private long nextRequestId;
        internal DiagnosticsService Diagnostics { get; }
        internal SdkLogger Log { get; }
        internal long RuntimeId { get; }
        internal SdkOptions Options { get; }
        internal SdkRuntimeHost Host { get; }
        internal bool IsDisposed { get; private set; }
        internal bool IsClosing { get; private set; }
        internal bool IsRunning { get; set; }

        internal SdkRuntimeContext(long runtimeId, SdkOptions options, Action tick, Action destroyed)
        {
            ownerThread = Thread.CurrentThread.ManagedThreadId;
            RuntimeId = runtimeId;
            Options = options;
            Log = new SdkLogger(runtimeId, options.Logging);
            Diagnostics = new DiagnosticsService(runtimeId, Log);
            var root = new GameObject("MVXRSDK.Runtime") { hideFlags = HideFlags.DontSave };
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(root);
            Host = root.AddComponent<SdkRuntimeHost>();
            Host.Initialize(tick, destroyed, Log);
        }

        internal void EnsureMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("SDK 操作必须在创建客户端的 Unity 主线程执行");
        }

        internal void EnsureAlive()
        {
            EnsureMainThread();
            if (IsDisposed) throw new ObjectDisposedException(nameof(SdkClient));
        }

        internal void BeginClosing()
        {
            EnsureMainThread();
            IsClosing = true;
            IsRunning = false;
        }

        internal OperationHandle CreateOperation(string domain, string targetId, Action<OperationResult> completed)
        {
            EnsureAlive();
            var operation = new OperationHandle(++nextRequestId, targetId, Host.Post, completed, EnsureMainThread, Log);
            operations.Add(operation);
            operation.Finished += finished =>
            {
                operations.Remove(finished);
                var outcome = finished.Outcome;
                if (outcome.HasValue && !outcome.Value.Success)
                    Diagnostics.Report(domain, outcome.Value.Result, finished.RequestId);
            };
            return operation;
        }

        internal void Close()
        {
            EnsureMainThread();
            if (IsDisposed) return;
            BeginClosing();
            IsDisposed = true;
            foreach (var operation in new List<OperationHandle>(operations))
                operation.TryComplete(SdkResult.Fail(SdkErrorCode.Disposed, "客户端已关闭"), false, operation.WasSent);
            operations.Clear();
            if (Host != null) Host.Close();
            Diagnostics.Close();
        }
    }
}
