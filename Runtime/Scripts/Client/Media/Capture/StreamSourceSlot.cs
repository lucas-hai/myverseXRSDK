using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class StreamSourceSlot : IDisposable
    {
        private readonly Func<RenderTexture> getOutput;
        private readonly Action clearOutput;
        private readonly Action<SdkResult> report;
        private readonly SdkLogger log;
        private bool sessionActive;
        private bool automatic;
        private bool mutating;
        private bool disposed;
        private bool endRequested;
        internal IStreamSource Current { get; private set; }
        internal bool IsReleased => disposed && !mutating && Current == null;

        internal StreamSourceSlot(Func<RenderTexture> getOutput, Action clearOutput, Action<SdkResult> report)
            : this(getOutput, clearOutput, report, null) { }

        internal StreamSourceSlot(Func<RenderTexture> getOutput, Action clearOutput, Action<SdkResult> report, SdkLogger log)
        {
            this.getOutput = getOutput;
            this.clearOutput = clearOutput;
            this.report = report;
            this.log = log;
        }

        internal SourceSetResult TrySet(IStreamSource source, bool isAutomatic)
        {
            if (disposed || source == null) return SourceSetResult.Invalid;
            if (mutating) return SourceSetResult.Busy;
            mutating = true;
            try
            {
                if (!source.IsAvailable || disposed) return SourceSetResult.Invalid;
                if (ReferenceEquals(Current, source)) return SourceSetResult.Accepted;
                if (sessionActive && Current != null) return SourceSetResult.Busy;
                var output = getOutput();
                if (output == null) return SourceSetResult.Invalid;
                ReleaseCurrent();
                if (disposed) return SourceSetResult.Invalid;
                Current = source;
                automatic = isAutomatic;
                BindLog(source, log);
                try { source.Attach(output); }
                catch (Exception error)
                {
                    Current = null;
                    automatic = false;
                    // Attach可能只完成了一部分；撤销临时写入，但未接受的对象仍归调用方。
                    Safely(source.Detach);
                    BindLog(source, null);
                    clearOutput();
                    Report(error);
                    return SourceSetResult.Invalid;
                }
                return SourceSetResult.Accepted;
            }
            catch (Exception error)
            {
                Report(error);
                return SourceSetResult.Invalid;
            }
            finally { FinishMutation(); }
        }

        internal void SetSessionActive(bool active) => sessionActive = active;

        internal void EndSession()
        {
            sessionActive = false;
            if (mutating) { endRequested = true; return; }
            if (automatic) Clear();
        }

        internal SdkResult Clear()
        {
            if (mutating) return SdkResult.Fail(SdkErrorCode.Busy, "画面源正在切换");
            mutating = true;
            try { ReleaseCurrent(); }
            finally { FinishMutation(); }
            return SdkResult.Ok;
        }

        internal void Tick(double now, int framesPerSecond)
        {
            if (disposed || Current == null || mutating) return;
            mutating = true;
            try
            {
                if (Current.IsAvailable) { Current.Tick(now, framesPerSecond); return; }
                report(SdkResult.Fail(SdkErrorCode.Unavailable, "画面源已失效，清源并输出黑帧"));
                ReleaseCurrent();
            }
            catch (Exception error) { Report(error); ReleaseCurrent(); }
            finally { FinishMutation(); }
        }

        private void FinishMutation()
        {
            // 连延后的Detach/Dispose也属于在途使用，不能提前向输出所有者报告已收敛。
            try { if (Current != null && (disposed || (endRequested && automatic))) ReleaseCurrent(); }
            finally { endRequested = false; mutating = false; }
        }

        private void ReleaseCurrent()
        {
            var owned = Current;
            Current = null;
            automatic = false;
            if (owned != null)
            {
                Safely(owned.Detach);
                Safely(owned.Dispose);
                BindLog(owned, null);
            }
            clearOutput();
        }

        private static void BindLog(IStreamSource source, SdkLogger log)
        {
            if (source is CameraStreamSource camera) camera.Log = log;
            else if (source is RenderTextureStreamSource texture) texture.Log = log;
        }

        private void Safely(Action action)
        {
            try { action(); }
            catch (Exception error) { Report(error); }
        }

        private void Report(Exception error) => report(SdkResult.Fail(SdkErrorCode.MediaFailed, "画面源操作失败：" + error.Message));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (!mutating) Clear();
        }
    }
}
