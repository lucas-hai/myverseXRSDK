using System;

namespace MyVerseXRSDK
{
    /// <summary>实例网络资源保护上限，不是设备性能容量保证；发送和接收分别计量排队字节。</summary>
    public sealed class NetworkLimits
    {
        public int MaximumMessageBytes { get; set; } = 4 * 1024 * 1024;
        public int MaximumQueuedMessages { get; set; } = 256;
        public int MaximumQueuedBytes { get; set; } = 8 * 1024 * 1024;
        public int MessagesPerFrame { get; set; } = 128;
        public int MaximumPendingRequests { get; set; } = 256;

        internal NetworkLimits Capture()
        {
            if (MaximumMessageBytes <= 0 || MaximumQueuedMessages <= 0 || MaximumQueuedBytes <= 0 ||
                MessagesPerFrame <= 0 || MaximumPendingRequests <= 0 || MaximumQueuedBytes < MaximumMessageBytes)
                throw new ArgumentException("网络容量须为正数，排队字节上限不能小于单消息上限");
            return (NetworkLimits)MemberwiseClone();
        }
    }
}
