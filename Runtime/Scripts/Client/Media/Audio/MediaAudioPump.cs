using System;
using System.Threading;

namespace MyVerseXRSDK
{
    /// <summary>音频线程的有界供给入口；异常交回主线程，关闭只封住入口而不假定在途调用已经返回。</summary>
    internal sealed class MediaAudioPump
    {
        private readonly AudioUseBarrier uses = new AudioUseBarrier();
        private readonly GameAudioBuffer input;
        private readonly Action<float[], int, int> send;
        private float[] mono;
        private Exception error;
        internal bool IsDrained => uses.IsDrained;

        internal MediaAudioPump(GameAudioBuffer input, Action<float[], int, int> send, int initialFrames)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.send = send ?? throw new ArgumentNullException(nameof(send));
            mono = new float[Math.Max(1, initialFrames)];
        }

        internal void Process(float[] output, int channels)
        {
            if (output == null) return;
            if (!uses.TryEnter()) { Array.Clear(output, 0, output.Length); return; }
            try
            {
                if (channels <= 0 || output.Length % channels != 0)
                    throw new ArgumentException("音频回调必须提供完整采样帧");
                int frames = output.Length / channels;
                if (mono.Length < frames) mono = new float[frames];
                input.Read(mono, frames);
                send(mono, frames, input.SampleRate);
            }
            catch (Exception failure)
            {
                // 音频线程不操作Unity日志；native所有者在主线程取出并报告，不吞释放竞态。
                Interlocked.CompareExchange(ref error, failure, null);
                uses.Close();
            }
            finally
            {
                Array.Clear(output, 0, output.Length);
                uses.Exit();
            }
        }

        internal Exception TakeError() => Interlocked.Exchange(ref error, null);
        internal void Close() => uses.Close();
    }
}
