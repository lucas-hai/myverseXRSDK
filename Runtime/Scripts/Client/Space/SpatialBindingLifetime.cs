using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class SpatialBindingLifetime : MonoBehaviour
    {
        private Action released;
        internal void Bind(Action callback) => released = callback;
        internal void Detach() => released = null;
        private void OnDisable() => Release();
        private void OnDestroy() => Release();
        private void Release()
        {
            var callback = released;
            released = null;
            callback?.Invoke();
        }
    }
}
