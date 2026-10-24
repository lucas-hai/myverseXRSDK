using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class LocalPoseReporter
    {
        private readonly SpatialService space;
        private readonly SessionService session;
        private readonly string deviceId;
        private readonly DeviceOptions options;
        private Pose last;
        private long lastEpoch;
        private bool force = true;
        private float nextSample;
        internal event Action<DevicePose> Sampled;

        internal LocalPoseReporter(SpatialService space, SessionService session, string deviceId, DeviceOptions options)
        {
            this.space = space;
            this.session = session;
            this.deviceId = deviceId;
            this.options = options;
        }

        internal void Invalidate() { force = true; nextSample = 0; }

        internal void Tick(float now)
        {
            if (!options.ReportingEnabled || !session.Current.IsJoined || !space.TryGetRoomPose(out var pose)) return;
            if (lastEpoch != session.Current.Epoch) { lastEpoch = session.Current.Epoch; force = true; }
            if (!force && now < nextSample) return;
            nextSample = now + options.SamplingIntervalSeconds;
            pose.position.y = 0;
            float yaw = pose.rotation.eulerAngles.y;
            pose.rotation = Quaternion.Euler(0, yaw, 0);
            if (!force && (pose.position - last.position).sqrMagnitude < options.PositionThreshold * options.PositionThreshold &&
                Mathf.Abs(Mathf.DeltaAngle(last.rotation.eulerAngles.y, yaw)) < options.YawThresholdDegrees) return;
            force = false;
            last = pose;
            Sampled?.Invoke(new DevicePose(deviceId, session.Current.RoomId, "10001", pose.position, pose.rotation));
        }
    }
}
