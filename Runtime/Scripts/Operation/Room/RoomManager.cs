using System;
using System.Collections;
using System.Collections.Generic;
using Google.Protobuf;
using UnityEngine;
using static Login.Types;

namespace MyVerseXRSDK
{
    internal static class RoomManager
    {

        private static RoomModule loginModule;
        private static bool s_Initialized;

        public static string RoomId { get; private set; }


        public static void Start()
        {
            loginModule = new RoomModule();
        }
        private static void AddEvent()
        {
            EventSystem.AddEventListener<string>(MVXRSDKEventType.ROOM_ALLOCATE_SUCCESS, OnAllocateRooms);
            EventSystem.AddEventListener(MVXRSDKEventType.ROOM_DISBAND, OnDisbandRoom);
        }
        private static void RemoveEvent()
        {
            EventSystem.RemoveEventListener<string>(MVXRSDKEventType.ROOM_ALLOCATE_SUCCESS, OnAllocateRooms);
            EventSystem.RemoveEventListener(MVXRSDKEventType.ROOM_DISBAND, OnDisbandRoom);
        }


        /// <summary>
        /// 正式启动入口：通过本地中控 HTTP(localhost:8868) 拉 WS 地址 → 连 WS。
        /// 回调参数：(是否成功, baseUrl)。
        /// </summary>
        public static void StartByHttpDirectory(Action<bool, string> cb)
        {
            if (s_Initialized)
            {
                MVXRSDKLog.Warning("RoomManager 已启动，忽略重复 StartByHttpDirectory");
                return;
            }
            AddEvent();
            MonoSystem.AddUpdateListener(Update);
            loginModule.SetDeviceId(MVXRSDK.DeviceId);
            loginModule.RequestIpAddress(MVXRSDKConfig.HTTP_IP, MVXRSDKConfig.API_GET_CONFIG, cb);
            s_Initialized = true;
        }
        /// <summary>
        /// WsDirect 启动入口：跳过 localhost:8868 HTTP 拉中控地址那一步，外部直接传入中控服地址。
        /// 仍走与 Production 相同的房间分配轮询链路：
        /// RoomModule.Update 每秒轮询中控 → 中控分配房间后返回房间 WS IP →
        /// ROOM_ALLOCATE_SUCCESS 事件 → OnAllocateRooms → 连房间 WS → 登录。
        /// </summary>
        /// <param name="controlServerAddress">中控服地址（如 "http://192.168.1.50:7015"），不是房间服 WS 地址</param>
        public static void StartByWsAddress(string controlServerAddress)
        {
            if (s_Initialized)
            {
                MVXRSDKLog.Warning("RoomManager 已启动，忽略重复 StartByWsAddress");
                return;
            }
            AddEvent();
            MonoSystem.AddUpdateListener(Update);
            loginModule.SetDeviceId(MVXRSDK.DeviceId);
            loginModule.SetControlServerDirect(controlServerAddress);
            s_Initialized = true;
        }

        public static void UnInitSDK()
        {
            if (!s_Initialized) return;
            // 四件套清理顺序：先 cancel 订阅（防止飞行回调拿到 null），再清缓存
            RemoveEvent();
            MonoSystem.RemoveUpdateListener(Update);
            OnDisbandRoom();   // 内部调 SocketSystem.Clear → 关闭 WS 连接
            RoomId = null;
            s_Initialized = false;
        }


        public static void Update()
        {
            loginModule.Update();
        }


        private static void OnAllocateRooms(string ip)
        {
            MVXRSDKLog.Debug($"请求加入房间");
            SocketSystem.ConnectServer(ip, (bool isResult) =>
                               {
                                   if (isResult)
                                   {
                                       RequestLogin();
                                   }
                                   else
                                   {
                                       MVXRSDKLog.Error($"网络错误，加入房间失败");
                                   }
                               });

        }
        public static void OnDisbandRoom()
        {
            SocketSystem.Clear();
            MVXRSDK.SetRoomAllocationStatus(RoomAllocationStatus.Undistributed);

            // 状态机：曾经 Connected → 掉线进入 Disconnected；尚未进入 Connecting 的不动
            if (MVXRSDK.State == MVXRSDKState.Connected || MVXRSDK.State == MVXRSDKState.Connecting)
            {
                MVXRSDK.SetState(MVXRSDKState.Disconnected);
            }
        }
        private static void RequestLogin()
        {
            Request loginReq = new Request();
            loginReq.Token = MVXRSDK.DeviceId;
            SocketSystem.SendMessage(MessageType.CS_LOGIN, loginReq.ToByteString(), onLoginResponse);

        }


        private static void onLoginResponse(int errorCode, byte[] buffer)
        {
            if (!s_Initialized) return;
            if (errorCode != 0)
            {
                MVXRSDKLog.Error(string.Format("加入房间错误,错误码：{0}", errorCode));
                return;
            }
            if (!SocketSystem.TryParse<Response>(buffer, out var loginRsp, "Room.LoginResp"))
            {
                MVXRSDKLog.Error("加入房间登录响应解析失败");
                return;
            }
            RoomId = loginRsp.RoomAddress.RoomId;
            MVXRSDK.SetRoomAllocationStatus(RoomAllocationStatus.Allocated);
            MVXRSDK.SetState(MVXRSDKState.Connected);
            EventSystem.EventTrigger(MVXRSDKEventType.LOGIN_SUCCESS);
            MVXRSDKLog.Info(string.Format("加入房间成功，所在房间人数{0}", loginRsp.Devices.Count));
        }

    }
}