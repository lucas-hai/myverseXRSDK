using System;
using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 在音频线程驱动 AudioStreamTrack.SetData。com.unity.webrtc 文档明确要求 SetData
    /// 必须在音频线程调用（OnAudioFilterRead），主线程协程调用会导致采样时序错乱甚至 native crash。
    ///
    /// 实现：挂一个 silent loop AudioSource 触发 OnAudioFilterRead；回调里从 AudioMixingSystem
    /// 取混音 PCM 喂 track，并把传入的 data buffer 清零（避免本地耳朵听见 silent clip 之外的串扰）。
    ///
    /// 线程安全：m_Started 是 volatile bool；Dispose 顺序保证：StopFeeding() 先置 false +
    /// 停 AudioSource → 让出至少一个音频回调周期 → Destroy GameObject。
    /// 这避免了 OnAudioFilterRead 与 Destroy 的竞速。
    /// </summary>
    internal class AudioStreamFeeder : MonoBehaviour
    {
        private AudioStreamTrack m_Track;
        private int m_SampleRate;
        private AudioSource m_Source;
        // volatile：StopFeeding 在主线程置 false，OnAudioFilterRead 在音频线程读，必须避免重排序
        private volatile bool m_Started;
        // 复用 buffer 避免每次回调 alloc；OnAudioFilterRead 频率 ~21ms 一次（48k/1024），不复用就是 GC 热点
        private float[] m_TempBuf;

        /// <summary>由 AudioMixingSystem.AttachToTrack 在 AddComponent 之后立即调用。</summary>
        public void Initialize(AudioStreamTrack track)
        {
            m_Track = track;
            m_SampleRate = AudioSettings.outputSampleRate;

            // AudioMixingSystem 内部以 48k 缓存；若 Unity 输出采样率不是 48k，
            // SetData 时仍按 m_SampleRate 写，会被 WebRTC 内部重采样到协商的 opus 采样率
            if (m_SampleRate != 48000)
            {
                MVXRSDKLog.Warning($"AudioStreamFeeder: AudioSettings.outputSampleRate={m_SampleRate} ≠ 48000，依赖 WebRTC 内部重采样");
            }

            // 1s 静音 clip + loop 播放，是触发 OnAudioFilterRead 的最轻量手段
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.clip = AudioClip.Create("mvxrsdk_silent", m_SampleRate, 1, m_SampleRate, false);
            m_Source.loop = true;
            m_Source.playOnAwake = false;
            m_Source.volume = 0f;
            m_Source.Play();
            m_Started = true;
        }

        /// <summary>
        /// 主线程调用：安全停止馈送。置 volatile 标志 + 停 AudioSource。
        /// 注意：返回时 OnAudioFilterRead 可能仍在执行（音频线程的当前一次回调），
        /// 调用方应再 yield 一帧后再 Destroy 本组件，保证下一次回调看到的是 m_Started=false。
        /// </summary>
        public void StopFeeding()
        {
            m_Started = false;
            try
            {
                m_Source?.Stop();
            }
            catch
            {
                // Stop 在某些状态下可能抛，忽略
            }
        }

        // Unity 在音频线程调用——禁止在此处调 Unity 主线程 API（含 Debug.Log/MVXRSDKLog）
        private void OnAudioFilterRead(float[] data, int channels)
        {
            // 即使未启动也要清零 data，避免上一帧残留通过 silent clip 走漏
            if (!m_Started || m_Track == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            int frameSize = data.Length / channels;
            if (m_TempBuf == null || m_TempBuf.Length < frameSize)
            {
                // 首次回调或回调 frameSize 突变时一次性分配；之后稳态零分配
                m_TempBuf = new float[frameSize];
            }

            AudioMixingSystem.DequeueMixed(m_TempBuf, frameSize);

            try
            {
                m_Track.SetData(m_TempBuf, 1, m_SampleRate);
            }
            catch (ObjectDisposedException)
            {
                // track 已 dispose 但 feeder 还在跑——主线程的 DetachFromTrack 还没切到 m_Started=false
                // 音频线程不能打日志，静默吞掉
            }
            catch (NullReferenceException)
            {
                // 同上
            }

            Array.Clear(data, 0, data.Length);
        }

        private void OnDestroy()
        {
            m_Started = false;
            m_Track = null;
        }
    }
}
