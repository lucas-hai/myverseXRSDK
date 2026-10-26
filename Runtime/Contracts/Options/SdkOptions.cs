using System;

namespace MyVerseXRSDK
{
    public enum SessionConnectionMode { Directory, ControlServer, External }

    /// <summary>客户端创建配置。工厂捕获独立快照，创建后修改原对象不会改变运行实例。</summary>
    public sealed class SdkOptions
    {
        public string DeviceId { get; set; }
        public SessionOptions Session { get; set; } = new SessionOptions();
        public SpatialOptions Spatial { get; set; } = new SpatialOptions();
        public DeviceOptions Devices { get; set; } = new DeviceOptions();
        /// <summary>显式启用本机推流，默认关闭；创建后不能通过媒体参数或Rig隐式开启。</summary>
        public bool EnableStreaming { get; set; }
        public MediaOptions Media { get; set; } = new MediaOptions();
        public SdkLogOptions Logging { get; set; } = new SdkLogOptions();
        /// <summary>会话终态失败时在已绑定Head前显示默认提示；自定义业务UI可关闭此项。</summary>
        public bool ShowNetworkFailureHud { get; set; } = true;

        internal SdkOptions Capture()
        {
            if (string.IsNullOrWhiteSpace(DeviceId) || DeviceId.Length > 64 ||
                DeviceId.IndexOfAny(new[] { ' ', '/', '?', '#' }) >= 0)
                throw new ArgumentException("设备标识不能为空、超过64字符或包含空格及URL分隔符", nameof(DeviceId));
            if (Session == null) throw new ArgumentException("会话配置不能为空", nameof(Session));
            if (Spatial == null || Devices == null) throw new ArgumentException("空间和设备配置不能为空");
            var spatial = Spatial.Capture();
            var devices = Devices.Capture();
            if (spatial.Mode != SpatialMode.XrCalibration &&
                (spatial.ShowObstacles || spatial.ShowFloor || devices.ReportingEnabled || devices.AvatarsEnabled))
                throw new ArgumentException("非XR应用模式不能启用空间代理或本机位姿上报");
            if (Media == null) throw new ArgumentException("媒体配置不能为空");
            if (Logging == null) throw new ArgumentException("日志配置不能为空");
            return new SdkOptions
            {
                DeviceId = DeviceId, Session = Session.Capture(), Spatial = spatial, Devices = devices,
                EnableStreaming = EnableStreaming, Media = Media.Capture(), Logging = Logging.Capture(), ShowNetworkFailureHud = ShowNetworkFailureHud
            };
        }
    }

    /// <summary>接入与请求策略；中控地址和账务地址分别配置。</summary>
    public sealed class SessionOptions
    {
        public SessionConnectionMode Mode { get; set; } = SessionConnectionMode.Directory;
        public string ControlServerUrl { get; set; }
        public string BillingBaseUrl { get; set; }
        public float RequestTimeoutSeconds { get; set; } = 30f;
        public float AllocationIntervalSeconds { get; set; } = 1f;
        public int MaximumReconnectAttempts { get; set; } = 5;
        public NetworkLimits NetworkLimits { get; set; } = new NetworkLimits();

        internal SessionOptions Capture()
        {
            if (!Enum.IsDefined(typeof(SessionConnectionMode), Mode))
                throw new ArgumentException("未知接入模式", nameof(Mode));
            if (!PositiveFinite(RequestTimeoutSeconds) || !PositiveFinite(AllocationIntervalSeconds) || MaximumReconnectAttempts < 0)
                throw new ArgumentException("会话超时/轮询间隔必须为有限正数，重连次数不能为负");
            if (Mode == SessionConnectionMode.ControlServer) ValidateHttp(ControlServerUrl, nameof(ControlServerUrl));
            if (!string.IsNullOrEmpty(BillingBaseUrl)) ValidateHttp(BillingBaseUrl, nameof(BillingBaseUrl));
            if (NetworkLimits == null) throw new ArgumentException("网络容量配置不能为空");
            var limits = NetworkLimits.Capture();
            return new SessionOptions
            {
                Mode = Mode, ControlServerUrl = ControlServerUrl,
                BillingBaseUrl = BillingBaseUrl, RequestTimeoutSeconds = RequestTimeoutSeconds,
                AllocationIntervalSeconds = AllocationIntervalSeconds, MaximumReconnectAttempts = MaximumReconnectAttempts,
                NetworkLimits = limits
            };
        }

        private static bool PositiveFinite(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static void ValidateHttp(string value, string name)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var address) ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("地址必须为完整的HTTP或HTTPS地址", name);
        }
    }
}
