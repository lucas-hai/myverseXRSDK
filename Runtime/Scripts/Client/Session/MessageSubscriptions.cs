using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class MessageSubscriptions : IDisposable
    {
        private sealed class Entry
        {
            internal Action<int, byte[]> Callback;
            internal bool Active = true;
        }
        private readonly Dictionary<string, List<Entry>> messages = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        private readonly SdkLogger log;
        internal MessageSubscriptions(SdkLogger log = null) => this.log = log ?? new SdkLogger(0);
        private int dispatchDepth;
        private bool disposed;

        internal IDisposable Subscribe(string type, Action<int, byte[]> callback)
        {
            if (disposed) throw new ObjectDisposedException(nameof(MessageSubscriptions));
            if (string.IsNullOrEmpty(type) || callback == null) throw new ArgumentException("消息类型与回调不能为空");
            if (!messages.TryGetValue(type, out var entries)) messages.Add(type, entries = new List<Entry>());
            var entry = new Entry { Callback = callback };
            entries.Add(entry);
            return new DisposableAction(() =>
            {
                entry.Active = false;
                entry.Callback = null;
                if (dispatchDepth == 0) entries.Remove(entry);
            });
        }

        internal void Publish(string type, int code, byte[] data)
        {
            if (disposed || !messages.TryGetValue(type, out var entries)) return;
            dispatchDepth++;
            int count = entries.Count;
            try
            {
                for (int index = 0; index < count && !disposed; index++)
                {
                    var entry = entries[index];
                    if (!entry.Active) continue;
                    try { entry.Callback(code, data); }
                    catch (Exception error) { log.Exception("Session", error); }
                }
            }
            finally
            {
                dispatchDepth--;
                if (dispatchDepth == 0)
                    for (int index = entries.Count - 1; index >= 0; index--)
                        if (!entries[index].Active) entries.RemoveAt(index);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entries in messages.Values)
                foreach (var entry in entries) { entry.Active = false; entry.Callback = null; }
            messages.Clear();
        }
    }
}
