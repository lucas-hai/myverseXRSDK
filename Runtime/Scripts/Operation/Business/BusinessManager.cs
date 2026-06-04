using System;
using System.Collections;
using System.Collections.Generic;
using Google.Protobuf;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal static class BusinessManager
    {

        private static TransactionModule transactionModule;
        private static bool s_Initialized;

        internal static void Start()
        {
            transactionModule = new TransactionModule();
        }

        private static void AddEvent()
        {
            SocketSystem.RegisterMessage(MessageType.SC_ROOM_ATTRIBUTEPUSH, OnRoomAttributePush);
            SocketSystem.RegisterMessage(MessageType.SC_DEVICE_GAME_START, OnDeviceGameStartPush);
        }

        private static void RemoveEvent()
        {
            SocketSystem.CancelMessage(MessageType.SC_ROOM_ATTRIBUTEPUSH);
            SocketSystem.CancelMessage(MessageType.SC_DEVICE_GAME_START);
        }

        internal static void InitSDK()
        {
            if (s_Initialized) return;
            AddEvent();
            s_Initialized = true;
        }

        internal static void UnInitSDK()
        {
            if (!s_Initialized) return;
            // 四件套清理顺序：先 cancel 订阅（防止飞行回调拿到 null），再清缓存
            RemoveEvent();
            s_Initialized = false;
        }


        internal static void TransactionVerification(Action<bool> cb)
        {
            transactionModule.RequestIntegralUse(cb);
        }

        private static void OnRoomAttributePush(int errorCode, byte[] buffer)
        {
            // 防御性守卫：cancel 与正在分发的回调存在时间窗口，UnInit 后仍可能进入
            if (!s_Initialized) return;

            if (errorCode != 0)
            {
                MVXRSDKLog.Error($"BusinessManager OnRoomAttributePush errorCode: {errorCode}");
                MVXRSDK.RaiseTransactionVerification(false);
                return;
            }
            if (!SocketSystem.TryParse<RoomAttributePush>(buffer, out var room, "Business.RoomAttributePush")) return;
            if (room.Key == RoomAttributePush.Types.AttributeType.Status)
            {
                if (room.Value == RoomStatusType.Play)
                {
                    MVXRSDKLog.Info("OnRoomAttributePush:开始游戏");
                    MVXRSDK.RaiseTransactionVerification(true);

                }


            }

        }

        /// <summary>
        /// SC_DEVICE_GAME_START 路由：与 OnRoomAttributePush(Play) 复用同一条「开始游戏」语义，
        /// 命中本机 SN 后 raise <see cref="MVXRSDK.OnTransactionVerification"/>(true)，不新增对外接口。
        /// 协议携带目标设备 SN，仅当 deviceId==本机 SN 才触发本机开始游戏。
        /// </summary>
        private static void OnDeviceGameStartPush(int errorCode, byte[] buffer)
        {
            // 防御性守卫：cancel 与正在分发的回调存在时间窗口，UnInit 后仍可能进入
            if (!s_Initialized) return;

            if (errorCode != 0)
            {
                MVXRSDKLog.Warning($"BusinessManager OnDeviceGameStartPush errorCode: {errorCode}");
                return;
            }
            if (!SocketSystem.TryParse<global::DeviceGameStartPush>(buffer, out var msg, "Business.DeviceGameStart")) return;

            // 仅目标设备响应：推送的是「应开始游戏的设备 SN」，非本机直接忽略
            if (msg.DeviceId != MVXRSDK.DeviceId) return;

            MVXRSDKLog.Info($"BusinessManager: DeviceGameStart 命中本机，开始游戏 teamTag={msg.TeamTag}");
            MVXRSDK.RaiseTransactionVerification(true);
        }




    }
}