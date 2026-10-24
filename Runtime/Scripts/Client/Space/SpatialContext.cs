using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class SpatialContext : IDisposable
    {
        private SpatialBindingLifetime lifetime;
        private Pose expected = Pose.identity;
        internal SpatialRigBinding Binding { get; private set; }
        internal Transform ProxyRoot { get; private set; }
        internal bool IsBound => Binding != null && Binding.CalibrationRoot != null;
        internal bool OwnsPose => IsBound && PoseMatches(Binding.CalibrationRoot, expected);

        internal static SdkResult Validate(SpatialRigBinding binding, bool requireIdentity)
        {
            if (binding == null || binding.MotionRoot == null || binding.CalibrationRoot == null ||
                binding.TrackingRoot == null || binding.Head == null)
                return Invalid("Rig节点缺失或已销毁");
            if (binding.CalibrationRoot.parent != binding.MotionRoot || binding.TrackingRoot == binding.CalibrationRoot ||
                !binding.TrackingRoot.IsChildOf(binding.CalibrationRoot) || !binding.Head.IsChildOf(binding.TrackingRoot))
                return Invalid("Rig层级必须为业务根→专用校准层→追踪层→头部");
            if (!binding.CalibrationRoot.gameObject.activeInHierarchy || !binding.Head.gameObject.activeInHierarchy)
                return Invalid("绑定的校准层与头部必须处于启用状态");
            if (!UnitScaleChain(binding.Head)) return Invalid("Rig及其祖先不能包含非单位缩放");
            for (int index = 0; index < binding.TrackedNodes.Count; index++)
            {
                var tracked = binding.TrackedNodes[index];
                if (tracked == null || !tracked.IsChildOf(binding.TrackingRoot) || !UnitScaleChain(tracked))
                    return Invalid("追踪节点必须经过同一校准层且保持单位缩放");
            }
            if (requireIdentity && !PoseMatches(binding.CalibrationRoot, Pose.identity))
                return Invalid("首次接管校准层要求单位局部位姿");
            return SdkResult.Ok;
        }

        internal void Bind(SpatialRigBinding binding, Action released)
        {
            Binding = binding;
            expected = Pose.identity;
            var root = new GameObject("MVXRSDK.PhysicalProxies") { hideFlags = HideFlags.DontSave };
            root.SetActive(false);
            root.transform.SetParent(binding.CalibrationRoot, false);
            ProxyRoot = root.transform;
            lifetime = binding.CalibrationRoot.gameObject.AddComponent<SpatialBindingLifetime>();
            lifetime.hideFlags = HideFlags.DontSave;
            lifetime.Bind(released);
        }

        internal SdkResult ValidateLive()
        {
            var result = Validate(Binding, false);
            if (!result.Success) return result;
            return OwnsPose ? SdkResult.Ok : SdkResult.Fail(SdkErrorCode.ExternalCalibrationWrite, "校准节点被外部改写，SDK停止争写");
        }

        internal void Apply(Pose roomToGame)
        {
            Binding.CalibrationRoot.SetLocalPositionAndRotation(roomToGame.position, roomToGame.rotation);
            expected = roomToGame;
        }

        internal void ResetPose()
        {
            if (OwnsPose) Apply(Pose.identity);
        }

        internal void SetVisible(bool visible)
        {
            if (ProxyRoot != null && ProxyRoot.gameObject.activeSelf != visible) ProxyRoot.gameObject.SetActive(visible);
        }

        internal Pose ReadRoomPose()
        {
            var root = Binding.CalibrationRoot;
            return new Pose(root.InverseTransformPoint(Binding.Head.position),
                Quaternion.Inverse(root.rotation) * Binding.Head.rotation);
        }

        public void Dispose()
        {
            if (lifetime != null)
            {
                lifetime.Detach();
                Destroy(lifetime);
                lifetime = null;
            }
            SetVisible(false);
            if (ProxyRoot != null) Destroy(ProxyRoot.gameObject);
            ProxyRoot = null;
            ResetPose();
            Binding = null;
        }

        internal static void Destroy(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        private static bool UnitScaleChain(Transform node)
        {
            while (node != null)
            {
                if (!SpatialValueValidation.Finite(node.localScale) || (node.localScale - Vector3.one).sqrMagnitude > 0.00000001f)
                    return false;
                node = node.parent;
            }
            return true;
        }

        private static bool PoseMatches(Transform node, Pose pose) =>
            (node.localPosition - pose.position).sqrMagnitude < 0.00000001f && Quaternion.Angle(node.localRotation, pose.rotation) < 0.01f;

        private static SdkResult Invalid(string message) => SdkResult.Fail(SdkErrorCode.InvalidBinding, message);
    }
}
