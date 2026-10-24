using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class ObstaclePresenter : IDisposable
    {
        private sealed class View
        {
            internal ObstacleUpdate Data;
            internal GameObject Prefab;
            internal GameObject Object;
            internal SpaceObstacles Geometry;
        }

        private readonly SpatialService space;
        private readonly SpatialOptions options;
        private readonly PrefabPool pool;
        private readonly Dictionary<string, View> views = new Dictionary<string, View>(StringComparer.Ordinal);
        private readonly HashSet<string> incomingIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> removedIds = new List<string>();
        private IReadOnlyList<ObstacleUpdate> snapshot = Array.Empty<ObstacleUpdate>();
        private GameObject boxPrefab;
        private GameObject ellipsePrefab;
        private float nextDistanceCheck;

        internal ObstaclePresenter(SpatialService space, SpatialOptions options, PrefabPool pool)
        {
            this.space = space;
            this.options = options;
            this.pool = pool;
            space.BindingChanging += ClearViews;
            space.PresentationChanged += Refresh;
        }

        internal void Apply(IReadOnlyList<ObstacleUpdate> next)
        {
            snapshot = next;
            Refresh();
        }

        internal void Reset()
        {
            snapshot = Array.Empty<ObstacleUpdate>();
            ClearViews();
        }

        private void Refresh()
        {
            if (!options.ShowObstacles || !space.PresentationReady || space.ProxyRoot == null) return;
            incomingIds.Clear();
            for (int index = 0; index < snapshot.Count; index++) incomingIds.Add(snapshot[index].Id);
            removedIds.Clear();
            foreach (var pair in views) if (!incomingIds.Contains(pair.Key)) removedIds.Add(pair.Key);
            foreach (var id in removedIds) Release(id);
            for (int index = 0; index < snapshot.Count; index++) Upsert(snapshot[index]);
            RefreshDistances();
        }

        private void Upsert(ObstacleUpdate data)
        {
            if (views.TryGetValue(data.Id, out var view) && (view.Object == null || view.Data.Shape != data.Shape))
            {
                Release(data.Id);
                view = null;
            }
            if (view == null)
            {
                var prefab = ResolvePrefab(data.Shape);
                view = new View { Prefab = prefab, Object = pool.Rent(prefab, space.ProxyRoot) };
                view.Object.name = "Obstacle." + data.Id;
                view.Geometry = view.Object.GetComponent<SpaceObstacles>() ?? view.Object.AddComponent<SpaceObstacles>();
                views.Add(data.Id, view);
            }
            view.Data = data;
            view.Geometry.Apply(data);
        }

        internal void Tick(float now)
        {
            if (now < nextDistanceCheck || !space.PresentationReady) return;
            nextDistanceCheck = now + 0.2f;
            RefreshDistances();
        }

        private void RefreshDistances()
        {
            if (!space.TryGetRoomPose(out var self)) return;
            foreach (var view in views.Values)
            {
                if (view.Object == null) continue;
                var delta = self.position - view.Data.Position;
                float extent = view.Data.Shape == ObstacleShape.Ellipse ? view.Data.Radius : Mathf.Max(view.Data.Length, view.Data.Width);
                float threshold = options.ObstacleDistance + (view.Object.activeSelf ? options.VisibilityHysteresis : 0f);
                bool visible = delta.x * delta.x + delta.z * delta.z <= threshold * threshold + extent * extent;
                if (view.Object.activeSelf != visible) view.Object.SetActive(visible);
            }
        }

        private GameObject ResolvePrefab(ObstacleShape shape)
        {
            if (shape == ObstacleShape.Box)
                return boxPrefab != null ? boxPrefab : boxPrefab = options.BoxPrefab != null ? options.BoxPrefab : Resources.Load<GameObject>("Obstacle/Prefabs/RectObstacle");
            return ellipsePrefab != null ? ellipsePrefab : ellipsePrefab = options.EllipsePrefab != null ? options.EllipsePrefab : Resources.Load<GameObject>("Obstacle/Prefabs/OvalObstacle");
        }

        private void Release(string id)
        {
            if (!views.TryGetValue(id, out var view)) return;
            views.Remove(id);
            pool.Return(view.Prefab, view.Object);
        }

        private void ClearViews()
        {
            foreach (var view in views.Values) pool.Return(view.Prefab, view.Object);
            views.Clear();
        }

        public void Dispose()
        {
            space.BindingChanging -= ClearViews;
            space.PresentationChanged -= Refresh;
            Reset();
        }
    }
}
