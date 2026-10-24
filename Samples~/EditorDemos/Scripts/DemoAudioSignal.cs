using System;
using System.Threading;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>低音量游戏测试音；可直接提交PCM，或仅发声让Listener采集。音频线程不记录日志、不分配缓冲。</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class DemoAudioSignal : MonoBehaviour
    {
        private volatile StreamingService pcmTarget;
        private volatile bool generating;
        private volatile int sampleRate = 48000;
        private double phase;
        private int lastError;
        private AudioClip silence;
        private AudioSource source;
        public SdkErrorCode LastPcmError => (SdkErrorCode)Volatile.Read(ref lastError);
        private void OnEnable() { UpdateRate(false); AudioSettings.OnAudioConfigurationChanged += UpdateRate; }
        private void OnDisable() { AudioSettings.OnAudioConfigurationChanged -= UpdateRate; StopTone(); }
        private void UpdateRate(bool changed) { int rate = AudioSettings.outputSampleRate; sampleRate = rate > 0 ? rate : 48000; }

        public void StartTone(StreamingService directPcmTarget)
        {
            source = GetComponent<AudioSource>();
            if (silence == null) silence = AudioClip.Create("EditorDemo.Silence", 48000, 1, 48000, false);
            source.clip = silence; source.loop = true; source.spatialBlend = 0;
            pcmTarget = directPcmTarget; generating = true; source.Play();
        }
        public void StopTone() { pcmTarget = null; generating = false; if (source != null) source.Stop(); }
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!generating || channels <= 0) return;
            int rate = sampleRate;
            double step = Math.PI * 2 * 440 / rate;
            for (int frame = 0; frame < data.Length; frame += channels)
            {
                float value = (float)Math.Sin(phase) * 0.05f;
                phase += step; if (phase >= Math.PI * 2) phase -= Math.PI * 2;
                for (int channel = 0; channel < channels && frame + channel < data.Length; channel++) data[frame + channel] = value;
            }
            var target = pcmTarget;
            if (target == null) return;
            var result = target.PushGameAudioPcm(data, rate, channels);
            Interlocked.Exchange(ref lastError, result.Code == SdkErrorCode.Disposed ? 0 : (int)result.Code);
        }
        private void OnDestroy()
        {
            StopTone();
            if (silence != null)
            { if (Application.isPlaying) Destroy(silence); else DestroyImmediate(silence); }
        }
    }
}
