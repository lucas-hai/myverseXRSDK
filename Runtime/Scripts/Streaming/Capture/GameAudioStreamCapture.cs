using System;
using UnityEngine;

namespace MyVerseXRSDK.Streaming
{
    /// <summary>
    /// 游戏音抓取：在 AudioListener 同 GameObject 上挂一个 OnAudioFilterRead helper，
    /// Unity 引擎会在 audio thread 把整机 master mix 喂进来，我们原样转发给 SDK。
    ///
    /// OnAudioFilterRead 不修改 data 不会影响扬声器输出（filter 链约定）。
    /// SDK 内部 PushGameAudioPcm 用 lock 做了线程同步，在 audio thread 调用安全。
    /// 采样率跟随设备输出（AudioSettings.outputSampleRate，PICO 4U 实测 24000），
    /// 与混音工作率一致直通；SDK 接受 8k–192k。
    /// </summary>
    public sealed class GameAudioStreamCapture : IDisposable
    {
        private Tap m_Tap;
        private bool m_Disposed;

        public GameAudioStreamCapture(AudioListener listener)
        {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            // OnAudioFilterRead 必须挂在 AudioListener 同 GameObject 上才能拿到 master mix
            m_Tap = listener.gameObject.AddComponent<Tap>();
            MVXRSDKLog.Info($"GameAudioStreamCapture: tap mounted on '{listener.name}' sampleRate={AudioSettings.outputSampleRate}");
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            if (m_Tap != null)
            {
                UnityEngine.Object.Destroy(m_Tap);
                m_Tap = null;
            }
        }

        /// <summary>
        /// 内部 helper：OnAudioFilterRead 在 audio thread 被调用。
        /// 标 internal + DisallowMultipleComponent，避免被外部误挂。
        /// </summary>
        [DisallowMultipleComponent]
        internal sealed class Tap : MonoBehaviour
        {
            private int m_SampleRate;

            private void OnEnable()
            {
                // AudioSettings.outputSampleRate 必须主线程读，audio thread 不行 → 在此缓存
                m_SampleRate = AudioSettings.outputSampleRate;
            }

            private void OnAudioFilterRead(float[] data, int channels)
            {
                // data 是交错 PCM；不修改即可，filter chain 会把它继续传给扬声器
                MVXRSDK.PushGameAudioPcm(data, m_SampleRate, channels);
            }
        }
    }
}
