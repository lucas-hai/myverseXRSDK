using System;
using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>显式编辑器装配工具；操作可撤销，不保存场景，不猜测业务节点角色。</summary>
    public static class SpatialRigSetup
    {
        /// <summary>在明确的MotionRoot与其直接TrackingRoot间插入单位校准层，保留追踪子树世界位姿；已有合法层则复用。</summary>
        public static Transform EnsureCalibrationRoot(Transform motionRoot, Transform trackingRoot)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("校准层只能在编辑模式装配");
            if (motionRoot == null || trackingRoot == null || motionRoot == trackingRoot)
                throw new ArgumentException("需要不同且有效的MotionRoot与TrackingRoot");
            ValidateScale(trackingRoot);
            ValidateScale(motionRoot);
            var parent = trackingRoot.parent;
            if (parent != motionRoot)
            {
                if (parent != null && parent.parent == motionRoot && parent.name == "CalibrationRoot" &&
                    parent.localPosition.sqrMagnitude < 0.00000001f && Quaternion.Angle(parent.localRotation, Quaternion.identity) < 0.001f)
                    return parent;
                throw new ArgumentException("TrackingRoot必须直接属于MotionRoot，或已处于合法CalibrationRoot下");
            }
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("插入SDK校准层");
            var calibration = new GameObject("CalibrationRoot").transform;
            calibration.SetParent(motionRoot, false);
            Undo.RegisterCreatedObjectUndo(calibration.gameObject, "创建校准层");
            Undo.SetTransformParent(trackingRoot, calibration, "迁入校准层");
            Undo.CollapseUndoOperations(group);
            return calibration;
        }

        private static void ValidateScale(Transform node)
        {
            for (var ancestor = node; ancestor != null; ancestor = ancestor.parent)
                if (!(Vector3.Distance(ancestor.localScale, Vector3.one) < 0.0001f))
                    throw new ArgumentException("XR绑定节点及祖先必须为单位缩放");
        }
    }
}
