using System;
using System.Collections.Generic;
using Google.Protobuf;
using UnityEngine;

namespace MyVerseXRSDK

{

    public static class SocketSystem
    {
        private static SocketModule m_Server;

        /// <summary>
        /// 是否正在登录过程中
        /// </summary>
        private static bool m_IsLogin;

        /// <summary>
        /// 当前 WebSocket 是否处于已连接状态。SDK 初始化前为 false。
        /// SDK 内部使用；业务方请用 <see cref="MVXRSDK.IsConnected"/>（v2 API）。
        /// </summary>
        internal static bool IsConnect => m_Server != null && m_Server.IsConnect;

        public static void Init()
        {

            m_Server = new SocketModule();

        }

        public static void OnUpdate()
        {

            m_Server.OnUpdate();

        }

        public static void Clear()
        {
            // 守卫必须最先：未 Init 或 UnInit 后调用（OnApplicationQuit 路径）时
            // m_Server 与 MonoSystem.instance 都可能为 null，提前返回避开两处 NRE
            if (m_Server == null) return;
            MonoSystem.RemoveUpdateListener(OnUpdate);
            m_Server.DisConnect();
        }

        /// <summary>
        /// SDK UnInit 末尾调用：释放 SocketModule 实例引用 + 重置登录标志，避免二次 Init 时静态字段残留。
        /// 必须在 Clear()（断连）之后调用，确保 WebSocket 已关闭。
        /// </summary>
        internal static void ResetAfterUnInit()
        {
            m_Server = null;
            m_IsLogin = false;
        }


        #region Protobuf 反序列化兜底

        /// <summary>
        /// 把 buffer 反序列化为 protobuf 消息 T。失败仅记日志返回 false，不抛异常。
        /// 空 buffer 视作"全默认字段"成功，对齐既有 if(buffer != null && buffer.Length > 0) 写法，
        /// 调用方不必再手写空判断。失败时 msg 是 new T() 默认实例（非 null）。
        /// </summary>
        /// <param name="contextTag">日志定位用，例如 "Business.RoomAttributePush"。</param>
        public static bool TryParse<T>(byte[] buffer, out T msg, string contextTag = null)
            where T : IMessage<T>, new()
        {
            msg = new T();
            if (buffer == null || buffer.Length == 0) return true;
            try
            {
                msg.MergeFrom(buffer);
                return true;
            }
            catch (InvalidProtocolBufferException ex)
            {
                MVXRSDKLog.Error($"SocketSystem.TryParse<{typeof(T).Name}> 解析失败 ctx={contextTag} len={buffer.Length} err={ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                MVXRSDKLog.Error($"SocketSystem.TryParse<{typeof(T).Name}> 异常 ctx={contextTag} err={ex.GetType().Name}:{ex.Message}");
                return false;
            }
        }

        #endregion


        #region 消息注册与接收
        internal static void RegisterMessage(string messageType, MessageCallBack callBack, bool debugLog = true)
        {
            m_Server.SubscribeMessage(messageType, callBack, debugLog);
        }

        public static void CancelMessage(string messageType)
        {
            m_Server.UnsubscribeMessage(messageType);
        }
        public static void SendMessage(string messageType, ByteString buffer, MessageCallBack callBack = null, bool debugLog = true)
        {
            m_Server.SendMessage(messageType, buffer, callBack, debugLog);
        }

        #endregion


        public static void ConnectServer(string ip, Action<bool> callBack)
        {

            if (m_IsLogin || m_Server.IsConnect)
            {
                return;
            }
            MonoSystem.AddUpdateListener(OnUpdate);
            m_IsLogin = true;
            m_Server.Connect(ip, (bool isResult) =>
            {
                m_IsLogin = false;
                callBack?.Invoke(isResult);

            });
        }




    }

}

