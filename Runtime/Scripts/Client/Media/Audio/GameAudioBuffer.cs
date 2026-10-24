using System;

namespace MyVerseXRSDK
{
    /// <summary>单实例游戏PCM环形缓冲；输入折叠为mono并连续重采样，写满丢最旧，稳态不分配。</summary>
    internal sealed class GameAudioBuffer : IDisposable
    {
        private readonly object gate = new object();
        private readonly float[] samples;
        private int readIndex;
        private int writeIndex;
        private int count;
        private int inputRate;
        private double resamplePosition;
        private float lastSample;
        private bool hasLastSample;
        private bool disposed;
        internal int SampleRate { get; }

        internal GameAudioBuffer(int sampleRate, int capacity)
        {
            if (sampleRate <= 0 || capacity <= 0) throw new ArgumentOutOfRangeException();
            SampleRate = sampleRate;
            samples = new float[capacity];
        }

        internal SdkResult Push(float[] pcm, int sampleRate, int channels)
        {
            lock (gate)
            {
                if (disposed) return SdkResult.Fail(SdkErrorCode.Disposed, "音频缓冲已关闭");
                if (pcm == null || channels < 1 || channels > 2 || sampleRate < 8000 || sampleRate > 192000 || pcm.Length % channels != 0)
                    return SdkResult.Fail(SdkErrorCode.InvalidArgument, "PCM需要完整的mono/stereo帧，采样率须在8000–192000Hz之间");
                if (pcm.Length == 0) return SdkResult.Ok;
                if (inputRate != sampleRate)
                {
                    inputRate = sampleRate;
                    hasLastSample = false;
                    resamplePosition = 0;
                }
                int frames = pcm.Length / channels;
                if (sampleRate == SampleRate)
                {
                    for (int frame = 0; frame < frames; frame++) Enqueue(Mono(pcm, channels, frame));
                    return SdkResult.Ok;
                }
                Resample(pcm, sampleRate, channels, frames);
                return SdkResult.Ok;
            }
        }

        private void Resample(float[] pcm, int sampleRate, int channels, int frames)
        {
            int virtualFrames = frames + (hasLastSample ? 1 : 0);
            double step = (double)sampleRate / SampleRate;
            // 保留最后一个源样本作为下一批的左端点，避免每批重新起相位造成断续。
            while (resamplePosition < virtualFrames - 1)
            {
                int left = (int)resamplePosition;
                double fraction = resamplePosition - left;
                float first = VirtualMono(pcm, channels, left);
                float second = VirtualMono(pcm, channels, left + 1);
                Enqueue((float)(first * (1 - fraction) + second * fraction));
                resamplePosition += step;
            }
            resamplePosition -= virtualFrames - 1;
            lastSample = Mono(pcm, channels, frames - 1);
            hasLastSample = true;
        }

        private float VirtualMono(float[] pcm, int channels, int frame)
            => hasLastSample ? (frame == 0 ? lastSample : Mono(pcm, channels, frame - 1)) : Mono(pcm, channels, frame);

        private static float Mono(float[] pcm, int channels, int frame)
            => channels == 1 ? pcm[frame] : pcm[frame * 2] * 0.5f + pcm[frame * 2 + 1] * 0.5f;

        private void Enqueue(float sample)
        {
            if (count == samples.Length)
            {
                readIndex = (readIndex + 1) % samples.Length;
                count--;
            }
            samples[writeIndex] = sample;
            writeIndex = (writeIndex + 1) % samples.Length;
            count++;
        }

        internal void Read(float[] destination, int frames)
        {
            if (destination == null || frames < 0 || destination.Length < frames) throw new ArgumentOutOfRangeException(nameof(frames));
            lock (gate)
            {
                int available = Math.Min(count, frames);
                for (int frame = 0; frame < available; frame++)
                {
                    float sample = samples[readIndex];
                    destination[frame] = float.IsNaN(sample) ? 0 : Math.Max(-1f, Math.Min(1f, sample));
                    readIndex = (readIndex + 1) % samples.Length;
                }
                count -= available;
                Array.Clear(destination, available, frames - available);
            }
        }

        internal void Clear()
        {
            lock (gate)
            {
                readIndex = writeIndex = count = inputRate = 0;
                resamplePosition = 0;
                hasLastSample = false;
                lastSample = 0;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                Clear();
            }
        }
    }
}
