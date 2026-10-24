namespace MyVerseXRSDK
{
    /// <summary>只有Accepted转移源所有权；Busy/Invalid不会Dispose调用方的源。</summary>
    public enum SourceSetResult { Accepted, Busy, Invalid }
}
