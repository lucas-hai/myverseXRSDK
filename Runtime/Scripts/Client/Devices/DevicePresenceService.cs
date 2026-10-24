using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>设备房间位姿与SDK虚影表现；不复制业务网络角色，不向业务相机安装上报组件。</summary>
    public sealed class DevicePresenceService : IDisposable
    {
        private readonly SdkRuntimeContext runtime;
        private readonly SpatialService space;
        private readonly SessionService session;
        private readonly PrefabPool pool;
        private readonly DeviceOptions options;
        private readonly LocalPoseReporter reporter;
        private readonly IReadOnlyDictionary<string, DevicePose> devices;
        private readonly Dictionary<string, NetworkTransform> views = new Dictionary<string, NetworkTransform>(StringComparer.Ordinal);
        private readonly List<string> scanIds = new List<string>();
        private GameObject prefab;
        private float nextVisibilityScan;
        private bool sameRoom;
        private float sameRoomDistance;
        private bool disposed;
        public IReadOnlyDictionary<string, DevicePose> Current { get; }
        /// <summary>本机房间位姿采样，不代表网络已送达；External接入可用它转交自有传输。</summary>
        public event Action<DevicePose> LocalPoseSampled;

        internal DevicePresenceService(SdkRuntimeContext runtime, SessionService session, SpatialService space, PrefabPool pool)
        {
            this.runtime = runtime;
            this.space = space;
            this.session = session;
            this.pool = pool;
            options = runtime.Options.Devices;
            sameRoom = options.SameRoomAvatars;
            sameRoomDistance = options.SameRoomDistance;
            devices = session.DeviceStore.Current;
            Current = devices;
            reporter = new LocalPoseReporter(space, session, runtime.Options.DeviceId, options);
            reporter.Sampled += OnSampled;
            session.DeviceReceived += OnDevice;
            session.DeviceRemoved += OnRemoved;
            session.SessionChanged += OnSession;
            space.BindingChanging += OnBindingChanging;
            space.PresentationChanged += OnSpaceChanged;
        }

        public SdkResult SetSameRoomVisibility(bool enabled, float distance)
        {
            runtime.EnsureAlive();
            if (!SpatialOptions.ValidNonnegative(distance)) return SdkResult.Fail(SdkErrorCode.InvalidArgument, "虚影距离必须为有限非负数");
            sameRoom = enabled;
            sameRoomDistance = distance;
            RefreshVisibility();
            return SdkResult.Ok;
        }

        internal void Tick(float now, float deltaTime)
        {
            if (disposed) return;
            reporter.Tick(now);
            if (now >= nextVisibilityScan)
            {
                nextVisibilityScan = now + options.SamplingIntervalSeconds;
                RefreshVisibility();
            }
            foreach (var view in views.Values) if (view != null) view.Tick(deltaTime);
        }

        private void OnSampled(DevicePose pose)
        {
            var callbacks = LocalPoseSampled;
            if (callbacks == null) return;
            foreach (Action<DevicePose> callback in callbacks.GetInvocationList())
            {
                try { callback(pose); }
                catch (Exception error) { runtime.Log.Exception("Devices", error); }
            }
        }

        private void OnDevice(long epoch, DevicePose device)
        {
            if (epoch != session.Current.Epoch || device.DeviceId == runtime.Options.DeviceId) return;
            RefreshVisibility();
        }

        private void OnRemoved(long epoch, string id)
        {
            if (epoch != session.Current.Epoch) return;
            ReleaseView(id);
        }

        private void OnSession(SessionSnapshot snapshot)
        {
            reporter.Invalidate();
            if (snapshot.State == SessionState.Reconnecting) { ClearViews(); return; }
            ClearViews();
        }

        private void OnBindingChanging() { reporter.Invalidate(); ClearViews(); }
        private void OnSpaceChanged() { reporter.Invalidate(); RefreshVisibility(); }

        private void RefreshVisibility()
        {
            if (disposed || !options.AvatarsEnabled || !space.TryGetRoomPose(out var self) || space.ProxyRoot == null)
            {
                ClearViews();
                return;
            }
            // 实例化自定义Prefab可能触发业务回调；复用键列表避免枚举器在重入更新时失效。
            scanIds.Clear();
            foreach (var id in devices.Keys) scanIds.Add(id);
            foreach (var id in scanIds)
            {
                if (!devices.TryGetValue(id, out var device)) continue;
                bool belongsHere = device.RoomId == session.Current.RoomId;
                float distance = belongsHere ? sameRoomDistance : options.OtherRoomDistance;
                var delta = device.Position - self.position;
                bool visible = (!belongsHere || sameRoom) && delta.x * delta.x + delta.z * delta.z <= distance * distance;
                if (!visible) { ReleaseView(id); continue; }
                var target = new Pose(device.Position, device.Rotation);
                if (views.TryGetValue(id, out var view) && view != null) { view.SetTarget(target); continue; }
                views.Remove(id);
                if (prefab == null) prefab = options.AvatarPrefab != null ? options.AvatarPrefab : Resources.Load<GameObject>("Characters/Prefabs/Role");
                var instance = pool.Rent(prefab, space.ProxyRoot);
                instance.name = "Avatar." + id;
                view = instance.GetComponent<NetworkTransform>() ?? instance.AddComponent<NetworkTransform>();
                views[id] = view;
                view.InitializePose(target);
            }
        }

        private void ReleaseView(string id)
        {
            if (!views.TryGetValue(id, out var view)) return;
            views.Remove(id);
            pool.Return(prefab, view != null ? view.Object : null);
        }

        private void ClearViews()
        {
            foreach (var view in views.Values) pool.Return(prefab, view != null ? view.Object : null);
            views.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.DeviceReceived -= OnDevice;
            session.DeviceRemoved -= OnRemoved;
            session.SessionChanged -= OnSession;
            space.BindingChanging -= OnBindingChanging;
            space.PresentationChanged -= OnSpaceChanged;
            reporter.Sampled -= OnSampled;
            ClearViews();
            LocalPoseSampled = null;
        }
    }
}
