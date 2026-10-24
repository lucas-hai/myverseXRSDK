using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MyVerseXRSDK
{
    /// <summary>单个持久发送worker；信号与入队同步，避免空队列到worker退出之间丢唤醒。</summary>
    internal sealed class SocketSendQueue : IDisposable
    {
        private readonly object gate = new object();
        private readonly Queue<byte[]> pending = new Queue<byte[]>();
        private readonly SemaphoreSlim available = new SemaphoreSlim(0);
        private readonly int maximumMessages;
        private readonly int maximumBytes;
        private int queuedBytes;
        private bool running;
        private bool closed;

        internal SocketSendQueue(int maximumMessages, int maximumBytes)
        {
            if (maximumMessages <= 0 || maximumBytes <= 0) throw new ArgumentOutOfRangeException();
            this.maximumMessages = maximumMessages;
            this.maximumBytes = maximumBytes;
        }

        /// <summary>不复制缓冲；成功入队后调用方不得修改字节，直至该次连接结束。</summary>
        internal SdkResult Enqueue(byte[] data)
        {
            if (data == null) return SdkResult.Fail(SdkErrorCode.InvalidArgument, "发送数据不能为空");
            lock (gate)
            {
                if (closed) return SdkResult.Fail(SdkErrorCode.Disposed, "发送队列已关闭");
                if (pending.Count >= maximumMessages || data.Length > maximumBytes - queuedBytes)
                    return SdkResult.Fail(SdkErrorCode.CapacityExceeded, "WebSocket发送队列超出容量");
                pending.Enqueue(data);
                queuedBytes += data.Length;
                available.Release();
                return SdkResult.Ok;
            }
        }

        internal async Task Run(Func<byte[], CancellationToken, Task> send, CancellationToken cancellation)
        {
            lock (gate)
            {
                if (running) throw new InvalidOperationException("每个发送队列只能启动一个worker");
                running = true;
                if (closed) return;
            }
            try
            {
                while (true)
                {
                    await available.WaitAsync(cancellation).ConfigureAwait(false);
                    byte[] data;
                    lock (gate)
                    {
                        if (closed) return;
                        data = pending.Dequeue();
                        queuedBytes -= data.Length;
                    }
                    await send(data, cancellation).ConfigureAwait(false);
                }
            }
            finally
            {
                lock (gate)
                {
                    closed = true;
                    pending.Clear();
                    queuedBytes = 0;
                    available.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (closed) return;
                closed = true;
                pending.Clear();
                queuedBytes = 0;
                if (running) available.Release();
                else available.Dispose();
            }
        }
    }

    internal sealed class SocketOperationException : Exception
    {
        internal SdkResult Result { get; }
        internal SocketOperationException(SdkResult result) : base(result.Message) => Result = result;
    }
}
