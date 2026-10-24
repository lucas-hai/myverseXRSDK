using System;

namespace MyVerseXRSDK
{
    /// <summary>创建时捕获的日志策略；只影响日志输出，不影响错误结果、事件或状态。</summary>
    public sealed class SdkLogOptions
    {
        public SdkLogLevel MinimumLevel { get; set; } = SdkLogLevel.Info;
        public bool WriteToUnityConsole { get; set; } = true;
        /// <summary>借用回调，主线程延后派发；关闭时排空已有条目，不由SDK释放。回调须快速返回。</summary>
        public Action<SdkLogEntry> Sink { get; set; }
        public int MaximumQueuedEntries { get; set; } = 256;
        public int EntriesPerFrame { get; set; } = 64;

        internal SdkLogOptions Capture()
        {
            if (!Enum.IsDefined(typeof(SdkLogLevel), MinimumLevel) || MaximumQueuedEntries <= 0 || EntriesPerFrame <= 0)
                throw new ArgumentException("日志等级无效或日志容量/帧预算不是正数");
            return (SdkLogOptions)MemberwiseClone();
        }
    }
}
