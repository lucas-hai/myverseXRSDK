using System;
using System.Collections.Generic;

namespace MyVerseXRSDK
{
    internal sealed class NativeMessageReceiver : IDisposable
    {
        private readonly SessionService session;
        private readonly string deviceId;
        private readonly Func<bool> active;
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private long regionRevision;

        internal NativeMessageReceiver(SessionService session, string deviceId, ICommandTransport transport, Func<bool> active)
        {
            this.session = session;
            this.deviceId = deviceId;
            this.active = active;
            subscriptions.Add(transport.Subscribe("RegionInfoPush", OnRegion));
            subscriptions.Add(transport.Subscribe("GameScenePush", OnObstacles));
            subscriptions.Add(transport.Subscribe("SynPositionPush", OnDevice));
            subscriptions.Add(transport.Subscribe("RoomAttributePush", OnRoomAttribute));
            subscriptions.Add(transport.Subscribe("DeviceGameStartPush", OnDeviceStart));
            subscriptions.Add(transport.Subscribe("NotifyLivePush", OnLive));
            subscriptions.Add(transport.Subscribe("DirectorSelectedPush", OnSelected));
        }

        private bool CanAccept(int code) => active() && session.Current.IsJoined && code == 0;

        internal void ApplyRegion(RegionInfoPush push)
        {
            if (!active() || !session.Current.IsJoined) return;
            if (!ProtocolMapper.TryRegion(push, out var region))
            {
                session.ReportError(SdkResult.Fail(SdkErrorCode.InvalidResponse, "区域消息字段无效"));
                return;
            }
            session.ReceiveRegion(session.Current.Epoch, ++regionRevision, region);
        }

        internal void ApplyObstacles(GameScenePush push)
        {
            if (!active() || !session.Current.IsJoined) return;
            if (push != null)
            {
                var capacity = session.CheckObstacleCapacity(push.SceneData.Count);
                if (!capacity.Success) { session.ReportError(capacity); return; }
            }
            if (!ProtocolMapper.TryObstacles(push, out var obstacles))
            {
                session.ReportError(SdkResult.Fail(SdkErrorCode.InvalidResponse, "障碍物快照无效，整帧拒绝"));
                return;
            }
            session.ReceiveObstacles(session.Current.Epoch, obstacles);
        }

        private void OnRegion(int code, byte[] data)
        {
            if (CanAccept(code) && ProtocolMapper.TryParse(RegionInfoPush.Parser, data, out var push, session.Log)) ApplyRegion(push);
        }
        private void OnObstacles(int code, byte[] data)
        {
            if (CanAccept(code) && ProtocolMapper.TryParse(GameScenePush.Parser, data, out var push, session.Log)) ApplyObstacles(push);
        }
        private void OnDevice(int code, byte[] data)
        {
            if (CanAccept(code) && ProtocolMapper.TryParse(SynPosition.Types.Push.Parser, data, out var push, session.Log) &&
                ProtocolMapper.TryDevice(push, out var device)) session.ReceiveDevice(session.Current.Epoch, device);
        }
        private void OnRoomAttribute(int code, byte[] data)
        {
            if (!CanAccept(code) || !ProtocolMapper.TryParse(RoomAttributePush.Parser, data, out var push, session.Log)) return;
            if (push.Key == RoomAttributePush.Types.AttributeType.Quit)
                session.RemoveDevice(session.Current.Epoch, push.Value);
            else if (push.Key == RoomAttributePush.Types.AttributeType.Status && push.Value == "2")
                session.ReceiveAdmission(new GameStartRequest(deviceId, 0, AdmissionSource.RoomState));
        }
        private void OnDeviceStart(int code, byte[] data)
        {
            if (CanAccept(code) && ProtocolMapper.TryParse(DeviceGameStartPush.Parser, data, out var push, session.Log) && push.DeviceId == deviceId)
                session.ReceiveAdmission(new GameStartRequest(push.DeviceId, push.TeamTag, AdmissionSource.DeviceCommand));
        }
        private void OnLive(int code, byte[] data)
        {
            if (CanAccept(code) && ProtocolMapper.TryParse(global::NotifyLive.Parser, data, out var push, session.Log))
                session.ReceiveLive(session.Current.Epoch, push.StreamServerIp, push.Start);
        }
        private void OnSelected(int code, byte[] data)
        {
            if (CanAccept(code) && ProtocolMapper.TryParse(global::DirectorSelected.Parser, data, out var push, session.Log))
                session.ReceiveSelection(new DirectorSelection(push.DeviceId, push.IsPrimary, push.Slot, push.DurationSec));
        }

        public void Dispose()
        {
            foreach (var subscription in subscriptions) subscription.Dispose();
            subscriptions.Clear();
        }
    }
}
