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

        /// <summary>
        /// 本次登录会话是否已上报 online=true。仅用于控制 offline 上报：
        /// 未登录不发、一次会话只发一次。online 上报不查此标志——
        /// 断线重连后服务端可能已标记离线，重登录必须重新上报。
        /// </summary>
        private static bool s_OnlineReported;

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
            OnDisbandRoom();   // 内部调 SocketSystem.Clear → 关闭 WS 连接（关闭前已上报 offline）
            RoomId = null;
            s_OnlineReported = false;   // 静态残留防护：二次 Init 前回到初始态
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
            ReportDeviceOffline();   // 必须在 Clear 关闭 WS 之前，socket 断后无法补发
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
            ReportDeviceOnline();
        }

        // ============================== 设备在线状态上报 ==============================

        /// <summary>
        /// 登录成功后上报 online=true。每次登录（含重连后重登录）都发送，不做去重。
        /// </summary>
        private static void ReportDeviceOnline()
        {
            SendOnlineStatus(true, (errorCode, buffer) =>
            {
                if (errorCode != 0)
                {
                    MVXRSDKLog.Warning($"设备在线状态上报失败 online=true errorCode={errorCode}");
                    return;
                }
                if (!SocketSystem.TryParse<UpdateDeviceOnlineStatus.Types.Response>(buffer, out var rsp, "Room.UpdateDeviceOnlineStatus") || !rsp.Success)
                {
                    MVXRSDKLog.Warning("设备在线状态上报未被服务端确认 online=true");
                }
            });
        }

        /// <summary>
        /// 退出房间前上报 offline。SDK 无独立退房 API，退出房间即三条路径：
        /// UnInit、服务端解散房间（均经 OnDisbandRoom）、应用直接退出（OnApplicationQuit 兜底）。
        /// 仅本会话上报过 online 才发送，且只发一次。供 MVXRSDKManager.OnApplicationQuit 调用。
        /// </summary>
        public static void ReportDeviceOffline()
        {
            if (!s_OnlineReported) return;
            // 不带回调：紧随的 DisConnect 会以 -1 回调所有 pending 请求，
            // 而消息本身可能已发出，避免制造误导性的失败日志
            SendOnlineStatus(false, null);
        }

        private static void SendOnlineStatus(bool online, MessageCallBack callBack)
        {
            if (!SocketSystem.IsConnect) return;
            var req = new UpdateDeviceOnlineStatus.Types.Request
            {
                DeviceId = MVXRSDK.DeviceId ?? "",
                Online = online,
            };
            SocketSystem.SendMessage(MessageType.CS_UPDATE_DEVICE_ONLINE_STATUS, req.ToByteString(), callBack);
            s_OnlineReported = online;
            MVXRSDKLog.Info($"已上报设备在线状态 online={online}");
        }

    }
}