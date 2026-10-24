using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MyVerseXRSDK
{
    /// <summary>会话拥有的有界设备值存储，原生与External共用；表现层不维护第二份设备缓存。</summary>
    internal sealed class RemoteDeviceStore
    {
        private readonly string selfId;
        private readonly int maximumDevices;
        private readonly Dictionary<string, DevicePose> values = new Dictionary<string, DevicePose>(StringComparer.Ordinal);
        internal IReadOnlyDictionary<string, DevicePose> Current { get; }

        internal RemoteDeviceStore(string selfId, int maximumDevices)
        {
            this.selfId = selfId;
            this.maximumDevices = maximumDevices;
            Current = new ReadOnlyDictionary<string, DevicePose>(values);
        }

        internal SdkResult CheckCapacity(DevicePose value)
        {
            if (!SpatialValueValidation.Device(value)) return SdkResult.Fail(SdkErrorCode.InvalidArgument, "设备标识或位姿无效");
            if (value.DeviceId == selfId || values.ContainsKey(value.DeviceId) || values.Count < maximumDevices) return SdkResult.Ok;
            return SdkResult.Fail(SdkErrorCode.CapacityExceeded, "设备快照数量超出实例容量");
        }

        internal SdkResult Upsert(DevicePose value)
        {
            var result = CheckCapacity(value);
            if (!result.Success || value.DeviceId == selfId) return result;
            values[value.DeviceId] = value;
            return SdkResult.Ok;
        }

        internal void Remove(string id) => values.Remove(id);
        internal void Clear() => values.Clear();
    }
}
