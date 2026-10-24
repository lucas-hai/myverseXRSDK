using System;
using System.Net.Sockets;
using ManagedWebSocket = System.Net.WebSockets.WebSocket;
using System.Threading;
using System.Threading.Tasks;

namespace MyVerseXRSDK
{
    /// <summary>一次真实连接的IO所有者；后台仅处理字节，主线程按实例预算派发单一收件队列。</summary>
    internal sealed partial class UnityNetworkSocket : INetworkSocket
    {
        private readonly object gate = new object();
        private readonly NetworkLimits limits;
        private readonly SocketInbox inbox;
        private readonly SocketSendQueue outgoing;
        private readonly Action<SdkResult> report;
        private readonly SdkLogger log;
        private ManagedWebSocket socket;
        private TcpClient tcp;
        private CancellationTokenSource lifetime;
        private Task connectionTask;
        private bool started;
        private bool connected;
        private bool ioStopped;
        private bool disposed;
        public event Action Opened;
        public event Action<byte[]> Received;
        public event Action<string> Closed;

        internal UnityNetworkSocket(SessionOptions options = null, Action<SdkResult> report = null, SdkLogger log = null)
        {
            limits = (options?.NetworkLimits ?? new NetworkLimits()).Capture();
            this.report = report;
            this.log = log ?? new SdkLogger(0);
            inbox = new SocketInbox(limits.MaximumQueuedMessages, limits.MaximumQueuedBytes,
                () => Opened?.Invoke(), data => Received?.Invoke(data), EmitClosed);
            outgoing = new SocketSendQueue(limits.MaximumQueuedMessages, limits.MaximumQueuedBytes);
        }

        public void Connect(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var endpoint) || (endpoint.Scheme != "ws" && endpoint.Scheme != "wss"))
                throw new ArgumentException("连接地址必须为WebSocket地址", nameof(address));
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(UnityNetworkSocket));
                if (started) throw new InvalidOperationException("一次连接实例不能复用");
                started = true;
                tcp = new TcpClient();
                lifetime = new CancellationTokenSource();
                var connection = tcp;
                var cancellation = lifetime.Token;
                connectionTask = Task.Run(() => RunConnection(connection, endpoint, cancellation));
            }
        }

        public void Send(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(UnityNetworkSocket));
                if (!connected || ioStopped) throw new InvalidOperationException("WebSocket未连接");
                if (data.Length > limits.MaximumMessageBytes)
                    throw new SocketOperationException(SdkResult.Fail(SdkErrorCode.CapacityExceeded, "WebSocket发送帧超出大小上限"));
                var result = outgoing.Enqueue(data);
                if (!result.Success) throw new SocketOperationException(result);
            }
        }

        public void Tick() => inbox.Drain(limits.MessagesPerFrame);

        private void EmitClosed(SdkResult result)
        {
            if (!result.Success)
            {
                if (report != null) report(result);
                else log.Write(SdkLogLevel.Warning, "Session", "[MVXRSDK][Socket] " + result.Code + ": " + result.Message);
            }
            if (!disposed) Closed?.Invoke(result.Message);
        }

        private void StopIo()
        {
            lock (gate)
            {
                if (ioStopped) return;
                ioStopped = true;
                connected = false;
                outgoing.Dispose();
                // Connecting也必须取消/中止；不能等待打开后再尝试发送Close帧。
                lifetime?.Cancel();
                tcp?.Dispose();
                socket?.Abort();
                socket?.Dispose();
            }
        }

        private void Fail(SdkResult result)
        {
            inbox.Close(result);
            StopIo();
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
            }
            inbox.Dispose();
            StopIo();
            Opened = null; Received = null; Closed = null;
        }
    }
}
