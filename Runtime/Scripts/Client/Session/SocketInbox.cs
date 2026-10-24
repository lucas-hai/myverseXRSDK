using System;
using System.Collections.Generic;

namespace MyVerseXRSDK
{
    /// <summary>后台只入队，主线程按预算消费；连接控制通知额外最多占两个槽，不能挤掉先收到的响应。</summary>
    internal sealed class SocketInbox : IDisposable
    {
        private enum Kind { Open, Data, Close }
        private readonly struct Notice
        {
            internal readonly Kind Kind;
            internal readonly byte[] Data;
            internal readonly SdkResult Result;
            internal Notice(Kind kind, byte[] data = null, SdkResult result = default)
            { Kind = kind; Data = data; Result = result; }
        }
        private readonly object gate = new object();
        private readonly Queue<Notice> pending = new Queue<Notice>();
        private readonly int maximumMessages;
        private readonly int maximumBytes;
        private Action opened;
        private Action<byte[]> received;
        private Action<SdkResult> terminated;
        private int messages;
        private int bytes;
        private bool openQueued;
        private bool closed;
        private bool disposed;

        internal SocketInbox(int maximumMessages, int maximumBytes, Action opened, Action<byte[]> received, Action<SdkResult> terminated)
        {
            if (maximumMessages <= 0 || maximumBytes <= 0) throw new ArgumentOutOfRangeException();
            this.maximumMessages = maximumMessages; this.maximumBytes = maximumBytes;
            this.opened = opened; this.received = received; this.terminated = terminated;
        }

        internal void Open()
        {
            lock (gate)
            {
                if (closed || disposed || openQueued) return;
                openQueued = true;
                pending.Enqueue(new Notice(Kind.Open));
            }
        }

        internal SdkResult Enqueue(byte[] data)
        {
            if (data == null) return SdkResult.Fail(SdkErrorCode.InvalidArgument, "接收数据不能为空");
            lock (gate)
            {
                if (closed || disposed) return SdkResult.Fail(SdkErrorCode.Disposed, "接收队列已关闭");
                if (messages >= maximumMessages || data.Length > maximumBytes - bytes)
                    return SdkResult.Fail(SdkErrorCode.CapacityExceeded, "WebSocket接收队列超出容量");
                pending.Enqueue(new Notice(Kind.Data, data));
                messages++; bytes += data.Length;
                return SdkResult.Ok;
            }
        }

        internal void Close(SdkResult result)
        {
            lock (gate)
            {
                if (closed || disposed) return;
                closed = true;
                pending.Enqueue(new Notice(Kind.Close, result: result));
            }
        }

        internal void Drain(int budget)
        {
            for (int index = 0; index < budget; index++)
            {
                Notice notice;
                lock (gate)
                {
                    if (disposed || pending.Count == 0) return;
                    notice = pending.Dequeue();
                    if (notice.Kind == Kind.Data) { messages--; bytes -= notice.Data.Length; }
                }
                switch (notice.Kind)
                {
                    case Kind.Open: opened?.Invoke(); break;
                    case Kind.Data: received?.Invoke(notice.Data); break;
                    case Kind.Close: terminated?.Invoke(notice.Result); break;
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true; closed = true;
                pending.Clear(); messages = bytes = 0;
                opened = null; received = null; terminated = null;
            }
        }
    }
}
