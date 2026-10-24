using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Google.Protobuf;
using Protobuf;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class SocketMessageTransport : ICommandTransport
    {
        private sealed class Pending
        {
            internal float Started;
            internal Action<int, byte[]> Callback;
        }
        internal const int TimeoutCode = -999;
        internal const int CancelledCode = -2;
        internal const int CapacityCode = int.MaxValue;
        private readonly Action<SdkResult> report;
        private readonly SessionOptions options;
        private readonly Func<float> now;
        private readonly Func<INetworkSocket> socketFactory;
        private readonly MessageSubscriptions subscriptions;
        private readonly SdkLogger log;
        private readonly Dictionary<long, Pending> pending = new Dictionary<long, Pending>();
        private readonly List<long> expired = new List<long>();
        private INetworkSocket socket;
        private Action opened;
        private Action<byte[]> received;
        private Action<string> closed;
        private string address;
        private long requestId;
        private long attemptGeneration;
        private int reconnectAttempts;
        private float stateStarted;
        private float nextRetry;
        private bool disposed;
        private TransportState state;
        public bool IsConnected => !disposed && state == TransportState.Connected;
        public event Action<TransportState> Changed;

        internal SocketMessageTransport(SessionOptions options, Func<float> now, Func<INetworkSocket> socketFactory)
            : this(options, now, socketFactory, null) { }

        internal SocketMessageTransport(SessionOptions options, Func<float> now, Func<INetworkSocket> socketFactory, Action<SdkResult> report)
            : this(options, now, socketFactory, report, null) { }

        internal SocketMessageTransport(SessionOptions options, Func<float> now, Func<INetworkSocket> socketFactory, Action<SdkResult> report, SdkLogger log)
        {
            this.options = (options ?? throw new ArgumentNullException(nameof(options))).Capture();
            this.report = report;
            this.log = log ?? new SdkLogger(0);
            subscriptions = new MessageSubscriptions(this.log);
            this.now = now ?? throw new ArgumentNullException(nameof(now));
            this.socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        }

        public void Connect(string value)
        {
            EnsureAlive();
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("房间地址不能为空", nameof(value));
            address = value.Contains("://") ? value : "ws://" + value;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "ws" && uri.Scheme != "wss"))
                throw new ArgumentException("房间地址必须为WebSocket地址", nameof(value));
            reconnectAttempts = 0;
            FailPending(-1);
            BeginConnect();
        }

        private void BeginConnect()
        {
            CloseSocket();
            long generation = ++attemptGeneration;
            socket = socketFactory();
            opened = () =>
            {
                if (disposed || generation != attemptGeneration) return;
                reconnectAttempts = 0;
                SetState(TransportState.Connected);
            };
            received = data => { if (!disposed && generation == attemptGeneration) Receive(data); };
            closed = _ => { if (!disposed && generation == attemptGeneration) HandleFailure(); };
            socket.Opened += opened;
            socket.Received += received;
            socket.Closed += closed;
            SetState(TransportState.Connecting);
            try { socket.Connect(address); }
            catch (Exception error)
            {
                log.Write(SdkLogLevel.Warning, "Session", "[MVXRSDK] WebSocket连接启动失败：" + error.Message);
                HandleFailure();
            }
        }

        public IDisposable Subscribe(string messageType, Action<int, byte[]> callback)
        {
            EnsureAlive();
            return subscriptions.Subscribe(messageType, callback);
        }

        public IDisposable Send(string api, byte[] payload, Action<int, byte[]> callback = null)
        {
            EnsureAlive();
            int split = api?.IndexOf('.') ?? -1;
            if (split <= 0 || split == api.Length - 1) throw new ArgumentException("消息接口必须为module.service", nameof(api));
            if (!IsConnected)
            {
                Invoke(callback, -1, null);
                return new DisposableAction(null);
            }
            if (requestId >= int.MaxValue)
            {
                Invoke(callback, -3, null);
                return new DisposableAction(null);
            }
            if ((callback != null && pending.Count >= options.NetworkLimits.MaximumPendingRequests) ||
                (payload != null && payload.Length > options.NetworkLimits.MaximumMessageBytes))
                return RejectCapacity(callback, "待应答请求数量或发送数据超出实例容量");
            int id = (int)++requestId;
            var message = new WSMessage
            {
                Module = api.Substring(0, split), ServiceName = api.Substring(split + 1),
                Data = ByteString.CopyFrom(payload ?? Array.Empty<byte>()), ReqId = id
            };
            if (message.CalculateSize() > options.NetworkLimits.MaximumMessageBytes - 1)
                return RejectCapacity(callback, "编码后的消息帧超出实例容量");
            if (callback != null) pending.Add(id, new Pending { Started = now(), Callback = callback });
            var content = message.ToByteArray();
            var frame = new byte[content.Length + 1];
            frame[0] = 2;
            Buffer.BlockCopy(content, 0, frame, 1, content.Length);
            try { socket.Send(frame); }
            catch (SocketOperationException error)
            {
                if (error.Result.Code == SdkErrorCode.CapacityExceeded) Complete(id, CapacityCode, null);
                else HandleFailure();
                Report(error.Result);
            }
            catch (Exception error)
            {
                log.Write(SdkLogLevel.Warning, "Session", "[MVXRSDK] WebSocket发送失败：" + error.Message);
                HandleFailure();
            }
            return new DisposableAction(() => Complete(id, CancelledCode, null));
        }

        private void Receive(byte[] frame)
        {
            if (frame == null || frame.Length == 0) return;
            if (frame.Length == 1 && frame[0] == 3)
            {
                CloseSocket();
                SetState(TransportState.Kicked);
                FailPending(-1);
                return;
            }
            WSResponse response;
            byte[] data;
            try
            {
                if (frame.Length > options.NetworkLimits.MaximumMessageBytes)
                    throw new SocketOperationException(SdkResult.Fail(SdkErrorCode.CapacityExceeded, "消息帧超出实例大小上限"));
                response = WSResponse.Parser.ParseFrom(frame);
                data = response.Data.ToByteArray();
                if (response.Zip) data = Decompress(data);
            }
            catch (SocketOperationException error)
            {
                Report(error.Result);
                HandleFailure();
                return;
            }
            catch (Exception error) when (error is InvalidProtocolBufferException || error is InvalidDataException || error is IOException)
            {
                log.Write(SdkLogLevel.Warning, "Session", "[MVXRSDK] 消息解析失败：" + error.Message);
                return;
            }
            if (Complete(response.ReqId, response.Code, data)) return;
            subscriptions.Publish(response.MessageType, response.Code, data);
        }

        private bool Complete(long id, int code, byte[] data)
        {
            if (!pending.TryGetValue(id, out var operation)) return false;
            // 先移除完成权，再进入业务回调，允许回调安全发起新请求。
            pending.Remove(id);
            Invoke(operation.Callback, code, data);
            return true;
        }

        public void Tick()
        {
            if (disposed) return;
            socket?.Tick();
            if (state == TransportState.Connecting && now() - stateStarted >= 10f) HandleFailure();
            else if (state == TransportState.WaitingRetry && now() >= nextRetry) BeginConnect();
            if (!IsConnected || pending.Count == 0) return;
            expired.Clear();
            foreach (var pair in pending)
                if (now() - pair.Value.Started >= options.RequestTimeoutSeconds) expired.Add(pair.Key);
            foreach (var id in expired) Complete(id, TimeoutCode, null);
        }

        private void HandleFailure()
        {
            if (disposed || (state != TransportState.Connected && state != TransportState.Connecting)) return;
            CloseSocket();
            reconnectAttempts++;
            if (reconnectAttempts > options.MaximumReconnectAttempts) SetState(TransportState.Failed);
            else
            {
                nextRetry = now() + Mathf.Min(30f, 2f * Mathf.Pow(2f, reconnectAttempts - 1));
                SetState(TransportState.WaitingRetry);
            }
            FailPending(-1);
        }

        public void Disconnect()
        {
            if (disposed) return;
            CloseSocket();
            SetState(TransportState.Disconnected);
            FailPending(-1);
        }

        private void CloseSocket()
        {
            ++attemptGeneration;
            if (socket == null) return;
            socket.Opened -= opened;
            socket.Received -= received;
            socket.Closed -= closed;
            socket.Dispose();
            socket = null;
        }

        private void SetState(TransportState next)
        {
            state = next;
            stateStarted = now();
            Changed?.Invoke(next);
        }

        private void FailPending(int code)
        {
            if (pending.Count == 0) return;
            var operations = new List<Pending>(pending.Values);
            pending.Clear();
            foreach (var operation in operations) Invoke(operation.Callback, code, null);
        }

        private void Invoke(Action<int, byte[]> callback, int code, byte[] data)
        {
            if (callback == null) return;
            try { callback(code, data); }
            catch (Exception error) { log.Exception("Session", error); }
        }

        private byte[] Decompress(byte[] data)
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > options.NetworkLimits.MaximumMessageBytes)
                    throw new SocketOperationException(SdkResult.Fail(SdkErrorCode.CapacityExceeded, "解压消息超出实例大小上限"));
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }

        private IDisposable RejectCapacity(Action<int, byte[]> callback, string message)
        {
            Invoke(callback, CapacityCode, null);
            Report(SdkResult.Fail(SdkErrorCode.CapacityExceeded, message));
            return new DisposableAction(null);
        }

        private void Report(SdkResult result)
        {
            if (report != null) report(result);
            else log.Write(SdkLogLevel.Warning, "Session", "[MVXRSDK][Transport] " + result.Code + ": " + result.Message);
        }

        private void EnsureAlive() { if (disposed) throw new ObjectDisposedException(nameof(SocketMessageTransport)); }
        public void Dispose()
        {
            if (disposed) return;
            Disconnect();
            disposed = true;
            subscriptions.Dispose();
            Changed = null;
        }
    }
}
