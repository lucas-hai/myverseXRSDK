using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>一次Listener采集绑定。释放只移除SDK的采集组件，不销毁或禁用业务Listener；旧句柄不影响新绑定。</summary>
    public sealed class AudioCaptureHandle : IDisposable
    {
        private Action<AudioCaptureHandle> release;
        private Action ensureMainThread;
        public AudioListener Listener { get; private set; }
        public bool IsDisposed { get; private set; }
        internal GameAudioInputTap Tap { get; private set; }

        internal AudioCaptureHandle(AudioListener listener, GameAudioInputTap tap, Action<AudioCaptureHandle> release, Action ensureMainThread)
        { Listener = listener; Tap = tap; this.release = release; this.ensureMainThread = ensureMainThread; }

        public void Dispose()
        {
            if (IsDisposed) return;
            ensureMainThread();
            IsDisposed = true;
            var callback = release;
            release = null;
            ensureMainThread = null;
            try { callback(this); }
            finally { Listener = null; Tap = null; }
        }
    }
}
