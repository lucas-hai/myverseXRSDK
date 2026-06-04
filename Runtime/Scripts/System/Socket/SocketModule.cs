using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Google.Protobuf;
using Protobuf;
using UnityEngine;

namespace MyVerseXRSDK
{
    public delegate void MessageCallBack(int errorCode, byte[] buffer);

    public class MessageSendData
    {
        public int Index;
        public string MessageType;
        public MessageCallBack CallBack;
        public bool IsLog;
        public float SendTime;
    }

    public class MessageReciveData
    {
        public string MessageType;
        public MessageCallBack CallBack;
        public bool IsLog;
    }

    public class SocketModule
    {
        /// <summary>请求-应答超时时 MessageCallBack 返回的 errorCode。订阅方据此区分超时与协议错误。</summary>
        public const int RequestTimeoutCode = -999;

        #region 状态机

        private enum SocketState
        {
            Disconnected,   // 空闲，无 WebSocket 实例
            Connecting,     // ConnectAsync 已发起，等待回调或超时
            Connected,      // 已连接，正常通信
            WaitingRetry,   // 退避等待中，到期后进入 Connecting
            Failed          // 重连次数耗尽
        }

        private SocketState m_State = SocketState.Disconnected;
        private float m_StateEnterTime;

        private void EnterState(SocketState newState)
        {
            MVXRSDKLog.Debug($"SocketModule:状态 {m_State} → {newState}");
            m_State = newState;
            m_StateEnterTime = Time.realtimeSinceStartup;
        }

        #endregion

        #region 配置 / 协议常量

        private const float CONNECT_TIMEOUT = 10f;
        private const float RETRY_DELAY_INIT = 2f;
        private const float RETRY_DELAY_MAX = 30f;
        private const int MAX_ATTEMPTS = 5;
        private const float REQUEST_TIMEOUT_SEC = 30f;

        // 服务端踢线指令：单字节 payload [3]。来源：中控登录会话冲突时主动关闭旧连接。
        internal const byte ServerControlByte_Kick = 3;
        // 客户端发包前缀字节：[2] = 普通业务消息（WSMessage）。
        internal const byte ClientFrame_Message = 2;

        #endregion

        #region 字段

        public WebSocket WebSocket { get; private set; }
        public bool IsConnect => m_State == SocketState.Connected;

        private string m_Address = "";
        private Action<bool> m_ConnectSuccess;
        private int m_SendMessageIndex;
        private readonly Dictionary<long, MessageSendData> m_ClientRequests = new();
        private readonly Dictionary<string, List<MessageReciveData>> m_ClientSubscribes = new();
        private readonly List<long> m_TimeoutKeys = new();

        private int m_Attempt;
        private float m_RetryDelay;

        #endregion

        #region 连接 / 断开

        public void Connect(string url, Action<bool> connectSuccess = null)
        {
            m_ConnectSuccess = connectSuccess;
            m_Address = $"ws://{url}";
            m_Attempt = 0;
            MVXRSDKLog.Info($"SocketModule:开始连接 → {m_Address}");
            DoConnect();
        }

        public void DisConnect()
        {
            MVXRSDKLog.Info($"SocketModule:主动断开连接 → {m_Address}");
            CloseWebSocket();
            FailAllPendingRequests(-1);
            EnterState(SocketState.Disconnected);
        }

        private void DoConnect()
        {
            if (m_Attempt > 0)
                MVXRSDKLog.Info($"SocketModule:第 {m_Attempt}/{MAX_ATTEMPTS} 次重连 → {m_Address}");

            CloseWebSocket();
            try
            {
                WebSocket = new WebSocket(m_Address);
                AddHandle();
                WebSocket.ConnectAsync();
                EnterState(SocketState.Connecting);
            }
            catch (Exception ex)
            {
                MVXRSDKLog.Error($"SocketModule:启动连接异常 → {ex.Message}");
                HandleConnectionFailed();
            }
        }

        private void CloseWebSocket()
        {
            if (WebSocket != null)
            {
                RemoveHandle();
                WebSocket.CloseAsync();
                WebSocket = null;
            }
        }

        #endregion

        #region OnUpdate 状态驱动

        public void OnUpdate()
        {
            WebSocket?.Update();

            switch (m_State)
            {
                case SocketState.Connecting:
                    if (Time.realtimeSinceStartup - m_StateEnterTime >= CONNECT_TIMEOUT)
                    {
                        MVXRSDKLog.Warning($"SocketModule:连接超时（{CONNECT_TIMEOUT}s）");
                        HandleConnectionFailed();
                    }
                    break;

                case SocketState.Connected:
                    CheckRequestTimeouts();
                    break;

                case SocketState.WaitingRetry:
                    if (Time.realtimeSinceStartup - m_StateEnterTime >= m_RetryDelay)
                    {
                        DoConnect();
                    }
                    break;
            }
        }

        #endregion

        #region 统一失败处理

        private void HandleConnectionFailed()
        {
            CloseWebSocket();
            FailAllPendingRequests(-1);
            m_Attempt++;

            if (m_Attempt > MAX_ATTEMPTS)
            {
                EnterState(SocketState.Failed);
                MVXRSDKLog.Error($"SocketModule:重连次数已耗尽（{MAX_ATTEMPTS}），停止重连");
                // 状态机：曾经 Connected 才进入 Disconnected；未连接成功过的不动 State
                if (MVXRSDK.State == MVXRSDKState.Connected)
                {
                    MVXRSDK.SetState(MVXRSDKState.Disconnected);
                }
                EventSystem.EventTrigger(MVXRSDKEventType.SOCKET_RECONNECT_FAILED);
            }
            else
            {
                m_RetryDelay = Mathf.Min(RETRY_DELAY_INIT * Mathf.Pow(2, m_Attempt - 1), RETRY_DELAY_MAX);
                float jitter = UnityEngine.Random.Range(-0.1f, 0.1f) * m_RetryDelay;
                m_RetryDelay = Mathf.Max(1f, m_RetryDelay + jitter);
                EnterState(SocketState.WaitingRetry);
                MVXRSDKLog.Warning($"SocketModule:等待重连（{m_Attempt}/{MAX_ATTEMPTS}），{m_RetryDelay:F1}s 后重试");
            }
        }

        #endregion

        #region WebSocket 回调

        private void AddHandle()
        {
            WebSocket.OnOpen += OnOpen;
            WebSocket.OnMessage += OnMessage;
            WebSocket.OnClose += OnClosed;
            WebSocket.OnError += OnError;
        }

        private void RemoveHandle()
        {
            WebSocket.OnOpen -= OnOpen;
            WebSocket.OnMessage -= OnMessage;
            WebSocket.OnClose -= OnClosed;
            WebSocket.OnError -= OnError;
        }

        private void OnOpen(object sender, OpenEventArgs e)
        {
            bool wasReconnecting = m_Attempt > 0;
            m_Attempt = 0;
            EnterState(SocketState.Connected);
            if (wasReconnecting)
                MVXRSDKLog.Info($"SocketModule:重连成功 → {m_Address}");
            else
                MVXRSDKLog.Info($"SocketModule:连接成功 → {m_Address}");
            m_ConnectSuccess?.Invoke(true);
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            MVXRSDKLog.Error($"SocketModule:连接错误 → {e.Message}");
            if (m_State == SocketState.Connecting || m_State == SocketState.Connected)
                HandleConnectionFailed();
        }

        private void OnClosed(object sender, CloseEventArgs e)
        {
            MVXRSDKLog.Warning($"SocketModule:连接关闭 code={e.Code} reason={e.Reason}");
            if (m_State == SocketState.Connecting || m_State == SocketState.Connected)
                HandleConnectionFailed();
        }

        private void OnMessage(object sender, MessageEventArgs e)
        {
            if (!e.IsBinary) return;

            byte[] data = e.RawData;
            // 账号异常处理（踢线）
            if (data.Length == 1 && data[0] == ServerControlByte_Kick)
            {
                MVXRSDKLog.Warning("SocketModule:收到踢线指令");
                CloseWebSocket();
                FailAllPendingRequests(-1);
                EnterState(SocketState.Disconnected);
                return;
            }

            if (!SocketSystem.TryParse<WSResponse>(data, out var response, "Socket.WSResponse"))
            {
                // 整条 WS 帧无法解析：吞掉，依赖下一帧；避免单个坏帧拖垮 OnMessage 分发通路
                return;
            }
            byte[] responseData = response.Data.ToByteArray();
            if (response.Zip)
            {
                responseData = GZipDecompress(responseData);
            }

            if (m_ClientRequests.ContainsKey(response.ReqId))
            {
                var cr = m_ClientRequests[response.ReqId];
                cr.CallBack(response.Code, responseData);
                m_ClientRequests.Remove(response.ReqId);
                return;
            }

            if (m_ClientSubscribes.ContainsKey(response.MessageType))
            {
                var cd = m_ClientSubscribes[response.MessageType];
                for (int i = 0; i < cd.Count; i++)
                {
                    cd[i].CallBack(response.Code, responseData);
                }
            }
        }

        #endregion

        #region 消息收发

        public void SendMessage(string api, ByteString buffer = null, MessageCallBack callBack = null, bool isLog = true)
        {
            if (!IsConnect) return;

            if (callBack != null)
            {
                m_SendMessageIndex++;
                m_ClientRequests.Add(m_SendMessageIndex, new MessageSendData
                {
                    Index = m_SendMessageIndex,
                    MessageType = api,
                    CallBack = callBack,
                    IsLog = isLog,
                    SendTime = Time.realtimeSinceStartup
                });
            }

            int dotIndex = api.IndexOf('.');
            WSMessage wsMessage = new WSMessage
            {
                Module = api[..dotIndex],
                ServiceName = api[(dotIndex + 1)..],
                Data = buffer,
                ReqId = m_SendMessageIndex
            };

            byte[] data = wsMessage.ToByteArray();
            byte[] bytes = new byte[data.Length + 1];
            bytes[0] = ClientFrame_Message;
            Array.Copy(data, 0, bytes, 1, data.Length);
            WebSocket.SendAsync(bytes);
        }

        public void SubscribeMessage(string api, MessageCallBack callBack, bool isLog = true)
        {
            if (!m_ClientSubscribes.ContainsKey(api))
            {
                m_ClientSubscribes.Add(api, new List<MessageReciveData>());
            }
            m_ClientSubscribes[api].Add(new MessageReciveData
            {
                MessageType = api,
                CallBack = callBack,
                IsLog = isLog
            });
        }

        public void UnsubscribeMessage(string api)
        {
            m_ClientSubscribes.Remove(api);
        }

        #endregion

        /// <summary>
        /// 内联 GZip 解压（v2 PR-9：从 Tools/GZipUtils 移入此处，避免 1 处调用拖一个工具类）。
        /// </summary>
        private static byte[] GZipDecompress(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return bytes;
            using var src = new MemoryStream(bytes);
            using var gz = new GZipStream(src, CompressionMode.Decompress);
            using var dst = new MemoryStream();
            gz.CopyTo(dst);
            return dst.ToArray();
        }

        #region 超时清理

        private void CheckRequestTimeouts()
        {
            if (m_ClientRequests.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            m_TimeoutKeys.Clear();
            foreach (var kv in m_ClientRequests)
            {
                if (now - kv.Value.SendTime >= REQUEST_TIMEOUT_SEC)
                    m_TimeoutKeys.Add(kv.Key);
            }
            foreach (var key in m_TimeoutKeys)
            {
                m_ClientRequests[key].CallBack?.Invoke(RequestTimeoutCode, null);
                m_ClientRequests.Remove(key);
                MVXRSDKLog.Warning($"SocketModule:请求超时已清理 key={key}");
            }
        }

        private void FailAllPendingRequests(int code)
        {
            if (m_ClientRequests.Count == 0) return;
            foreach (var kv in m_ClientRequests)
            {
                kv.Value.CallBack?.Invoke(code, null);
            }
            m_ClientRequests.Clear();
        }

        #endregion
    }
}
