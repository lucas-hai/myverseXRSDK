using System;
using System.Collections.Generic;

namespace MyVerseXRSDK
{
    internal sealed class CommandScope : IDisposable
    {
        private readonly SdkRuntimeContext context;
        private readonly string domain;
        private readonly HashSet<OperationHandle> operations = new HashSet<OperationHandle>();
        private bool disposed;

        internal CommandScope(SdkRuntimeContext context, string domain)
        { this.context = context; this.domain = domain; }

        internal OperationHandle Begin(string targetId, Action<OperationResult> completed)
        {
            context.EnsureAlive();
            if (disposed) throw new ObjectDisposedException(domain);
            var operation = context.CreateOperation(domain, targetId, completed);
            operations.Add(operation);
            operation.Finished += OnFinished;
            return operation;
        }

        private void OnFinished(OperationHandle operation)
        {
            operations.Remove(operation);
            operation.Finished -= OnFinished;
        }

        internal static void AttachLease(OperationHandle operation, IDisposable lease)
        {
            if (lease == null) return;
            if (operation.IsCompleted) { lease.Dispose(); return; }
            operation.Finished += Release;
            void Release(OperationHandle completed)
            {
                completed.Finished -= Release;
                lease.Dispose();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var operation in new List<OperationHandle>(operations))
                operation.TryComplete(SdkResult.Fail(SdkErrorCode.Disposed, "所属服务已关闭"), false, operation.WasSent);
            operations.Clear();
        }
    }
}
