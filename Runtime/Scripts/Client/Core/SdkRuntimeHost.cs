using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class SdkRuntimeHost : MonoBehaviour
    {
        private readonly Queue<Action> pending = new Queue<Action>();
        private Action tick;
        private Action destroyed;
        private bool closing;
        internal SdkLogger Log { get; private set; }
        internal Action LateTick { private get; set; }

        private void LateUpdate() { if (!closing) LateTick?.Invoke(); }

        internal void Initialize(Action onTick, Action onDestroyed, SdkLogger log = null)
        {
            Log = log ?? new SdkLogger(0);
            tick = onTick;
            destroyed = onDestroyed;
        }

        internal void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (closing) throw new ObjectDisposedException(nameof(SdkRuntimeHost));
            pending.Enqueue(action);
        }

        internal Coroutine Run(IEnumerator routine) => StartCoroutine(routine);
        internal void Cancel(Coroutine routine) { if (routine != null) StopCoroutine(routine); }

        private void Update()
        {
            DrainCallbacks();
            if (!closing) tick?.Invoke();
        }

        internal void DrainCallbacks()
        {
            // 回调再次排队留到下一调度点，避免业务重入使本帧无限增长。
            int count = pending.Count;
            for (int index = 0; index < count && pending.Count > 0; index++)
            {
                try { pending.Dequeue().Invoke(); }
                catch (Exception error) { Log.Exception("Runtime", error); }
            }
            if (!closing) Log.Drain();
        }

        internal void Close()
        {
            if (closing) return;
            closing = true;
            enabled = false;
            tick = null;
            LateTick = null;
            destroyed = null;
            StopAllCoroutines();
            DrainCallbacks();
            pending.Clear();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        private void OnDestroy()
        {
            if (closing) return;
            var callback = destroyed;
            destroyed = null;
            callback?.Invoke();
        }
    }
}
