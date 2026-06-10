using System;
using System.Collections;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流会话状态机：编排 IWebRTCSession + WhipClient 完成协商。
    /// 通过工厂注入会话与 HTTP 实现，便于测试注入 mock。
    ///
    /// 协程化：WHIP 握手（POST）走 MonoSystem.Start_Coroutine 启动的协程，**不阻塞主线程**。
    /// 协程期间外部可能调用 Stop 改写状态，本类在 yield return 后做"脏写防护"检查 State，
    /// 不一致则丢弃后续操作避免错乱。
    /// </summary>
    internal class PushStreamModule
    {
        internal PushStreamState State { get; private set; } = PushStreamState.Idle;
        internal string CurrentWhipUrl { get; private set; }

        /// <summary>
        /// 集中状态切换：当前只做 log + 赋值，不做 transition matrix 校验（3 状态规模太小）。
        /// 未来若加 Restarting / Stopping 等中间态，在此集中加白名单。
        /// </summary>
        private void SetState(PushStreamState next)
        {
            if (State == next) return;
            MVXRSDKLog.Debug($"PushStreamModule: 状态转换 {State} → {next}");
            State = next;
        }

        private Func<RenderTexture> m_GetSource = () => null;
        private Func<IWebRTCSession> m_SessionFactory = () => null;
        private Func<WhipClient> m_WhipFactory = () => null;

        private IWebRTCSession m_Session;
        private string m_Location;   // WHIP 返回的 stream session URL，用于 DELETE
        // 当前协程使用的 WhipClient；Stop 时复用其 Delete 协程发送注销，避免重新构造
        private WhipClient m_CurrentWhip;

        internal event Action<string> OnStarted;
        internal event Action<StreamStopReason> OnStopped;
        internal event Action<MVXRSDKErrorCode, string> OnFailed;
        internal event Action<StreamStats> OnStats;

        internal void SetDependencies(
            Func<RenderTexture> getSource,
            Func<IWebRTCSession> sessionFactory,
            Func<WhipClient> whipFactory)
        {
            m_GetSource = getSource ?? (() => null);
            m_SessionFactory = sessionFactory ?? (() => null);
            m_WhipFactory = whipFactory ?? (() => null);
        }

        internal void Start(string whipUrl)
        {
            if (State != PushStreamState.Idle)
            {
                MVXRSDKLog.Warning($"PushStreamModule: 当前非 Idle 状态({State})，幂等忽略 Start url={whipUrl}");
                return;
            }

            if (string.IsNullOrEmpty(whipUrl))
            {
                MVXRSDKLog.Error("PushStreamModule: whipUrl 为空，拒绝启动推流");
                OnFailed?.Invoke(MVXRSDKErrorCode.WhipPostFailed, "whipUrl empty");
                return;
            }

            SetState(PushStreamState.Starting);
            CurrentWhipUrl = whipUrl;
            MVXRSDKLog.Info($"PushStreamModule: 开始推流 url={whipUrl}");

            m_Session = m_SessionFactory();
            if (m_Session == null)
            {
                FailAndReset(MVXRSDKErrorCode.WebRTCInitFailed, "WebRTCSystem factory returned null");
                return;
            }

            m_Session.OnLocalSdpReady += OnLocalSdpReady;
            m_Session.OnConnected += OnConnected;
            m_Session.OnFailed += OnSessionFailed;
            m_Session.OnStatsUpdated += OnSessionStats;
            // 注：不订阅 m_Session.OnDisconnected——WebRTCSystem 内部已在 Disconnected 时启自愈
            // 协程，超时后会通过 OnFailed 上报；上游不需要短暂断流的中间信号

            m_Session.Start(m_GetSource());
        }

        private void OnSessionStats(StreamStats stats)
        {
            OnStats?.Invoke(stats);
        }

        private void OnLocalSdpReady(string sdpOffer)
        {
            // 由 WebRTCSystem 在主线程触发；启动独立协程跑 WHIP 握手，不阻塞主线程
            // capture 当前 session / url：协程跑完时 m_Session / CurrentWhipUrl 可能已被 Stop/Start
            // 改成新 session，必须用 capture 值，避免把旧 answer 喂给新 session 协议错乱
            var session = m_Session;
            var url = CurrentWhipUrl;
            MonoSystem.Start_Coroutine(WhipHandshakeCoroutine(sdpOffer, url, session));
        }

        private IEnumerator WhipHandshakeCoroutine(string sdpOffer, string whipUrl, IWebRTCSession session)
        {
            var whip = m_WhipFactory();
            if (whip == null)
            {
                if (session == m_Session)
                    FailAndReset(MVXRSDKErrorCode.WhipPostFailed, "WhipClient factory returned null");
                yield break;
            }
            // 只有当前 gen 才更新 m_CurrentWhip 引用（避免旧协程覆盖新 session 的 whip 实例）
            if (session == m_Session) m_CurrentWhip = whip;

            // 全局握手超时：覆盖 WhipClient.Post 内部重试总耗时 + 极端 transport 死锁的兜底
            // 单次 POST 30s × 4 次重试 + 间隔 1+3+8 ≈ 132s，再加 30s buffer = 默认 60s 不够，要 ~150s
            // StreamConfig.WhipHandshakeTimeoutSec 让业务可调
            float deadline = Time.realtimeSinceStartup + StreamConfig.Active.WhipHandshakeTimeoutSec;
            WhipResponse resp = null;
            var postCo = MonoSystem.Start_Coroutine(WhipPostWrapper(whip, whipUrl, sdpOffer, r => resp = r));
            while (resp == null && Time.realtimeSinceStartup < deadline)
            {
                if (session != m_Session) yield break;  // 期间发生了 Stop/Start，立刻退出，留 cleanup 给主路径
                yield return null;
            }
            if (resp == null)
            {
                if (postCo != null) MonoSystem.Stop_Coroutine(postCo);
                if (session == m_Session)
                    FailAndReset(MVXRSDKErrorCode.WhipPostFailed, $"WHIP 握手超时 {StreamConfig.Active.WhipHandshakeTimeoutSec}s");
                yield break;
            }

            // 串台防护：协程期间业务侧 Stop+Start 会换 m_Session，旧 answer 必须丢弃，
            // 否则会被错误地 SetRemoteAnswer 到新 session 导致协议错乱
            if (session != m_Session)
            {
                MVXRSDKLog.Info($"PushStreamModule: WHIP 握手期间 session 已被替换，丢弃旧 answer url={whipUrl}");
                yield break;
            }

            // 状态二次检查：理论上 session 检查已覆盖大多数场景，留为防御性兜底
            if (State != PushStreamState.Starting)
            {
                MVXRSDKLog.Info($"PushStreamModule: WHIP 握手期间状态已变为 {State}，丢弃 answer");
                yield break;
            }

            if (!resp.Success)
            {
                FailAndReset(resp.ErrorCode, resp.ErrorMsg ?? "WhipClient.Post failed");
                yield break;
            }

            m_Location = resp.Location;
            m_Session.SetRemoteAnswer(resp.SdpAnswer);
            // 后续等 WebRTCSystem 的 OnConnected / OnFailed 回调
        }

        // 包装 Post 为可独立 Stop 的 coroutine（不能 Stop_Coroutine 直接的 IEnumerator 嵌套，需要 wrapper）
        private static IEnumerator WhipPostWrapper(WhipClient whip, string url, string sdp, Action<WhipResponse> onDone)
        {
            yield return whip.Post(url, sdp, onDone);
        }

        private void OnConnected()
        {
            SetState(PushStreamState.Started);
            OnStarted?.Invoke(CurrentWhipUrl);
        }

        private void OnSessionFailed(MVXRSDKErrorCode code, string msg)
        {
            FailAndReset(code, msg);
        }

        private void FailAndReset(MVXRSDKErrorCode code, string msg)
        {
            MVXRSDKLog.Error($"PushStreamModule: 推流失败 code={code}({(int)code}) msg={msg}");
            CleanupSession();
            CurrentWhipUrl = null;
            m_Location = null;
            SetState(PushStreamState.Idle);
            OnFailed?.Invoke(code, msg);
        }

        internal void Stop(StreamStopReason reason)
        {
            if (State == PushStreamState.Idle)
            {
                MVXRSDKLog.Warning("PushStreamModule: 当前未在推流，忽略 Stop");
                return;
            }

            MVXRSDKLog.Info($"PushStreamModule: 停止推流 url={CurrentWhipUrl} reason={reason}");
            FireDelete();  // fire-and-forget：立即清空 Location，不等 DELETE 完成
            CleanupSession();
            CurrentWhipUrl = null;
            m_Location = null;
            SetState(PushStreamState.Idle);
            OnStopped?.Invoke(reason);
        }

        internal void HandleSocketReconnectFailed()
        {
            if (State == PushStreamState.Idle) return;
            MVXRSDKLog.Warning($"PushStreamModule: WS 重连失败，异常停止推流 url={CurrentWhipUrl}");
            FireDelete();
            CleanupSession();
            CurrentWhipUrl = null;
            m_Location = null;
            SetState(PushStreamState.Idle);
            OnStopped?.Invoke(StreamStopReason.NetworkLost);
        }

        private void FireDelete()
        {
            if (string.IsNullOrEmpty(m_Location)) return;
            var whip = m_CurrentWhip ?? m_WhipFactory();
            if (whip == null) return;
            string loc = m_Location;
            // 协程内置 3 次重试 + 200/204/404 视为成功，主流程无需等待
            MonoSystem.Start_Coroutine(whip.Delete(loc));
        }

        private void CleanupSession()
        {
            m_CurrentWhip = null;
            if (m_Session == null) return;
            m_Session.OnLocalSdpReady -= OnLocalSdpReady;
            m_Session.OnConnected -= OnConnected;
            m_Session.OnFailed -= OnSessionFailed;
            m_Session.OnStatsUpdated -= OnSessionStats;
            // IWebRTCSession 合并 Stop/Dispose 后只调 Dispose；内部已包含 Close + 协程清理
            try { m_Session.Dispose(); } catch { /* ignore */ }
            m_Session = null;
        }
    }
}
