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

    // 推流错误码已合并到 MVXRSDKErrorCode（4xxx 段）—— 见 MVXRSDK.cs。
}
