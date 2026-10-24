using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>SDK拥有的空间计算和校准应用。只写专用校准层，不移动地图或业务根。</summary>
    public sealed class SpatialService : IDisposable
    {
        private readonly SdkRuntimeContext runtime;
        private readonly SessionService session;
        private readonly SpatialOptions options;
        private readonly IRegionTileSource tiles;
        private readonly SpatialContext spatial = new SpatialContext();
        private readonly ObstaclePresenter obstacles;
        private readonly RegionFloorPresenter floor;
        private CalibrationResult? calibration;
        private SpatialBindingHandle handle;
        private long bindingRevision;
        private bool disposed;
        private bool originInvalidated;
        private bool holdPoseForRegion;

        public SpatialStatus Status { get; private set; }
        public SdkResult LastRejectedInput { get; private set; } = SdkResult.Ok;
        public event Action<SpatialStatus> Changed;
        public event Action<CalibrationResult> CalibrationChanged;
        internal event Action BindingChanging;
        internal event Action PresentationChanged;
        internal Transform ProxyRoot => spatial.ProxyRoot;
        internal Transform HudAnchor => spatial.Binding?.Head;
        internal RegionUpdate? CurrentRegion { get; private set; }
        internal bool PresentationReady => Status.State == SpatialState.Ready;

        internal SpatialService(SdkRuntimeContext runtime, SessionService session, PrefabPool pool)
        {
            this.runtime = runtime;
            this.session = session;
            options = runtime.Options.Spatial;
            tiles = options.TileSource ?? new RegionTileRepository();
            Status = new SpatialStatus(options.Mode == SpatialMode.Disabled ? SpatialState.Disabled : SpatialState.Unbound,
                false, false, false, SdkResult.Ok);
            if (options.Mode == SpatialMode.Disabled) return;
            session.SessionChanged += OnSessionChanged;
            session.RegionReceived += OnRegion;
            if (options.Mode == SpatialMode.XrCalibration)
            {
                obstacles = new ObstaclePresenter(this, options, pool);
                floor = new RegionFloorPresenter(this, options, runtime.Log);
                session.ObstaclesReceived += OnObstacles;
            }
        }

        private void OnObstacles(long epoch, System.Collections.Generic.IReadOnlyList<ObstacleUpdate> snapshot)
        {
            if (epoch == session.Current.Epoch) obstacles?.Apply(snapshot);
        }

        /// <summary>事务式绑定；失败保留原绑定，成功后自动补齐当前会话的有效校准。</summary>
        public SpatialBindingResult BindRig(SpatialRigBinding binding)
        {
            EnsureAlive();
            if (options.Mode != SpatialMode.XrCalibration)
                return new SpatialBindingResult(SdkResult.Fail(SdkErrorCode.InvalidState, "当前模式不应用XR节点"));
            if (binding != null && binding.HasSameNodes(spatial.Binding))
            {
                var live = spatial.ValidateLive();
                if (!live.Success)
                {
                    SetStatus(SpatialState.BindingInvalid, live, false);
                    return new SpatialBindingResult(live);
                }
                if (Status.State == SpatialState.BindingInvalid) ApplyCurrent();
                return new SpatialBindingResult(SdkResult.Ok, handle);
            }
            bool reusesCalibration = binding != null && spatial.Binding != null &&
                ReferenceEquals(binding.CalibrationRoot, spatial.Binding.CalibrationRoot);
            var validation = SpatialContext.Validate(binding, !reusesCalibration);
            if (!validation.Success) return new SpatialBindingResult(validation);
            if (reusesCalibration && !spatial.OwnsPose)
            {
                var conflict = SdkResult.Fail(SdkErrorCode.ExternalCalibrationWrite, "校准节点已被外部修改，不能通过换绑夺回写入权");
                SetStatus(SpatialState.BindingInvalid, conflict, false);
                return new SpatialBindingResult(conflict);
            }
            BindingChanging?.Invoke();
            spatial.Dispose();
            long revision = ++bindingRevision;
            spatial.Bind(binding, () => ReleaseBinding(revision));
            handle = new SpatialBindingHandle(this, revision);
            ApplyCurrent();
            return new SpatialBindingResult(SdkResult.Ok, handle);
        }

        internal void ReleaseBinding(long revision)
        {
            runtime.EnsureMainThread();
            if (disposed || revision != bindingRevision) return;
            ++bindingRevision;
            BindingChanging?.Invoke();
            spatial.Dispose();
            handle = null;
            SetStatus(SpatialState.Unbound, SdkResult.Ok, false);
        }

        public bool TryGetCalibration(out CalibrationResult result)
        {
            EnsureAlive();
            result = calibration.GetValueOrDefault();
            return IsCurrentResult;
        }

        /// <summary>取得协议房间帧中的本机位姿，未应用当前有效校准时返回false。</summary>
        public bool TryGetRoomPose(out Pose pose)
        {
            EnsureAlive();
            pose = default;
            if (!PresentationReady || !spatial.ValidateLive().Success) return false;
            pose = spatial.ReadRoomPose();
            return SpatialValueValidation.Finite(pose.position) && SpatialValueValidation.Rotation(pose.rotation);
        }

        /// <summary>用当前B×C将R帧位姿转为世界位姿；参考帧不可用或输入非法时返回false，输出为default。</summary>
        public bool TryRoomToWorld(Pose roomPose, out Pose worldPose)
        {
            EnsureAlive();
            worldPose = default;
            if (!CanConvertPose(roomPose)) return false;
            var root = spatial.Binding.CalibrationRoot;
            var converted = new Pose(root.TransformPoint(roomPose.position), root.rotation * roomPose.rotation);
            if (!ValidPose(converted)) return false;
            worldPose = converted;
            return true;
        }

        /// <summary>用当前(B×C)的逆将任意世界位姿转为R帧；参考帧不可用或输入非法时返回false，输出为default。限主线程，不修改节点。</summary>
        public bool TryWorldToRoom(Pose worldPose, out Pose roomPose)
        {
            EnsureAlive();
            roomPose = default;
            if (!CanConvertPose(worldPose)) return false;
            var root = spatial.Binding.CalibrationRoot;
            // CalibrationRoot世界变换已包含当前业务移动B，不能仅使用校准结果中的T。
            var converted = new Pose(root.InverseTransformPoint(worldPose.position), Quaternion.Inverse(root.rotation) * worldPose.rotation);
            if (!ValidPose(converted)) return false;
            roomPose = converted;
            return true;
        }

        private bool CanConvertPose(Pose pose) => PresentationReady && ValidPose(pose) && spatial.ValidateLive().Success;
        private static bool ValidPose(Pose pose) => SpatialValueValidation.Finite(pose.position) && SpatialValueValidation.Rotation(pose.rotation);

        /// <summary>追踪原点改变后撤销旧参考帧；外部源必须重新打开会话并提交新帧数据。</summary>
        public void InvalidateTrackingOrigin()
        {
            EnsureAlive();
            originInvalidated = true;
            calibration = null;
            CurrentRegion = null;
            session.InvalidateReferenceFrame();
            SetStatus(SpatialState.Invalidated, SdkResult.Fail(SdkErrorCode.InvalidCalibration, "追踪原点已改变，等待新参考帧"), false);
        }

        internal void Tick()
        {
            if (disposed || spatial.Binding == null || Status.State == SpatialState.BindingInvalid) return;
            var validation = spatial.ValidateLive();
            if (!validation.Success) SetStatus(SpatialState.BindingInvalid, validation, false);
            else obstacles?.Tick(Time.realtimeSinceStartup);
        }

        private bool IsCurrentResult => calibration.HasValue && session.Current.IsJoined &&
            calibration.Value.SessionEpoch == session.Current.Epoch;

        private void OnSessionChanged(SessionSnapshot snapshot)
        {
            if (snapshot.State == SessionState.Reconnecting)
            {
                holdPoseForRegion = true;
                SetStatus(SpatialState.StaleHold, SdkResult.Fail(SdkErrorCode.NotConnected, "会话重连中，校准暂不可用"), false);
                return;
            }
            if (snapshot.State == SessionState.Ended && !originInvalidated) holdPoseForRegion = false;
            bool keepPose = originInvalidated || holdPoseForRegion;
            obstacles?.Reset();
            calibration = null;
            CurrentRegion = null;
            if (!keepPose || snapshot.State == SessionState.Ended && !originInvalidated) spatial.ResetPose();
            var state = spatial.IsBound ? SpatialState.WaitingForRegion : SpatialState.Unbound;
            if (keepPose && snapshot.IsJoined) state = SpatialState.StaleHold;
            SetStatus(state, SdkResult.Ok, false);
        }

        private void OnRegion(long epoch, long revision, RegionUpdate region)
        {
            if (disposed || epoch != session.Current.Epoch || !session.Current.IsJoined) return;
            string id = RegionIdUtil.MakeRuntimeId(region.Length, region.Width);
            var found = tiles.Find(id, out var tile);
            if (!found.Success)
            {
                calibration = null;
                CurrentRegion = null;
                SetStatus(spatial.IsBound ? SpatialState.Unavailable : SpatialState.Unbound, found, false);
                return;
            }
            var result = CalibrationSolver.Solve(region, tile, options, out var forward, out var inverse);
            if (!result.Success)
            {
                LastRejectedInput = result;
                if (!IsCurrentResult) SetStatus(spatial.IsBound ? SpatialState.Unavailable : SpatialState.Unbound, result, false);
                return;
            }
            LastRejectedInput = SdkResult.Ok;
            originInvalidated = false;
            holdPoseForRegion = false;
            CurrentRegion = region;
            var computed = new CalibrationResult(runtime.RuntimeId, epoch, revision, tile.Id, tiles.Version,
                forward, inverse, options.Calibration);
            calibration = computed;
            ApplyCurrent();
            // Ready通知可能换房、关闭或提交更新revision；仅发布本次仍拥有的计算结果。
            if (disposed || runtime.IsClosing || !IsCurrentResult || calibration.Value.RegionRevision != revision ||
                calibration.Value.SessionEpoch != epoch) return;
            Publish(CalibrationChanged, computed);
        }

        private void ApplyCurrent()
        {
            if (options.Mode == SpatialMode.ComputeOnly)
            {
                SetStatus(IsCurrentResult ? SpatialState.Computed : SpatialState.WaitingForRegion, SdkResult.Ok, false);
                return;
            }
            if (!spatial.IsBound) { SetStatus(SpatialState.Unbound, SdkResult.Ok, false); return; }
            if (!IsCurrentResult) { SetStatus(SpatialState.WaitingForRegion, SdkResult.Ok, false); return; }
            var validation = spatial.ValidateLive();
            if (!validation.Success) { SetStatus(SpatialState.BindingInvalid, validation, false); return; }
            spatial.Apply(calibration.Value.RoomToGame);
            SetStatus(SpatialState.Ready, SdkResult.Ok, true);
        }

        private void SetStatus(SpatialState state, SdkResult reason, bool applied)
        {
            bool current = IsCurrentResult && state != SpatialState.StaleHold && state != SpatialState.Invalidated;
            bool changed = Status.State != state;
            Status = new SpatialStatus(state, IsCurrentResult, applied, current, reason);
            if (changed) runtime.Log.Write(SdkLogLevel.Info, "Space", "空间状态：" + state, sessionEpoch: session.Current.Epoch);
            spatial.SetVisible(state == SpatialState.Ready);
            PresentationChanged?.Invoke();
            Publish(Changed, Status);
        }

        private void Publish<T>(Action<T> callbacks, T value)
        {
            if (callbacks == null) return;
            foreach (Action<T> callback in callbacks.GetInvocationList())
            {
                try { callback(value); }
                catch (Exception error) { runtime.Log.Exception("Space", error); }
            }
        }

        private void EnsureAlive()
        {
            runtime.EnsureAlive();
            if (disposed) throw new ObjectDisposedException(nameof(SpatialService));
        }

        public void Dispose()
        {
            runtime.EnsureMainThread();
            if (disposed) return;
            disposed = true;
            session.SessionChanged -= OnSessionChanged;
            session.RegionReceived -= OnRegion;
            session.ObstaclesReceived -= OnObstacles;
            obstacles?.Dispose();
            floor?.Dispose();
            BindingChanging?.Invoke();
            spatial.Dispose();
            calibration = null;
            CurrentRegion = null;
            Changed = null;
            CalibrationChanged = null;
            BindingChanging = null;
            PresentationChanged = null;
        }
    }
}
