using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>接线层：装配 Store + 表现层 Module；监听 WS 把数据喂给 Store。</summary>
    internal class NetworkTransformManager
    {
        private static NetworkTransformStateStore s_Store;
        private static NetworldTransformModule    s_Module;
        private static bool                       s_Initialized;
        private static bool                       s_SyncSameRoomAvatar;  // 是否同步"本房间"其他玩家虚影；默认 false（不同步）

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
            s_SyncSameRoomAvatar = false;   // 复位到默认（不同步同房间虚影），保证二次 Init 行为可预期
            s_Initialized = false;
        }

        /// <summary>是否同步"同房间（本房间）其他玩家虚影"。默认 false。</summary>
        public static bool IsSyncSameRoomAvatar => s_SyncSameRoomAvatar;

        /// <summary>
        /// 设置是否同步"同房间（本房间）其他玩家虚影"。
        /// - 开启：后续收到本房间位置推送即创建虚影（成员静止不发推送时，等其移动后出现）。
        /// - 关闭：立即回收已创建的本房间虚影（不影响非本房间的虚影）。
        /// 任何时机皆可调用（Start 前调用也安全，仅置标志）。
        /// </summary>
        public static void SetSyncSameRoomAvatar(bool enable)
        {
            if (s_SyncSameRoomAvatar == enable) return;
            s_SyncSameRoomAvatar = enable;
            MVXRSDKLog.Info($"NetworkTransformManager: 同房间虚影同步 {(enable ? "开启" : "关闭")}");

            // 关闭时主动回收已创建的本房间虚影；开启时无需操作（等下次本房间位置推送自然创建）
            if (!enable && s_Initialized) s_Store.RemoveRolesByRoom(RoomManager.RoomId);
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
                // 非本房间成员退出：移除其本地虚影代理。
                // 本房间成员退出：默认不处理（本房间默认不建虚影）；仅当开启"同房间虚影同步"时才移除其虚影。
                if (room.Value2 == RoomManager.RoomId && !s_SyncSameRoomAvatar) return;
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

            // 本房间成员的位置推送默认跳过（不显示同房间虚影）；仅当业务开启"同房间虚影同步"时才落地。
            if (push.RoomId == RoomManager.RoomId && !s_SyncSameRoomAvatar) return;

            SynPosition.Types.DevicePosition devP = push.Position;
            SynPosition.Types.DeviceRotation devR = push.Rotation;
            Vector3 pos = new Vector3(devP.X, devP.Y, devP.Z);
            Vector3 rot = new Vector3(devR.X, devR.Y, devR.Z);

            s_Store.ApplyRole(devP.DeviceId, devP.RoleModeId, push.RoomId, pos, rot);
        }

        private static void OnDisbandRoom()
        {
            // 房间解散：清空全部快照并广播移除，让表现层同步释放所有 GO。
            // 不 Dispose（订阅者保留），后续仍能继续接收位置推送。
            s_Store.ClearAndBroadcast();
        }
    }
}
