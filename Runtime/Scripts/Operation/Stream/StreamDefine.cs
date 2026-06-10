namespace MyVerseXRSDK
{
    // 推流状态机
    internal enum PushStreamState
    {
        Idle,
        Starting,
        Started
    }

    /// <summary>
    /// 启动录屏的参数集，对应 pb StartRecord.Types.Request 的 5 个字段。
    /// 所有字段由游戏侧传入，SDK 仅做参数转发不做实际录制。
    /// </summary>
    public sealed class StartRecordOptions
    {
        /// <summary>是否为真实摄像头，true 时使用 CameraId，false 时使用 PicoDeviceId。</summary>
        public bool RealCamera;

        /// <summary>真实摄像头 ID（RealCamera=true 时使用）。</summary>
        public string CameraId = string.Empty;

        /// <summary>录制时长（秒），到时由服务端自动停止。</summary>
        public int DurationSec;

        /// <summary>录制文件名（不含扩展名）。</summary>
        public string FileName = string.Empty;

        /// <summary>Pico 设备 ID（RealCamera=false 时使用）。</summary>
        public string PicoDeviceId = string.Empty;
    }

    /// <summary>
    /// DirectorInsert.source 机位来源常量。proto 注释（语义权威）：
    /// "unity"=本机 Unity 游戏内机位; 空/"mr"=原直播。
    /// 空字符串是合法协议值（=原直播），SDK 不做默认填充。
    /// </summary>
    public static class DirectorSource
    {
        /// <summary>本机 Unity 游戏内机位（具体推哪个相机是本地决策，中控不感知）。</summary>
        public const string Unity = "unity";

        /// <summary>原直播（播控第一视角）。空字符串等效。</summary>
        public const string Mr = "mr";
    }

    /// <summary>
    /// 切镜请求参数集，对应 pb DirectorInsert.Types.Request 的 4 个字段。
    /// 与 <see cref="StartRecordOptions"/> 同风格；协议加字段时在此扩展不破坏调用方。
    /// </summary>
    public struct DirectorRequestOptions
    {
        /// <summary>机位来源，见 <see cref="DirectorSource"/>。空 = 原直播，SDK 原样透传。
        /// 注意：camera 自动接源重载下留空会被自动填为 "unity"（传相机即明确本机机位）。</summary>
        public string Source;

        /// <summary>镜头数：1=单镜头全屏 / 2=双拼 / 3=品字 / 4=2x2 四宫格。&lt;1 时按 1 处理。</summary>
        public int Lenses;

        /// <summary>这步持续秒数，必须 &gt; 0；到期由服务端停流，客户端不做本地倒计时。</summary>
        public int DurationSec;

        /// <summary>是否录制这一段（服务端执行）。</summary>
        public bool Record;
    }

    // 推流错误码已合并到 MVXRSDKErrorCode（4xxx 段）—— 见 MVXRSDK.cs。
}
