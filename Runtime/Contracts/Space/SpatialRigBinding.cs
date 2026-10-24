using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>绑定业务Rig的明确角色；仅CalibrationRoot由SDK写入，其他Transform均为借用。</summary>
    public sealed class SpatialRigBinding
    {
        public Transform MotionRoot { get; }
        public Transform CalibrationRoot { get; }
        public Transform TrackingRoot { get; }
        public Transform Head { get; }
        public IReadOnlyList<Transform> TrackedNodes { get; }

        internal bool HasSameNodes(SpatialRigBinding other)
        {
            if (other == null || !ReferenceEquals(MotionRoot, other.MotionRoot) ||
                !ReferenceEquals(CalibrationRoot, other.CalibrationRoot) || !ReferenceEquals(TrackingRoot, other.TrackingRoot) ||
                !ReferenceEquals(Head, other.Head) || TrackedNodes.Count != other.TrackedNodes.Count) return false;
            for (int index = 0; index < TrackedNodes.Count; index++)
                if (!ReferenceEquals(TrackedNodes[index], other.TrackedNodes[index])) return false;
            return true;
        }

        public SpatialRigBinding(Transform motionRoot, Transform calibrationRoot, Transform trackingRoot,
            Transform head, params Transform[] trackedNodes)
        {
            MotionRoot = motionRoot;
            CalibrationRoot = calibrationRoot;
            TrackingRoot = trackingRoot;
            Head = head;
            TrackedNodes = Array.AsReadOnly(trackedNodes == null ? Array.Empty<Transform>() : (Transform[])trackedNodes.Clone());
        }
    }
}
