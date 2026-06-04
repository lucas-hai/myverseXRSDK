using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>接线层：装配 Store + 表现层 Module；监听 WS 把数据喂给 Store。</summary>
    internal class NetworkTransformManager
    {
        private static NetworkTransformStateStore s_Store;
        private static NetworldTransformModule    s_Module;
        private static bool                       s_Initialized;

        public static void Start()
        {
            s_Store  = new NetworkTransformStateStore();
            s_Module = new NetworldTransformModule(s_Store);
        }

        public static void InitSDK()
        {
            if (s_Initialized) return;
            AddEvent();
            s_Module.InitSDK();
            s_Initialized = true;
        }

        public static void UnInitSDK()
        {
            if (!s_Initialized) return;
            RemoveEvent();
            s_Module.UnInitSDK();
            s_Store.Dispose();
            s_Initialized = false;
        }

        private static void AddEvent()
        {
            SocketSystem.RegisterMessage(MessageType.SC_ROOM_POSITIONPUSH,  OnRoomPositionPush);
            SocketSystem.RegisterMessage(MessageType.SC_ROOM_ATTRIBUTEPUSH, OnRoomAttributePush);
            EventSystem.AddEventListener(MVXRSDKEventType.ROOM_DISBAND, OnDisbandRoom);
        }

        private static void RemoveEvent()
        {
            SocketSystem.CancelMessage(MessageType.SC_ROOM_POSITIONPUSH);
            SocketSystem.CancelMessage(MessageType.SC_ROOM_ATTRIBUTEPUSH);
            EventSystem.RemoveEventListener(MVXRSDKEventType.ROOM_DISBAND, OnDisbandRoom);
        }

        /// <summary>
        /// 服务器主动推送 房间属性信息
        /// </summary>
        private static void OnRoomAttributePush(int errorCode, byte[] buffer)
        {
            if (!s_Initialized) return;
            if (errorCode != 0)
            {
                MVXRSDKLog.Error($"NetworkTransformManager OnRoomAttributePush errorCode:{errorCode}");
                return;
            }
            if (!SocketSystem.TryParse<RoomAttributePush>(buffer, out var room, "NetTransform.RoomAttributePush")) return;

            if (room.Key == RoomAttributePush.Types.AttributeType.Quit)
            {
                // 协议语义已确认：服务端会把多房间的退出事件都推过来，本地仅处理非本房间（其它房间成员退出 → 移除其本地代理）。
                // 本房间内的 Quit 由其它链路（房间解散/本地状态机）处理，这里直接跳过。
                if (room.Value2 == RoomManager.RoomId) return;
                s_Store.RemoveRole(room.Value);
            }
        }

        private static void OnRoomPositionPush(int errorCode, byte[] buffer)
        {
            if (!s_Initialized) return;
            if (errorCode != 0)
            {
                MVXRSDKLog.Error($"NetworkTransformManager OnRoomPositionPush errorCode:{errorCode}");
                return;
            }
            if (!SocketSystem.TryParse<SynPosition.Types.Push>(buffer, out var push, "NetTransform.SynPosition")) return;

            // 协议语义已确认：仅处理非本房间的位置推送（本房间成员的位置由其它链路同步）。
            if (push.RoomId == RoomManager.RoomId) return;

            SynPosition.Types.DevicePosition devP = push.Position;
            SynPosition.Types.DeviceRotation devR = push.Rotation;
            Vector3 pos = new Vector3(devP.X, devP.Y, devP.Z);
            Vector3 rot = new Vector3(devR.X, devR.Y, devR.Z);

            s_Store.ApplyRole(devP.DeviceId, devP.RoleModeId, pos, rot);
        }

        private static void OnDisbandRoom()
        {
            // 房间解散：清空全部快照并广播移除，让表现层同步释放所有 GO。
            // 不 Dispose（订阅者保留），后续仍能继续接收位置推送。
            s_Store.ClearAndBroadcast();
        }
    }
}
