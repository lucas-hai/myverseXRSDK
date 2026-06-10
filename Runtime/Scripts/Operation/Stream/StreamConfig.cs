namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流模块可配置参数。业务侧通过 <see cref="MVXRSDK.SetStreamConfig"/> 注入；
    /// 各 System / Module 通过 <see cref="Active"/> 读取最新生效配置。
    ///
    /// 设计为可变 POCO + 全局 Active 指针的模式：业务侧改完字段后调一次 Apply 即生效。
    /// 注：未在推流期间修改的字段才能稳定生效；推流进行中修改 Width/Height/Bandwidth
    /// 不会触发已建立 PeerConnection 重新协商，需 Stop → Start。
    /// </summary>
    public class StreamConfig
    {
        // === 视频参数 ===

        /// <summary>
        /// 推流目标帧率上限。CameraStreamCapture（cmd.CopyTexture）和 RenderTextureStreamSource
        /// （Graphics.Blit）都按此 fps 节流，避免编码器被设备 72/90Hz 主循环喂爆。
        /// 改动下一帧立即生效。
        /// </summary>
        public int Fps = 30;

        /// <summary>
        /// 推流 InternalRT 长边像素（固定 16:9，偶数对齐；默认 1280 → 1280x720）。
        /// v3 起 InternalRT 尺寸由本字段决定、与画面源无关：CameraStreamSource 相机按 RT
        /// 尺寸渲染（比例永远正确）；RenderTextureStreamSource Blit 缩放适配（非 16:9 外部
        /// RT 会拉伸）。≤0 时按 1280 处理。RT 创建后修改不重建，需 UnInit → Init。
        /// </summary>
        public int StreamMaxLongSide = 1280;

        /// <summary>视频带宽上限（kbps），写入 SDP 的 b=AS:N 行。</summary>
        public int VideoBandwidthKbps = 3500;

        /// <summary>
        /// 视频编码器输出下限（kbps）。落到 RTCRtpEncodingParameters.minBitrate。
        /// 作用：跳过 WebRTC GCC 慢启动期——libwebrtc 默认初始 BWE ~100kbps，编码器会
        /// 主动降 fps 到 2-5 直到 BWE 爬升完成（实测需 ~40s）。期间接收端 ffmpeg 按
        /// PTS 推算帧间隔可能 dup 帧卡死（PA9410 现象）。在局域网上行充足场景把下限
        /// 拉到 1.5Mbps，强制编码器从启动起即按目标 fps 输出，避免 PTS 步长不稳。
        /// 注：minBitrate ≤ maxBitrate（=VideoBandwidthKbps）；公网/拥塞链路谨慎调高。
        /// </summary>
        public int VideoMinBitrateKbps = 1500;

        /// <summary>是否强制 H.264 编码（删除 SDP 中其他视频 codec）。局域网+PICO 4U 推荐 true。</summary>
        public bool ForceH264 = true;

        // === 握手 / 网络参数 ===

        /// <summary>ICE 收集超时（秒）。non-trickle 模式下超时即失败；局域网 host candidate 一般 &lt; 1s 收齐。</summary>
        public int IceGatheringTimeoutSec = 3;

        /// <summary>WHIP HTTP 请求超时（秒），覆盖 POST 和 DELETE。</summary>
        public int WhipHttpTimeoutSec = 30;

        /// <summary>
        /// WHIP 握手协程全局超时（秒），覆盖 POST 重试总耗时 + 极端 transport 死锁兜底。
        /// 计算：单次 POST WhipHttpTimeoutSec × PostRetryDelaysMs.Length + sum(PostRetryDelaysMs) + buffer
        /// 默认 150 = 30×4 + 12 + 18 buffer，覆盖正常重试预算
        /// </summary>
        public int WhipHandshakeTimeoutSec = 150;

        /// <summary>WHIP DELETE 重试间隔（毫秒）。数组长度决定最多重试次数。</summary>
        public int[] DeleteRetryDelaysMs = { 1000, 3000, 8000 };

        /// <summary>
        /// 推流自动重试间隔（毫秒）。当 PushStreamModule 上报可恢复错误（ICE 失败 3008 /
        /// DTLS 握手失败 3007 / 连接丢失 3010 / ICE gathering 超时 3002）后，StreamManager
        /// 用最近一次 NotifyLive 缓存的 URL 按此节奏自动调 Start 重试，覆盖播控重启后
        /// 中控未及时再推 NotifyLive 的窗口。数组长度决定最多重试次数；空数组 = 不自动重试。
        /// 4xx 鉴权/找不到/codec 类错误不触发自动重试（配置问题，重试无意义）。
        /// </summary>
        public int[] PushStreamRetryDelaysMs = { 2000, 5000, 10000 };

        // === 容错参数 ===

        /// <summary>PeerConnectionState=Disconnected 后的自愈等待窗口（秒）。Phase 3.2 ICE Restart 使用。</summary>
        public int DisconnectedSelfHealSec = 5;

        /// <summary>实时 Stats 采集间隔（毫秒）。Phase 3.1 GetStats 使用。</summary>
        public int StatsReportIntervalMs = 1000;

        // === 全局 Active 单例 ===

        private static StreamConfig s_Active = new StreamConfig();

        /// <summary>当前生效配置。SDK 内部使用，不要在业务代码持有该引用做读改写。</summary>
        public static StreamConfig Active => s_Active;

        /// <summary>
        /// 替换当前生效配置。传 null 等价于恢复默认配置。
        /// MVXRSDK.SetStreamConfig 内部转调此方法。
        /// </summary>
        internal static void Apply(StreamConfig cfg)
        {
            s_Active = cfg ?? new StreamConfig();
            MVXRSDKLog.Info(
                $"StreamConfig 已生效：fps={s_Active.Fps} " +
                $"maxLongSide={s_Active.StreamMaxLongSide} " +
                $"bw={s_Active.VideoBandwidthKbps}kbps minBw={s_Active.VideoMinBitrateKbps}kbps H264={s_Active.ForceH264} " +
                $"iceGather={s_Active.IceGatheringTimeoutSec}s httpTimeout={s_Active.WhipHttpTimeoutSec}s " +
                $"statsInterval={s_Active.StatsReportIntervalMs}ms");
        }
    }
}
