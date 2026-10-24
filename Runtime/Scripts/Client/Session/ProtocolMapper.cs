using System;
using System.Collections.Generic;
using Google.Protobuf;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal static class ProtocolMapper
    {
        internal static bool TryParse<T>(MessageParser<T> parser, byte[] bytes, out T message, SdkLogger log = null) where T : IMessage<T>
        {
            message = default;
            if (bytes == null) return false;
            try { message = parser.ParseFrom(bytes); return true; }
            catch (InvalidProtocolBufferException error)
            {
                (log ?? new SdkLogger(0)).Write(SdkLogLevel.Warning, "Session", "协议数据解析失败：" + error.Message);
                return false;
            }
        }

        internal static bool TryRegion(RegionInfoPush push, out RegionUpdate result)
        {
            result = default;
            if (push?.RegionInfo == null) return false;
            var region = push.RegionInfo;
            var game = push.GameInfo;
            var center = region.Center == null ? Vector3.zero : new Vector3(region.Center.X, region.Center.Y, region.Center.Z);
            var rotation = region.Rotation == null ? Vector3.zero : new Vector3(region.Rotation.X, region.Rotation.Y, region.Rotation.Z);
            var offset = game?.GameOffset == null ? Vector3.zero : new Vector3(game.GameOffset.X, game.GameOffset.Y, game.GameOffset.Z);
            var offsetRotation = game?.GameOffsetRotation == null ? Vector3.zero : new Vector3(game.GameOffsetRotation.X, game.GameOffsetRotation.Y, game.GameOffsetRotation.Z);
            if (!SpatialValueValidation.Finite(rotation) || !SpatialValueValidation.Finite(offsetRotation)) return false;
            result = new RegionUpdate(region.Len, region.Width, center, Quaternion.Euler(rotation), offset,
                Quaternion.Euler(offsetRotation), game != null && game.ShowFloor);
            return SpatialValueValidation.Region(result);
        }

        internal static bool TryDevice(SynPosition.Types.Push push, out DevicePose result)
        {
            result = default;
            if (push?.Position == null) return false;
            var position = push.Position;
            var rotation = push.Rotation == null ? Vector3.zero : new Vector3(push.Rotation.X, push.Rotation.Y, push.Rotation.Z);
            if (!SpatialValueValidation.Finite(rotation)) return false;
            result = new DevicePose(position.DeviceId, push.RoomId, position.RoleModeId,
                new Vector3(position.X, position.Y, position.Z), Quaternion.Euler(rotation));
            return SpatialValueValidation.Device(result);
        }

        internal static bool TryObstacles(GameScenePush push, out IReadOnlyList<ObstacleUpdate> result)
        {
            result = Array.Empty<ObstacleUpdate>();
            if (push == null) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var values = new ObstacleUpdate[push.SceneData.Count];
            for (int index = 0; index < values.Length; index++)
            {
                var data = push.SceneData[index];
                if (data.ModuleId != 1 && data.ModuleId != 2) return false;
                var position = data.Position == null ? Vector3.zero : new Vector3(data.Position.X, data.Position.Y, data.Position.Z);
                var obstacle = new ObstacleUpdate(data.Id, data.ModuleId == 1 ? ObstacleShape.Box : ObstacleShape.Ellipse,
                    position, data.Len, data.Width, data.Radius);
                if (!SpatialValueValidation.Obstacle(obstacle) || !ids.Add(obstacle.Id)) return false;
                values[index] = obstacle;
            }
            result = values;
            return true;
        }

        internal static byte[] EncodeDevice(DevicePose pose)
        {
            return new SynPosition.Types.Request
            {
                RoomId = pose.RoomId ?? string.Empty,
                Position = new SynPosition.Types.DevicePosition
                {
                    DeviceId = pose.DeviceId, RoleModeId = pose.ModelId ?? "10001", X = pose.Position.x, Y = 0, Z = pose.Position.z
                },
                Rotation = new SynPosition.Types.DeviceRotation { X = 0, Y = pose.Rotation.eulerAngles.y, Z = 0 }
            }.ToByteArray();
        }
    }
}
