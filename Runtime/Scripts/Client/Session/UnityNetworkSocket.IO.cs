using System;
using System.IO;
using System.Net.WebSockets;
using System.Net.Sockets;
using ManagedWebSocket = System.Net.WebSockets.WebSocket;
using System.Threading;
using System.Threading.Tasks;

namespace MyVerseXRSDK
{
    internal sealed partial class UnityNetworkSocket
    {
        private async Task RunConnection(TcpClient client, Uri endpoint, CancellationToken cancellation)
        {
            Task sender = null;
            ManagedWebSocket connection = null;
            try
            {
                connection = await WebSocketUpgrade.Connect(client, endpoint, cancellation).ConfigureAwait(false);
                lock (gate)
                {
                    if (ioStopped || disposed) return;
                    socket = connection;
                    connected = true;
                }
                inbox.Open();
                sender = SendLoop(connection, cancellation);
                await ReceiveLoop(connection, cancellation).ConfigureAwait(false);
            }
            catch (SocketOperationException error) { if (!cancellation.IsCancellationRequested) Fail(error.Result); }
            catch (Exception error)
            {
                if (!cancellation.IsCancellationRequested)
                    Fail(SdkResult.Fail(SdkErrorCode.TransportFailed, "WebSocket连接/接收失败：" + error.GetType().Name));
            }
            finally
            {
                StopIo();
                if (sender != null) await sender.ConfigureAwait(false);
                lock (gate)
                {
                    connection?.Dispose();
                    client.Dispose();
                    lifetime?.Dispose();
                    lifetime = null;
                    socket = null;
                    tcp = null;
                }
            }
        }

        private async Task SendLoop(ManagedWebSocket connection, CancellationToken cancellation)
        {
            try
            {
                await outgoing.Run((bytes, token) => connection.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Binary, true, token), cancellation).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                if (!cancellation.IsCancellationRequested)
                    Fail(SdkResult.Fail(SdkErrorCode.TransportFailed, "WebSocket发送失败：" + error.GetType().Name));
            }
        }

        private async Task ReceiveLoop(ManagedWebSocket connection, CancellationToken cancellation)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);
            using var message = new MemoryStream();
            while (!cancellation.IsCancellationRequested)
            {
                var result = await connection.ReceiveAsync(buffer, cancellation).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    inbox.Close(SdkResult.Fail(SdkErrorCode.NotConnected, "服务端关闭WebSocket，状态：" + result.CloseStatus));
                    return;
                }
                if (result.MessageType != WebSocketMessageType.Binary)
                    throw new InvalidDataException("房间协议只接受二进制帧");
                if (message.Length + result.Count > limits.MaximumMessageBytes)
                    throw new SocketOperationException(SdkResult.Fail(SdkErrorCode.CapacityExceeded, "WebSocket组帧超出大小上限"));
                message.Write(buffer.Array, 0, result.Count);
                if (!result.EndOfMessage) continue;
                var accepted = inbox.Enqueue(message.ToArray());
                message.SetLength(0);
                if (!accepted.Success) throw new SocketOperationException(accepted);
            }
        }
    }
}
