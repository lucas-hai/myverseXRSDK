using UnityEngine;

namespace MyVerseXRSDK
{
    public enum RecordingTargetKind { Device, Camera }

    /// <summary>远端限时录制请求；SDK不持有录制任务或文件。</summary>
    public sealed class RecordingRequest
    {
        public RecordingTargetKind TargetKind { get; set; }
        public string TargetId { get; set; }
        public int DurationSeconds { get; set; } = 60;
        public string FileName { get; set; }
    }

    /// <summary>导演仲裁请求；AutoCamera是借用的本机候选相机，不随请求发送到服务端。</summary>
    public sealed class DirectorRequest
    {
        public string Source { get; set; }
        public int Lenses { get; set; } = 1;
        public int DurationSeconds { get; set; } = 10;
        public bool Record { get; set; }
        /// <summary>Record为true时使用的录制名称；协议约定不含扩展名，SDK原样透传而不增删后缀。</summary>
        public string FileName { get; set; }
        public Camera AutoCamera { get; set; }
    }

}
