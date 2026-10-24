using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ManagedWebSocket = System.Net.WebSockets.WebSocket;

namespace MyVerseXRSDK
{
    /// <summary>在显式拥有的TCP/TLS流上完成升级；不借用无法由连接所有者关闭的HTTP连接池。</summary>
    internal static class WebSocketUpgrade
    {
        private const int MaximumHeaderBytes = 16 * 1024;

        internal static async Task<ManagedWebSocket> Connect(TcpClient client, Uri endpoint, CancellationToken cancellation)
        {
            await client.ConnectAsync(endpoint.DnsSafeHost, endpoint.Port).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            Stream stream = client.GetStream();
            try
            {
                if (endpoint.Scheme == "wss")
                {
                    var secure = new SslStream(stream, false);
                    stream = secure;
                    // 默认链/主机名校验保留；LAN不依赖在线吊销查询，禁止接受任意证书的回调。
                    await secure.AuthenticateAsClientAsync(endpoint.DnsSafeHost, new X509CertificateCollection(),
                        SslProtocols.Tls12, false).ConfigureAwait(false);
                }
                cancellation.ThrowIfCancellationRequested();
                string key = CreateKey();
                var request = Encoding.ASCII.GetBytes("GET " + endpoint.PathAndQuery + " HTTP/1.1\r\nHost: " + endpoint.Authority +
                    "\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n");
                await stream.WriteAsync(request, 0, request.Length, cancellation).ConfigureAwait(false);
                string response = await ReadHeaders(stream, cancellation).ConfigureAwait(false);
                ValidateResponse(response, key);
                cancellation.ThrowIfCancellationRequested();
                return ManagedWebSocket.CreateFromStream(stream, false, null, TimeSpan.FromSeconds(30));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        private static string CreateKey()
        {
            var bytes = new byte[16];
            using var random = RandomNumberGenerator.Create();
            random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private static async Task<string> ReadHeaders(Stream stream, CancellationToken cancellation)
        {
            var header = new byte[MaximumHeaderBytes];
            int count = 0;
            // 不超读首个WebSocket帧，避免另建可变前缀缓冲；握手不处于稳态消息热路径。
            while (count < header.Length)
            {
                if (await stream.ReadAsync(header, count, 1, cancellation).ConfigureAwait(false) == 0)
                    throw new EndOfStreamException("WebSocket升级响应提前结束");
                count++;
                if (count >= 4 && header[count - 4] == 13 && header[count - 3] == 10 && header[count - 2] == 13 && header[count - 1] == 10)
                    return Encoding.ASCII.GetString(header, 0, count);
            }
            throw new InvalidDataException("WebSocket升级响应头超出上限");
        }

        private static void ValidateResponse(string response, string key)
        {
            var lines = response.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var status = lines[0].Split(' ');
            if (status.Length < 2 || status[0] != "HTTP/1.1" || status[1] != "101")
                throw new InvalidDataException("WebSocket升级响应状态不是101");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 1; index < lines.Length; index++)
            {
                if (lines[index].Length == 0) break;
                int separator = lines[index].IndexOf(':');
                if (separator <= 0) throw new InvalidDataException("WebSocket升级响应头无效");
                string name = lines[index].Substring(0, separator).Trim();
                string value = lines[index].Substring(separator + 1).Trim();
                if (headers.TryGetValue(name, out var existing)) value = existing + "," + value;
                headers[name] = value;
            }
            if (!headers.TryGetValue("Upgrade", out var upgrade) || !string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase) ||
                !headers.TryGetValue("Connection", out var connection) || !HasToken(connection, "Upgrade"))
                throw new InvalidDataException("服务端未确认WebSocket升级");
            using var sha = SHA1.Create();
            string expected = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            if (!headers.TryGetValue("Sec-WebSocket-Accept", out var accept) || !string.Equals(accept, expected, StringComparison.Ordinal))
                throw new InvalidDataException("WebSocket升级校验值不匹配");
            if (headers.ContainsKey("Sec-WebSocket-Extensions") || headers.ContainsKey("Sec-WebSocket-Protocol"))
                throw new InvalidDataException("服务端选择了未请求的WebSocket扩展或子协议");
        }

        private static bool HasToken(string value, string token)
        {
            foreach (string item in value.Split(','))
                if (string.Equals(item.Trim(), token, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
