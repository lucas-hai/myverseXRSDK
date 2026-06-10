using UnityEngine;

namespace MyVerseXRSDK.Streaming
{
    /// <summary>
    /// 推流音频采集 + 配置应用装配 MonoBehaviour（v3 切镜化重构后）。
    ///
    /// 本版本 Rig **不承载画面推流**：画面源管理由业务直接调 SDK 公共 API——
    /// 推荐 MVXRSDK.SendDirectorRequest(opts, camera) 一步完成"请求 + 被选中自动接源 +
    /// 停流自动清"；需要精细控制时手动订阅 OnPushStreamStarting 接源 /
    /// OnPushStreamStopped 清源。多场景约定见 Documentation~ 的"多场景使用"一节。
    ///
    /// 职责：
    /// - OnEnable 时应用 StreamConfigAsset（写入 StreamConfig.Active）
    /// - 装配游戏音采集（AudioListener tap → PushGameAudioPcm）
    /// - 装配麦克风采集（Microphone → PushMicPcm，注意与语音 SDK 抢麦）
    /// </summary>
    [AddComponentMenu("MyVerse XR SDK/Stream Rig")]
    [DisallowMultipleComponent]
    public sealed class MVXRStreamRig : MonoBehaviour
    {
        [Header("音频源")]
        [Tooltip("游戏音 AudioListener：通过 OnAudioFilterRead 抓 master mix。留空则不推游戏音。")]
        public AudioListener gameAudioListener;

        [Tooltip("是否采集麦克风推给 SDK。注意会占用麦克风设备，可能与 Pico 语音 SDK 冲突。")]
        public bool captureMicrophone = false;

        [Tooltip("麦克风采样率，SDK 仅支持 48000 / 44100。")]
        public int micSampleRate = 48000;

        [Tooltip("麦克风设备名，留空使用系统默认。")]
        public string micDevice = "";

        [Header("推流配置 Asset")]
        [Tooltip("推流视频编码参数 Asset（Fps / 长边像素 / 码率 / H.264）。\n" +
                 "创建方式：Project 右键 → Create → MyVerse XR SDK → Stream Config。\n" +
                 "Rig OnEnable 时自动 Apply 写入 StreamConfig.Active。\n" +
                 "留空时全部走 SDK 默认（Fps=30 / 长边 1280 / 码率 3500 / H.264 强制）。")]
        public StreamConfigAsset streamConfigAsset;

        private GameAudioStreamCapture m_GameAudio;
        private MicrophoneStreamCapture m_Microphone;

        private void OnEnable()
        {
            if (streamConfigAsset != null)
            {
                streamConfigAsset.Apply();
            }

            if (gameAudioListener != null)
            {
                m_GameAudio = new GameAudioStreamCapture(gameAudioListener);
            }

            if (captureMicrophone)
            {
                m_Microphone = new MicrophoneStreamCapture(
                    string.IsNullOrEmpty(micDevice) ? null : micDevice,
                    micSampleRate);
            }
        }

        private void OnDisable()
        {
            m_GameAudio?.Dispose();
            m_GameAudio = null;
            m_Microphone?.Dispose();
            m_Microphone = null;
        }
    }
}
