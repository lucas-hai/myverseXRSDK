namespace MyVerseXRSDK
{
    internal class MessageType
    {
        //请求登录
        public static string CS_LOGIN = "logic.Binding";
        //心跳
        public static string CS_HEAD_L = "login.L";

        ///更新不同用户的真实位置
        /// </summary>
        public static string CS_UPDATEDE_USER_POSITION = "logic.SynPosition";


        ///更新不同用户的真实位置
        /// </summary>
        public static string CS_UPDATEDE_USER_PROCESSOR = "logic.UpdateDeviceNode";

        /// <summary>
        /// 服务器主动推送 其他房间的位置信息
        /// </summary>
        public static string SC_ROOM_POSITIONPUSH = "SynPositionPush";
 

        /// <summary>
        /// 服务器主动推送 房间属性信息
        /// </summary>
        public static string SC_ROOM_ATTRIBUTEPUSH = "RoomAttributePush";

        /// <summary>
        /// 服务器主动推送 游戏场景信息
        /// </summary>
        public static string SC_GAME_SCENE_PUSH = "GameScenePush";

        /// <summary>
        /// 客户端请求 游戏场景信息
        /// </summary>
        public static string CS_QUERY_GAME_SCENE_INFO = "logic.QueryGameSceneInfo";

        /// <summary>
        /// 播控通知推流（SC 推送）：NotifyLivePush { StreamServerIp, Start, DeviceId }
        /// </summary>
        public static string SC_NOTIFY_LIVE = "NotifyLivePush";

        /// <summary>
        /// SDK 请求开始录屏（CS 请求-应答）：logic.StartRecord
        /// </summary>
        public static string CS_START_RECORD = "logic.StartRecord";

        /// <summary>
        /// 客户端请求切换推流镜头（CS 请求-应答）：logic.DirectorInsert
        /// payload: DirectorInsert.Types.Request { Lenses, DurationSec }
        /// </summary>
        public static string CS_DIRECTOR_INSERT = "logic.DirectorInsert";

        /// <summary>
        /// 中控选中某客户端的推送（SC 推送）：DirectorSelectedPush
        /// payload: DirectorSelected { DeviceId, IsPrimary, Slot, DurationSec }
        /// </summary>
        public static string SC_DIRECTOR_SELECTED = "DirectorSelectedPush";

        /// <summary>
        /// 服务器通知某设备开始游戏（SC 推送）：DeviceGameStartPush
        /// payload: DeviceGameStartPush { DeviceId, TeamTag }
        /// </summary>
        public static string SC_DEVICE_GAME_START = "DeviceGameStartPush";

        /// <summary>
        /// 上报设备在线状态（CS 请求-应答）：logic.UpdateDeviceOnlineStatus
        /// payload: UpdateDeviceOnlineStatus.Types.Request { DeviceId, Online }
        /// 登录成功上报 online=true；退出房间（UnInit / 房间解散 / 应用退出）上报 online=false
        /// </summary>
        public static string CS_UPDATE_DEVICE_ONLINE_STATUS = "logic.UpdateDeviceOnlineStatus";

    }
}