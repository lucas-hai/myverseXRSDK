using System;
using System.Collections.Generic;

namespace MyVerseXRSDK
{
    /// <summary>外部数据源的会话租约。输入限主线程，各数据域revision必须递增；释放不影响替换它的新会话。</summary>
    public sealed class ExternalSessionWriter : IDisposable
    {
        private readonly SessionService owner;
        private readonly long epoch;
        private long regionRevision;
        private long obstacleRevision;
        private long deviceRevision;
        private long liveRevision;
        private bool disposed;

        internal ExternalSessionWriter(SessionService owner, long epoch)
        {
            this.owner = owner;
            this.epoch = epoch;
        }

        /// <summary>提交结构有效的区域值；接受输入不等于地块匹配或校准应用成功，请观察Space状态。</summary>
        public SdkResult ApplyRegion(long revision, RegionUpdate region)
        {
            var result = Validate(revision, regionRevision);
            if (!result.Success) return result;
            if (!SpatialValueValidation.Region(region)) return Invalid("区域尺寸、位置或旋转无效");
            regionRevision = revision;
            owner.ReceiveRegion(epoch, revision, region);
            return SdkResult.Ok;
        }

        /// <summary>提交障碍物全量快照；整帧验证后才替换，调用方的集合不会被长期借用。</summary>
        public SdkResult ApplyObstacles(long revision, IReadOnlyList<ObstacleUpdate> obstacles)
        {
            var result = Validate(revision, obstacleRevision);
            if (!result.Success) return result;
            if (obstacles == null) return Invalid("障碍物快照不能为空");
            result = owner.CheckObstacleCapacity(obstacles.Count);
            if (!result.Success) return result;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < obstacles.Count; index++)
                if (!SpatialValueValidation.Obstacle(obstacles[index]) || !ids.Add(obstacles[index].Id))
                    return Invalid("障碍物几何无效或Id重复");
            var snapshot = new ObstacleUpdate[obstacles.Count];
            for (int index = 0; index < snapshot.Length; index++) snapshot[index] = obstacles[index];
            obstacleRevision = revision;
            owner.ReceiveObstacles(epoch, snapshot);
            return SdkResult.Ok;
        }

        /// <summary>更新单台设备；revision属于整个设备输入域，而非每个设备独立计数。</summary>
        public SdkResult UpsertDevice(long revision, DevicePose device)
        {
            var result = Validate(revision, deviceRevision);
            if (!result.Success) return result;
            if (!SpatialValueValidation.Device(device)) return Invalid("设备标识或房间位姿无效");
            result = owner.DeviceStore.CheckCapacity(device);
            if (!result.Success) return result;
            deviceRevision = revision;
            owner.ReceiveDevice(epoch, device);
            return SdkResult.Ok;
        }

        public SdkResult RemoveDevice(long revision, string deviceId)
        {
            var result = Validate(revision, deviceRevision);
            if (!result.Success) return result;
            if (string.IsNullOrWhiteSpace(deviceId)) return Invalid("设备标识不能为空");
            deviceRevision = revision;
            owner.RemoveDevice(epoch, deviceId);
            return SdkResult.Ok;
        }

        /// <summary>注入本会话的推流控制；start的地址必须是完整WHIP HTTP/HTTPS地址。</summary>
        public SdkResult NotifyLive(long revision, string url, bool start)
        {
            var result = Validate(revision, liveRevision);
            if (!result.Success) return result;
            if (start && (!Uri.TryCreate(url, UriKind.Absolute, out var address) ||
                          (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)))
                return Invalid("推流地址必须为HTTP或HTTPS");
            liveRevision = revision;
            owner.ReceiveLive(epoch, url, start);
            return SdkResult.Ok;
        }

        private SdkResult Validate(long revision, long previous)
        {
            var result = owner.ValidateWriter(epoch);
            if (!result.Success) return result;
            if (disposed || revision <= previous)
                return SdkResult.Fail(SdkErrorCode.StaleGeneration, "写入已失效或revision未递增");
            return SdkResult.Ok;
        }

        private static SdkResult Invalid(string message) => SdkResult.Fail(SdkErrorCode.InvalidArgument, message);

        public void Dispose()
        {
            if (disposed) return;
            owner.CloseExternal(epoch);
            disposed = true;
        }
    }
}
