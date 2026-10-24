using System;

namespace MyVerseXRSDK
{
    /// <summary>单次Rig绑定的所有权；旧句柄释放不会影响后续替换的新绑定。</summary>
    public sealed class SpatialBindingHandle : IDisposable
    {
        private SpatialService owner;
        private readonly long revision;

        internal SpatialBindingHandle(SpatialService owner, long revision)
        {
            this.owner = owner;
            this.revision = revision;
        }

        public void Dispose()
        {
            if (owner == null) return;
            owner.ReleaseBinding(revision);
            owner = null;
        }
    }

    public readonly struct SpatialBindingResult
    {
        public SdkResult Result { get; }
        public bool Success => Result.Success;
        public SpatialBindingHandle Handle { get; }
        internal SpatialBindingResult(SdkResult result, SpatialBindingHandle handle = null)
        {
            Result = result;
            Handle = handle;
        }
    }
}
