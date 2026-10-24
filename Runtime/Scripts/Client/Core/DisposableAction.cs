using System;
using System.Threading;

namespace MyVerseXRSDK
{
    internal sealed class DisposableAction : IDisposable
    {
        private Action action;
        internal DisposableAction(Action action) => this.action = action;
        public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke();
    }
}
