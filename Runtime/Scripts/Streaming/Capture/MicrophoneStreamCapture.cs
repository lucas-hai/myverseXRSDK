using System;
using System.Collections;
using UnityEngine;

namespace MyVerseXRSDK.Streaming
{
    /// <summary>
    /// 麦克风抓取：Microphone.Start → 定时（约 50ms）拉新样本 → PushMicPcm。
    ///
    /// SDK 不主动开 AudioRecord，避免与 Pico 语音 SDK 抢麦；要推麦必须由游戏侧采集并喂进来。
    /// SDK 仅支持 48k / 44.1k 采样率。
    ///
    /// 实现细节：内部创建一个 hidden GameObject 跑协程驱动 pump，
    /// 调用方不需要持有 MonoBehaviour；Dispose 时清理。
    /// </summary>
    public sealed class MicrophoneStreamCapture : IDisposable
    {
        private GameObject m_HolderGO;
        private Pump m_Pump;
        private bool m_Disposed;

        /// <summary>
        /// 创建并启动麦克风采集。
        /// </summary>
        /// <param name="device">麦克风设备名，null 使用系统默认（Microphone.devices[0]）</param>
        /// <param name="sampleRate">采样率，建议 48000</param>
        public MicrophoneStreamCapture(string device = null, int sampleRate = 48000)
        {
            m_HolderGO = new GameObject("__MVXR_MicCapture__")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            m_Pump = m_HolderGO.AddComponent<Pump>();
            m_Pump.Init(device, sampleRate);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            // Destroy hidden GO → Pump.OnDestroy 负责 Microphone.End
            if (m_HolderGO != null)
            {
                UnityEngine.Object.Destroy(m_HolderGO);
                m_HolderGO = null;
                m_Pump = null;
            }
        }

        internal sealed class Pump : MonoBehaviour
        {
            private string m_Device;
            private int m_RequestedSampleRate;
            private AudioClip m_Clip;
            private bool m_Running;

            public void Init(string device, int sampleRate)
            {
                m_RequestedSampleRate = sampleRate;
                m_Device = device;
                StartCoroutine(Co_Pump());
            }

            private IEnumerator Co_Pump()
            {
                if (Microphone.devices == null || Microphone.devices.Length == 0)
                {
                    MVXRSDKLog.Warning("MicrophoneStreamCapture: 当前设备无可用麦克风");
                    yield break;
                }
                if (string.IsNullOrEmpty(m_Device)) m_Device = Microphone.devices[0];

                const int bufferSec = 10;
                m_Clip = Microphone.Start(m_Device, true, bufferSec, m_RequestedSampleRate);
                if (m_Clip == null)
                {
                    MVXRSDKLog.Error($"MicrophoneStreamCapture: Microphone.Start 失败 device={m_Device}");
                    yield break;
                }

                while (Microphone.GetPosition(m_Device) <= 0) yield return null;
                MVXRSDKLog.Info($"MicrophoneStreamCapture: 麦克风已开 device={m_Device} sampleRate={m_Clip.frequency} channels={m_Clip.channels}");
                m_Running = true;

                int channels = m_Clip.channels;
                int sampleRate = m_Clip.frequency;
                int clipSamples = m_Clip.samples; // per-channel
                int lastPos = Microphone.GetPosition(m_Device);
                int maxFramesPerPull = sampleRate / 20; // 每次至多读 ~50ms，避免抖动一次拷过大

                while (m_Running)
                {
                    yield return null;
                    int pos = Microphone.GetPosition(m_Device);
                    if (pos == lastPos) continue;

                    int frames = pos > lastPos ? (pos - lastPos) : (clipSamples - lastPos + pos);
                    if (frames <= 0) continue;
                    if (frames > maxFramesPerPull) frames = maxFramesPerPull;

                    // GetData 读取量 = data.Length / channels 帧；按 frames 精确分配
                    var segment = new float[frames * channels];
                    if (m_Clip.GetData(segment, lastPos))
                    {
                        MVXRSDK.PushMicPcm(segment, sampleRate, channels);
                    }
                    lastPos = (lastPos + frames) % clipSamples;
                }
            }

            private void OnDestroy()
            {
                m_Running = false;
                if (!string.IsNullOrEmpty(m_Device) && Microphone.IsRecording(m_Device))
                {
                    Microphone.End(m_Device);
                    MVXRSDKLog.Info($"MicrophoneStreamCapture: 麦克风已关闭 device={m_Device}");
                }
            }
        }
    }
}
