using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal class MVXRSDKEventType
    {

        /// <summary>
        /// 
        /// </summary>
        public const string ROOM_ALLOCATE_SUCCESS = "RoomAllocateSuccess";
        /// <summary>
        /// 房间解散成功
        /// </summary>
        public const string ROOM_DISBAND = "RoomDisband";

        /// <summary>
        /// 登录成功
        /// </summary>
        public const string LOGIN_SUCCESS = "LoginSuccess";

        /// <summary>
        /// 断线重连次数耗尽
        /// </summary>
        public const string SOCKET_RECONNECT_FAILED = "SocketReconnectFailed";

        /// <summary>
        /// 推流已开始
        /// </summary>
        public const string PUSH_STREAM_STARTED = "PushStreamStarted";

        /// <summary>
        /// 推流已停止
        /// </summary>
        public const string PUSH_STREAM_STOPPED = "PushStreamStopped";

        /// <summary>
        /// 推流失败
        /// </summary>
        public const string PUSH_STREAM_FAILED = "PushStreamFailed";

        /// <summary>
        /// 录屏请求结果
        /// </summary>
        public const string RECORD_RESULT = "RecordResult";

    }
}
