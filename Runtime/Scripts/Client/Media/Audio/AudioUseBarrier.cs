using System;

namespace MyVerseXRSDK
{
    /// <summary>关闭入口和确认在途使用归零共用一把锁；调用方只能在IsDrained后释放native资源。</summary>
    internal sealed class AudioUseBarrier
    {
        private readonly object gate = new object();
        private int users;
        private bool closed;
        internal bool IsDrained { get { lock (gate) return closed && users == 0; } }

        internal bool TryEnter()
        {
            lock (gate)
            {
                if (closed) return false;
                users++;
                return true;
            }
        }

        internal void Exit()
        {
            lock (gate)
            {
                if (users == 0) throw new InvalidOperationException("音频使用计数没有对应的进入操作");
                users--;
            }
        }

        internal void Close()
        {
            lock (gate) closed = true;
        }
    }
}
