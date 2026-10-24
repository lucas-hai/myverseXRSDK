using System;
using System.Threading;

namespace MyVerseXRSDK
{
    public static partial class MVXRSDK
    {
        private static readonly object clientGate = new object();
        private static SdkClient activeClient;
        private static long nextRuntimeId;

        /// <summary>在 Unity 主线程创建但不启动客户端；同一进程只允许一个未释放的实例。</summary>
        public static SdkClient Create(SdkOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var captured = options.Capture();
            lock (clientGate)
            {
                if (activeClient != null)
                    throw new InvalidOperationException("已有活动 SDK 客户端，请等待其完全释放后再创建");
                var client = new SdkClient(Interlocked.Increment(ref nextRuntimeId), captured, ReleaseClient);
                activeClient = client;
                return client;
            }
        }

        private static void ReleaseClient(SdkClient owner)
        {
            lock (clientGate)
            {
                if (ReferenceEquals(activeClient, owner)) activeClient = null;
            }
        }
    }
}
