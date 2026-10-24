using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class MediaAudioFeeder : MonoBehaviour
    {
        private volatile MediaAudioPump pump;
        private AudioSource source;
        private AudioClip clip;

        internal void Initialize(MediaAudioPump audioPump, int sampleRate)
        {
            pump = audioPump;
            source = gameObject.AddComponent<AudioSource>();
            clip = AudioClip.Create("MVXRSDK静音时钟", sampleRate, 1, sampleRate, false);
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0;
            source.Play();
        }

        internal void StopFeeding()
        {
            pump?.Close();
            if (source != null) source.Stop();
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            var current = pump;
            if (current == null) Array.Clear(data, 0, data.Length);
            else current.Process(data, channels);
        }

        private void OnDestroy()
        {
            pump?.Close();
            pump = null;
            if (source != null) source.clip = null;
            if (clip == null) return;
            if (Application.isPlaying) Destroy(clip);
            else DestroyImmediate(clip);
            clip = null;
        }
    }
}
