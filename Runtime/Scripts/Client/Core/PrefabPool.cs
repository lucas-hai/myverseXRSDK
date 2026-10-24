using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class PrefabPool : IDisposable
    {
        private readonly Transform root;
        private readonly Dictionary<GameObject, Stack<GameObject>> cached = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly HashSet<GameObject> allocated = new HashSet<GameObject>();
        private readonly HashSet<GameObject> pooled = new HashSet<GameObject>();
        private const int CapacityPerPrefab = 32;

        internal PrefabPool(Transform owner)
        {
            var container = new GameObject("MVXRSDK.ProxyPool") { hideFlags = HideFlags.DontSave };
            container.SetActive(false);
            root = container.transform;
            root.SetParent(owner, false);
        }

        internal GameObject Rent(GameObject prefab, Transform parent)
        {
            if (prefab == null) throw new InvalidOperationException("SDK表现资源不存在或已被外部销毁");
            GameObject instance = null;
            if (cached.TryGetValue(prefab, out var items))
                while (items.Count > 0 && instance == null)
                {
                    var candidate = items.Pop();
                    pooled.Remove(candidate);
                    if (candidate != null) instance = candidate;
                    else allocated.Remove(candidate);
                }
            if (instance == null)
            {
                instance = UnityEngine.Object.Instantiate(prefab, root, false);
                allocated.Add(instance);
            }
            instance.SetActive(false);
            instance.transform.SetParent(parent, false);
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = prefab.transform.localScale;
            return instance;
        }

        internal void Return(GameObject prefab, GameObject instance)
        {
            if (instance == null || pooled.Contains(instance)) return;
            instance.SetActive(false);
            if (prefab == null || root == null)
            {
                allocated.Remove(instance);
                SpatialContext.Destroy(instance);
                return;
            }
            if (!cached.TryGetValue(prefab, out var items)) cached.Add(prefab, items = new Stack<GameObject>());
            if (items.Count >= CapacityPerPrefab)
            {
                allocated.Remove(instance);
                SpatialContext.Destroy(instance);
                return;
            }
            instance.transform.SetParent(root, false);
            if (instance.transform.parent != root)
            {
                allocated.Remove(instance);
                SpatialContext.Destroy(instance);
                return;
            }
            pooled.Add(instance);
            items.Push(instance);
        }

        public void Dispose()
        {
            foreach (var instance in allocated) if (instance != null) SpatialContext.Destroy(instance);
            allocated.Clear();
            pooled.Clear();
            cached.Clear();
            if (root != null) SpatialContext.Destroy(root.gameObject);
        }
    }
}
