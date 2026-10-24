using System;
using Google.Protobuf;
using Newtonsoft.Json.Linq;

namespace MyVerseXRSDK
{
    internal sealed class NativeSessionConnection : IDisposable
    {
        private const string DirectoryEndpoint = "http://localhost:8868/v1/vr/service/config";
        private readonly SdkRuntimeContext context;
        private readonly SessionService session;
        private readonly ICommandTransport commands;
        private readonly IHttpRequestRunner http;
        private readonly Func<float> now;
        private NativeMessageReceiver receiver;
        private IDisposable httpRequest;
        private string allocationUrl;
        private string roomAddress;
        private long generation;
        private long loginAttempt;
        private float nextPoll;
        private bool requestPending;
        private bool active;
        private bool onlineReported;
        private bool disposed;
        internal string BillingBaseUrl { get; private set; }
        internal ICommandTransport Commands => commands;
        private bool IsActive => active && !disposed && !context.IsDisposed && !context.IsClosing;

        internal NativeSessionConnection(SdkRuntimeContext context, SessionService session, ICommandTransport commands,
            IHttpRequestRunner http, Func<float> now)
        {
            this.context = context;
            this.session = session;
            this.commands = commands;
            this.http = http;
            this.now = now;
            BillingBaseUrl = context.Options.Session.BillingBaseUrl;
        }

        internal void Start()
        {
            if (active || disposed) throw new InvalidOperationException("原生接入不能重复启动");
            active = true;
            ++generation;
            commands.Changed += OnTransport;
            receiver = new NativeMessageReceiver(session, context.Options.DeviceId, commands, () => active && !disposed);
            if (context.Options.Session.Mode == SessionConnectionMode.Directory) Discover();
            else ConfigureControl(context.Options.Session.ControlServerUrl);
            Tick();
        }

        private void Discover()
        {
            session.UpdateConnection(SessionState.Discovering);
            if (!IsActive) return;
            long captured = generation;
            requestPending = true;
            httpRequest = http.Begin("POST", DirectoryEndpoint, null, reply =>
            {
                if (!IsCurrent(captured)) return;
                requestPending = false;
                if (!reply.Result.Success) { Fail(reply.Result); return; }
                try
                {
                    var response = JObject.Parse(reply.Body);
                    var data = response["data"];
                    if (response["code"] == null || response.Value<int>("code") != 0 || data == null)
                    { Fail(SdkResult.Fail(SdkErrorCode.InvalidResponse, "目录响应无有效配置")); return; }
                    BillingBaseUrl = (string)data["baseUrl"];
                    ConfigureControl((string)data["serveIp"]);
                }
                catch (Exception error) { Fail(SdkResult.Fail(SdkErrorCode.InvalidResponse, "目录响应解析失败：" + error.Message)); }
            });
        }

        private void ConfigureControl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            { Fail(SdkResult.Fail(SdkErrorCode.InvalidResponse, "中控地址无效")); return; }
            allocationUrl = url.TrimEnd('/') + "/v1/open/api/device?id=" + Uri.EscapeDataString(context.Options.DeviceId);
            session.UpdateConnection(SessionState.Allocating);
            nextPoll = now();
        }

        internal void Tick()
        {
            if (!IsActive) return;
            commands.Tick();
            if (!IsActive) return;
            if (requestPending || string.IsNullOrEmpty(allocationUrl) || now() < nextPoll) return;
            long captured = generation;
            requestPending = true;
            httpRequest = http.Begin("GET", allocationUrl, null, reply =>
            {
                if (!IsCurrent(captured)) return;
                requestPending = false;
                nextPoll = now() + context.Options.Session.AllocationIntervalSeconds;
                if (!reply.Result.Success) { session.ReportError(reply.Result); return; }
                try
                {
                    ApplyAllocationResponse(JObject.Parse(reply.Body));
                }
                catch (Exception error) { session.ReportError(SdkResult.Fail(SdkErrorCode.InvalidResponse, "分配响应解析失败：" + error.Message)); }
            });
        }

        private void ApplyAllocationResponse(JObject response)
        {
            if (response["Code"]?.Type != JTokenType.Integer)
            { session.ReportError(SdkResult.Fail(SdkErrorCode.InvalidResponse, "设备分配响应Code必须为整数")); return; }
            if (response.Value<int>("Code") != 0)
            { session.ReportError(SdkResult.Fail(SdkErrorCode.RemoteRejected, "中控拒绝设备分配查询")); return; }
            var address = (response["Data"] as JObject)?["CenterIp"];
            // 只有明确的空字符串表示解散；缺字段或类型错误不能撤销现有房间。
            if (address?.Type != JTokenType.String)
            { session.ReportError(SdkResult.Fail(SdkErrorCode.InvalidResponse, "设备分配响应缺少字符串CenterIp")); return; }
            string value = (string)address;
            if (value.Length != 0 && (!Uri.TryCreate(value.Contains("://") ? value : "ws://" + value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "ws" && uri.Scheme != "wss")))
            { session.ReportError(SdkResult.Fail(SdkErrorCode.InvalidResponse, "分配的房间地址不是有效WebSocket地址")); return; }
            ApplyAllocation(value);
        }

        private void ApplyAllocation(string address)
        {
            if (string.Equals(address ?? string.Empty, roomAddress ?? string.Empty, StringComparison.Ordinal)) return;
            long captured = generation;
            ++loginAttempt;
            ReportOnline(false);
            commands.Disconnect();
            session.EndSession();
            if (!IsCurrent(captured)) return;
            roomAddress = address;
            if (string.IsNullOrWhiteSpace(address)) { session.UpdateConnection(SessionState.Allocating); return; }
            session.UpdateConnection(SessionState.Connecting);
            if (IsCurrent(captured)) commands.Connect(address);
        }

        private void OnTransport(TransportState state)
        {
            if (!IsActive) return;
            switch (state)
            {
                case TransportState.Connected: RequestLogin(); break;
                case TransportState.Connecting: session.UpdateConnection(SessionState.Connecting); break;
                case TransportState.WaitingRetry:
                    ++loginAttempt;
                    session.UpdateConnection(SessionState.Reconnecting);
                    break;
                case TransportState.Failed:
                case TransportState.Kicked:
                    ++loginAttempt;
                    Fail(SdkResult.Fail(SdkErrorCode.TransportFailed,
                        state == TransportState.Kicked ? "设备被服务端踢线" : "房间重连次数耗尽"));
                    break;
            }
        }

        private void RequestLogin()
        {
            long attempt = ++loginAttempt;
            long captured = generation;
            session.UpdateConnection(SessionState.Authenticating);
            if (!IsCurrent(captured) || attempt != loginAttempt) return;
            var request = new Login.Types.Request { Token = context.Options.DeviceId };
            commands.Send("logic.Binding", request.ToByteArray(), (code, data) =>
            {
                if (!IsCurrent(captured) || attempt != loginAttempt) return;
                if (code != 0 || !ProtocolMapper.TryParse(Login.Types.Response.Parser, data, out var response, context.Log) ||
                    string.IsNullOrEmpty(response.RoomAddress?.RoomId))
                { Fail(SdkResult.Fail(SdkErrorCode.InvalidResponse, "房间登录未成功或响应缺少房间标识")); return; }
                long epoch = session.BeginSession(response.RoomAddress.RoomId);
                if (!IsCurrent(captured) || epoch != session.Current.Epoch || !session.Current.IsJoined) return;
                if (response.RegionInfo != null) receiver.ApplyRegion(response.RegionInfo);
                if (!IsCurrent(captured) || epoch != session.Current.Epoch || !session.Current.IsJoined) return;
                QueryObstacles(epoch, attempt);
                ReportOnline(true);
            });
        }

        private void QueryObstacles(long epoch, long attempt)
        {
            commands.Send("logic.QueryGameSceneInfo", new QueryGameSceneInfo.Types.Request().ToByteArray(), (code, data) =>
            {
                if (!IsActive || attempt != loginAttempt || epoch != session.Current.Epoch) return;
                if (code == 0 && ProtocolMapper.TryParse(QueryGameSceneInfo.Types.Response.Parser, data, out var response, context.Log))
                    receiver.ApplyObstacles(response.GameSceneInfo);
                else session.ReportError(SdkResult.Fail(SdkErrorCode.InvalidResponse, "障碍物查询失败"));
            });
        }

        internal void SendPose(DevicePose pose)
        {
            if (IsActive && commands.IsConnected && session.Current.IsJoined)
                commands.Send("logic.SynPosition", ProtocolMapper.EncodeDevice(pose));
        }

        internal void RestartReferenceFrame()
        {
            if (!IsActive) return;
            ++generation;
            ++loginAttempt;
            httpRequest?.Dispose();
            requestPending = false;
            nextPoll = now();
            commands.Disconnect();
            session.UpdateConnection(SessionState.Reconnecting);
            if (IsActive && !string.IsNullOrEmpty(roomAddress)) commands.Connect(roomAddress);
        }

        private void ReportOnline(bool online)
        {
            if (online && !IsActive || !commands.IsConnected || !online && !onlineReported) return;
            var request = new UpdateDeviceOnlineStatus.Types.Request { DeviceId = context.Options.DeviceId, Online = online };
            commands.Send("logic.UpdateDeviceOnlineStatus", request.ToByteArray());
            onlineReported = online;
        }

        private bool IsCurrent(long captured) => IsActive && generation == captured;
        private void Fail(SdkResult result)
        {
            long captured = generation;
            session.ReportError(result);
            if (IsCurrent(captured)) session.UpdateConnection(SessionState.Failed);
        }

        public void Dispose()
        {
            if (disposed) return;
            ++generation;
            ++loginAttempt;
            active = false;
            ReportOnline(false);
            disposed = true;
            httpRequest?.Dispose();
            receiver?.Dispose();
            commands.Changed -= OnTransport;
            commands.Dispose();
            http.Dispose();
            session.EndSession();
        }
    }
}
