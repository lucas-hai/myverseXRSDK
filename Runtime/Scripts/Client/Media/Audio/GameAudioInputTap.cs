using System.Threading;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class GameAudioInputTap : MonoBehaviour
    {
        private volatile GameAudioBuffer buffer;
        private volatile int sampleRate;
        private volatile bool capturing;
        private bool subscribed;
        private int lastError;
        private int pendingError;
        internal bool IsBound => buffer != null;

        internal void Initialize(GameAudioBuffer input)
        {
            buffer = input;
            capturing = enabled && gameObject.activeInHierarchy;
            UpdateSampleRate(false);
            AudioSettings.OnAudioConfigurationChanged += UpdateSampleRate;
            subscribed = true;
        }

        private void OnEnable() { capturing = true; UpdateSampleRate(false); }
        private void OnDisable() => capturing = false;
        private void OnDestroy() => Detach();

        private void UpdateSampleRate(bool deviceWasChanged)
        {
            int currentRate = AudioSettings.outputSampleRate;
            sampleRate = currentRate > 0 ? currentRate : buffer?.SampleRate ?? 48000;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var input = buffer;
            if (!capturing || input == null) return;
            // 不改data：同一块音频还会继续传给扬声器。
            var result = input.Push(data, sampleRate, channels);
            int code = result.Code == SdkErrorCode.Disposed ? 0 : (int)result.Code;
            int previous = Interlocked.Exchange(ref lastError, code);
            if (code != 0 && previous != code) Interlocked.Exchange(ref pendingError, code);
        }

        internal SdkResult TakeError()
        {
            int code = Interlocked.Exchange(ref pendingError, 0);
            return code == 0 ? SdkResult.Ok : SdkResult.Fail((SdkErrorCode)code, "Listener游戏音格式无效，需要完整mono/stereo采样帧");
        }

        internal void Detach()
        {
            capturing = false;
            buffer = null;
            if (!subscribed) return;
            subscribed = false;
            AudioSettings.OnAudioConfigurationChanged -= UpdateSampleRate;
        }
    }
}
