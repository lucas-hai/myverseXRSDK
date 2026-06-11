using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// MVXRSDK Facade 的推流 / 录屏 / 切镜 / 音频 PCM API。
    /// 拆分自原 MVXRSDK.cs（v2 整理），无行为变化；所有方法都是对 StreamManager 的转发。
    /// </summary>
    public static partial class MVXRSDK
    {
        // ============================== 推流状态查询 ==============================

        /// <summary>当前是否正在推流（已 Connected）。</summary>
        public static bool IsStreaming => StreamManager.IsStreaming;

        /// <summary>当前推流的 WHIP URL；未推流时返回 null。</summary>
        public static string CurrentStreamUrl => StreamManager.CurrentStreamUrl;

        /// <summary>同步获取最近一次推流统计快照；未推流或未采集到首帧 stats 时返回 null。</summary>
        public static StreamStats GetStreamStats() => StreamManager.GetStreamStats();

        // ============================== 画面源 ==============================

        /// <summary>
        /// 设置推流画面源：业务侧自己渲染到一张 RenderTexture（任意格式）。
        /// SDK 每帧把该 RT Blit 到内部合法格式 RT，自动处理 com.unity.webrtc 的格式约束。
        /// 性能：每帧多一次 Blit（约 0.2-0.5ms GPU）。
        /// </summary>
        public static void SetStreamSource(RenderTexture source)
        {
            StreamManager.SetStreamSource(source);
        }

        /// <summary>
        /// 设置推流画面源（高级用法）：业务侧自行实现 <see cref="IStreamSource"/>。
        /// 相比 <see cref="SetStreamSource(RenderTexture)"/>，业务可订阅 source.OnAttached/OnDetached
        /// 生命周期事件做精细控制（典型场景：切走时暂停自家采集 Blit 省 GPU，切回自动恢复）。
        /// MVXRStreamRig 切镜内部就是用此入口包装 RenderTextureStreamSource / CameraStreamSource。
        /// </summary>
        public static void SetStreamSource(IStreamSource source)
        {
            StreamManager.SetStreamSource(source);
        }

        /// <summary>清除画面源引用（推流停止后可选调用）。</summary>
        public static void ClearStreamSource()
        {
            StreamManager.ClearStreamSource();
        }

        /// <summary>
        /// 应用推流配置（分辨率/码率/超时/H.264 强制等）。传 null 恢复默认配置。
        /// 修改 Width/Height/Bandwidth 仅对后续 Start 生效，进行中的推流不会重协商。
        /// </summary>
        public static void SetStreamConfig(StreamConfig config)
        {
            StreamManager.SetStreamConfig(config);
        }

        // ============================== 录屏 ==============================

        /// <summary>
        /// 通知播控开始录屏。所有 pb 字段（含 PicoDeviceId、FileName、DurationSec 等）必须由游戏侧填充。
        /// 限时模式由 DurationSec 控制，服务端到时自动停止；本期无 StopRecord 接口。
        /// 结果异步走 OnRecordResult。
        /// </summary>
        public static void StartRecord(StartRecordOptions opts)
        {
            StreamManager.StartRecord(opts);
        }

        // ============================== 音频 PCM 推送 ==============================

        /// <summary>
        /// 推送游戏音频 PCM 给 SDK（推流唯一音频源；不推麦克风语音）。
        /// 推荐挂在 AudioListener 同 GameObject 的 OnAudioFilterRead 里调用。
        /// 支持 8000–192000 Hz（与设备输出率一致时直通零重采样，否则 SDK 线性重采样）；
        /// mono 或 stereo（stereo 内部自动平均成 mono）。
        /// </summary>
        /// <exception cref="ArgumentException">pcm 为 null 或采样率/通道数越界。</exception>
        public static void PushGameAudioPcm(float[] pcm, int sampleRate, int channels)
        {
            ValidatePcmArgs(pcm, sampleRate, channels, nameof(PushGameAudioPcm));
            StreamManager.PushGameAudioPcm(pcm, sampleRate, channels);
        }

        private static void ValidatePcmArgs(float[] pcm, int sampleRate, int channels, string apiName)
        {
            if (pcm == null)
                throw new ArgumentException($"{apiName}: pcm 不能为空", nameof(pcm));
            // 采样率由设备/采集源决定（PICO 4U 输出实测 24000、语音 SDK 常见 16000），
            // 不设白名单，只拦无意义入参；区间外视为调用方传错参数
            if (sampleRate < 8000 || sampleRate > 192000)
                throw new ArgumentException($"{apiName}: 不支持的采样率 {sampleRate}Hz（仅 8000–192000）", nameof(sampleRate));
            if (channels != 1 && channels != 2)
                throw new ArgumentException($"{apiName}: 不支持的通道数 {channels}（仅 mono=1 / stereo=2）", nameof(channels));
        }

        // ============================== 导播切镜头 ==============================

        /// <summary>
        /// 请求中控仲裁切镜（纯请求）：发 DirectorInsert.Request。受理结果走
        /// <see cref="OnDirectorRequestResult"/>；是否被选中以 NotifyLive 为准——被选中时
        /// SDK 触发 <see cref="OnPushStreamStarting"/>，业务在该回调中 SetStreamSource 接源。
        /// 请求切回原直播：opts.Source = <see cref="DirectorSource.Mr"/>（或留空）。
        /// </summary>
        public static void SendDirectorRequest(DirectorRequestOptions opts)
        {
            StreamManager.SendDirectorRequest(opts, null);
        }

        /// <summary>
        /// 请求中控仲裁切镜 + 被选中后自动接源（推荐）：opts.Source 留空自动填
        /// <see cref="DirectorSource.Unity"/>。被选中（NotifyLive start）时若业务未手动接源，
        /// SDK 自动把 <paramref name="camera"/> 包成 CameraStreamSource 接上；停流时自动清除。
        /// 业务在 <see cref="OnPushStreamStarting"/> 中手动接源优先于自动接源。
        /// pending 生命周期：新请求覆盖旧值；请求被拒清除；会话启动消费；相机销毁自动放弃。
        /// </summary>
        public static void SendDirectorRequest(DirectorRequestOptions opts, Camera camera)
        {
            StreamManager.SendDirectorRequest(opts, camera);
        }

        // ============================== Debug 入口 ==============================

        /// <summary>调试用：模拟播控下发 NotifyLive，绕过 WS。Offline 模式下是唯一启动推流的入口。</summary>
        public static void Debug_SimulateNotifyLive(string streamServerIp, bool start)
        {
            StreamManager.Debug_SimulateNotifyLive(streamServerIp, start);
        }
    }
}
