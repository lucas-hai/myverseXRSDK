using System.Collections;
using Google.Protobuf;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流/录屏业务域门面：编排 PushStreamModule 与 RecordModule、订阅/转发 WS 消息、
    /// 转发事件到 MVXRSDK Facade 与 EventSystem。
    /// 协议：NotifyLive（SC 推送，合并 Start/Stop）+ logic.StartRecord（CS 请求-应答）。
    ///
    /// 重构后的职责严格收缩到 Manager 本职（编排 + 事件转发）；
    /// RT / Camera / Blit 由 <see cref="TextureProviderSystem"/> 接管，
    /// 音频混音 + Feeder 由 <see cref="AudioMixingSystem"/> 接管，
    /// WebRTC 会话由 <see cref="WebRTCSystem"/> 实现并被 PushStreamModule 注入。
    /// </summary>
    internal static class StreamManager
    {
        private static PushStreamModule m_PushModule;
        private static RecordModule m_RecordModule;
        private static bool m_Initialized;
        // 最近一次 Stats 快照，供 MVXRSDK.GetStreamStats() 同步查询
        private static StreamStats s_LatestStats;
        // 推流自动重试状态：缓存最近一次 NotifyLive(start) 的 URL，OnFailed 时按 StreamConfig
        // 节奏自动调 PushModule.Start 重试；覆盖播控重启后中控未及时再推 NotifyLive 的窗口。
        private static string s_LastStreamUrl;
        private static int s_RetryAttempts;
        private static Coroutine s_StreamRetryCoroutine;
        // 代次 counter：ApplyNotifyLive(stop) / 新 NotifyLive / UnInitSDK 时 ++；coroutine 内
        // capture 启动时的 gen，yield 完成后比对，不一致直接 yield break。避免 CancelStreamRetry
        // 在 coroutine 已通过检查、即将调 Start 的窗口里失效（field 已 null 但 Start 仍会执行）
        private static int s_StreamRetryGen;

        // === 生命周期 ===

        internal static void InitSDK()
        {
            if (m_Initialized)
            {
                MVXRSDKLog.Warning("StreamManager: 已初始化，跳过重复 Init");
                return;
            }

            // System 层依赖：必须在 Module 构造前 Init
            AudioMixingSystem.Init();
            TextureProviderSystem.Init();

            m_PushModule = new PushStreamModule();
            m_PushModule.SetHasSourcePredicate(() => TextureProviderSystem.HasSource);
            m_PushModule.SetDependencies(
                getSource: () => TextureProviderSystem.InternalRT,
                sessionFactory: () => new WebRTCSystem(),
                whipFactory: () => new WhipClient(new UnityWebRequestHttpTransport())
            );
            m_PushModule.OnStarted += HandlePushStarted;
            m_PushModule.OnStopped += HandlePushStopped;
            m_PushModule.OnFailed  += HandlePushFailed;
            m_PushModule.OnStats   += HandlePushStats;

            m_RecordModule = new RecordModule(
                isConnected: () => SocketSystem.IsConnect,
                sendRequest: (msgType, buffer, cb) => SocketSystem.SendMessage(msgType, buffer, (code, buf) => cb(code, buf))
            );
            m_RecordModule.OnResult += HandleRecordResult;

            // Director 切镜：SDK 只透传 pb，不做编排。业务在上层（MVXRStreamRig）处理倒计时 / 切回 / 本机判定
            SocketSystem.RegisterMessage(MessageType.SC_NOTIFY_LIVE, OnNotifyLiveMessage);
            SocketSystem.RegisterMessage(MessageType.SC_DIRECTOR_SELECTED, OnDirectorSelectedPush);
            EventSystem.AddEventListener(MVXRSDKEventType.SOCKET_RECONNECT_FAILED, OnSocketReconnectFailed);

            m_Initialized = true;
            MVXRSDKLog.Info("StreamManager: 初始化完成");
        }

        internal static void UnInitSDK()
        {
            if (!m_Initialized) return;

            SocketSystem.CancelMessage(MessageType.SC_NOTIFY_LIVE);
            SocketSystem.CancelMessage(MessageType.SC_DIRECTOR_SELECTED);
            EventSystem.RemoveEventListener(MVXRSDKEventType.SOCKET_RECONNECT_FAILED, OnSocketReconnectFailed);

            // 运行时 transient state 一次性 reset（缓存 URL / retry counter / stats / retry 协程）
            ResetTransientState();

            if (m_PushModule != null)
            {
                // A3 修复：UnInit 前必须先 Stop 推流——否则 IWebRTCSession.Dispose 不会被触发，
                // PeerConnection / VideoStreamTrack 泄漏；紧接着 TextureProviderSystem.Dispose
                // 释放 RT 会让 VideoStreamTrack 引用悬挂。Stop 在 Idle 状态下自身短路无副作用，
                // 所以无需先判 State
                m_PushModule.Stop(StreamStopReason.SdkUnInit);

                m_PushModule.OnStarted -= HandlePushStarted;
                m_PushModule.OnStopped -= HandlePushStopped;
                m_PushModule.OnFailed  -= HandlePushFailed;
                m_PushModule.OnStats   -= HandlePushStats;
                m_PushModule = null;
            }
            if (m_RecordModule != null)
            {
                m_RecordModule.OnResult -= HandleRecordResult;
                m_RecordModule = null;
            }

            // 反向顺序释放 System 层
            TextureProviderSystem.Dispose();
            AudioMixingSystem.Dispose();
            m_Initialized = false;
            MVXRSDKLog.Info("StreamManager: 已反初始化");
        }

        /// <summary>
        /// 集中清理"运行时 transient state"——推流自动重试缓存、stats 快照等。
        /// 不动 module 引用（PushModule/RecordModule）和 m_Initialized，这些由 UnInit 主流程处理。
        /// 抽出来便于：
        ///   - UnInit 调用（简化主流程）
        ///   - 单元测试做场景隔离（reset 到干净状态）
        /// 注：s_StreamRetryGen 不重置——保持 monotonic 增长，避免 reset 后旧协程 gen 匹配上
        /// </summary>
        private static void ResetTransientState()
        {
            CancelStreamRetry();
            s_LastStreamUrl = null;
            s_RetryAttempts = 0;
            s_LatestStats = null;
        }

        // === 对 MVXRSDK Facade 暴露的方法 ===

        /// <summary>
        /// 游戏侧自己渲染到一张 RenderTexture（任意格式），SDK 每帧 Blit 转换到内部合法格式。
        /// 性能：每帧多一次 GPU Blit（约 0.2-0.5ms）。
        /// 推流期间切到不同尺寸的 RT 需先 ClearStreamSource。
        /// </summary>
        internal static void SetStreamSource(RenderTexture source)
        {
            if (source == null)
            {
                if (m_PushModule != null && m_PushModule.State != PushStreamState.Idle)
                {
                    MVXRSDKLog.Warning("StreamManager: 推流期间清空 source，VideoStreamTrack 将失去画面");
                }
                TextureProviderSystem.ClearSource();
                return;
            }
            TextureProviderSystem.SwitchSource(new RenderTextureStreamSource(source));
        }

        /// <summary>
        /// 高级用法：业务侧自己实现 <see cref="IStreamSource"/> 直接交给 SDK。
        /// 相比 RT 重载，业务可订阅 source 的 OnAttached/OnDetached 生命周期事件做精细控制
        /// （例如切走时暂停自家采集 Blit，避免空跑 GPU）。
        /// MVXRStreamRig 内部就是用这条路径包装 RenderTextureStreamSource / CameraStreamSource。
        /// </summary>
        internal static void SetStreamSource(IStreamSource source)
        {
            if (source == null)
            {
                if (m_PushModule != null && m_PushModule.State != PushStreamState.Idle)
                {
                    MVXRSDKLog.Warning("StreamManager: 推流期间清空 source，VideoStreamTrack 将失去画面");
                }
                TextureProviderSystem.ClearSource();
                return;
            }
            TextureProviderSystem.SwitchSource(source);
        }

        internal static void ClearStreamSource()
        {
            TextureProviderSystem.ClearSource();
        }

        internal static void SetStreamConfig(StreamConfig config)
        {
            StreamConfig.Apply(config);
        }

        // === 查询接口（供 MVXRSDK Facade 转发）===

        internal static bool IsStreaming =>
            m_Initialized && m_PushModule != null && m_PushModule.State == PushStreamState.Started;

        internal static string CurrentStreamUrl =>
            m_Initialized && m_PushModule != null ? m_PushModule.CurrentWhipUrl : null;

        internal static StreamStats GetStreamStats() => s_LatestStats;

        internal static void StartRecord(StartRecordOptions opts)
        {
            if (!m_Initialized)
            {
                MVXRSDKLog.Warning("StreamManager: SDK 未初始化，拒绝 StartRecord");
                MVXRSDK.RaiseRecordResult(MVXRSDKErrorCode.NotInitialized, "SDK not initialized");
                return;
            }
            m_RecordModule.StartRecord(opts);
        }

        /// <summary>
        /// 业务侧请求中控仲裁切镜：把请求发给 WebSocket，是否真切由 server 推 DirectorSelectedPush 决定。
        /// SDK 仅做 pb 发送；上层（业务/MVXRStreamRig）订阅 MVXRSDK.OnDirectorSelected 自行决定如何响应。
        /// </summary>
        internal static void SendDirectorRequest(int lenses, int durationSec)
        {
            if (!m_Initialized)
            {
                MVXRSDKLog.Warning("StreamManager: SDK 未初始化，拒绝 SendDirectorRequest");
                return;
            }
            if (durationSec <= 0)
            {
                MVXRSDKLog.Error($"StreamManager.SendDirectorRequest: durationSec={durationSec} 必须 > 0");
                return;
            }
            if (!SocketSystem.IsConnect)
            {
                MVXRSDKLog.Warning("StreamManager.SendDirectorRequest: WebSocket 未连接，请求被丢弃");
                return;
            }
            var req = new global::DirectorInsert.Types.Request { Lenses = lenses, DurationSec = durationSec };
            SocketSystem.SendMessage(MessageType.CS_DIRECTOR_INSERT, req.ToByteString());
            MVXRSDKLog.Info($"StreamManager: 发 DirectorInsert lenses={lenses} duration={durationSec}");
        }

        internal static void PushGameAudioPcm(float[] pcm, int sampleRate, int channels)
        {
            if (!m_Initialized) return;
            AudioMixingSystem.PushGameAudio(pcm, sampleRate, channels);
        }

        internal static void PushMicPcm(float[] pcm, int sampleRate, int channels)
        {
            if (!m_Initialized) return;
            AudioMixingSystem.PushMicAudio(pcm, sampleRate, channels);
        }

        // === WS 消息处理（解析 pb 真实数据）===

        private static void OnNotifyLiveMessage(int errorCode, byte[] buffer)
        {
            if (errorCode != 0)
            {
                MVXRSDKLog.Warning($"StreamManager: 收到 NotifyLive errorCode={errorCode}");
                return;
            }

            if (!SocketSystem.TryParse<global::NotifyLive>(buffer, out var notify, "Stream.NotifyLive")) return;

            MVXRSDKLog.Info($"StreamManager: 收到 NotifyLive start={notify.Start} url={notify.StreamServerIp} deviceId={notify.DeviceId}");
            ApplyNotifyLive(notify.StreamServerIp, notify.Start);
        }

        /// <summary>
        /// 应用一条 NotifyLive 业务语义到 PushStreamModule。
        /// 抽出来是为了让 Debug_SimulateNotifyLive 走完全相同的代码路径，
        /// 避免假 raise 事件的"假链路"问题。
        /// </summary>
        private static void ApplyNotifyLive(string url, bool start)
        {
            if (!m_Initialized || m_PushModule == null)
            {
                MVXRSDKLog.Warning("StreamManager: 未初始化，忽略 NotifyLive");
                return;
            }

            if (start)
            {
                if (string.IsNullOrEmpty(url))
                {
                    MVXRSDKLog.Error("StreamManager: NotifyLive whipUrl 为空");
                    MVXRSDK.RaisePushStreamFailed(MVXRSDKErrorCode.WhipPostFailed, "NotifyLive whipUrl empty");
                    return;
                }
                // 格式校验：畸形 URL 在 Manager 层直接拒绝，不浪费 PushModule 状态机和 WHIP POST 一轮失败
                if (!System.Uri.TryCreate(url, System.UriKind.Absolute, out var parsed) ||
                    (parsed.Scheme != System.Uri.UriSchemeHttp && parsed.Scheme != System.Uri.UriSchemeHttps))
                {
                    MVXRSDKLog.Error($"StreamManager: NotifyLive whipUrl 非法 url={url}");
                    MVXRSDK.RaisePushStreamFailed(MVXRSDKErrorCode.WhipPostFailed, $"NotifyLive whipUrl invalid: {url}");
                    return;
                }

                // 收到新 NotifyLive：取消可能在跑的自动重试 + 重置 attempt 计数 + 更新缓存 URL
                // 缓存的 URL 用于 OnFailed 后 SDK 自动 retry，覆盖 server 没再推 NotifyLive 的窗口
                CancelStreamRetry();
                s_RetryAttempts = 0;
                s_LastStreamUrl = url;

                // 播控文档 §4.2.1 幂等语义：当前已在推流时——
                //   URL 未变 → 忽略（中控可能重发同一条 start）
                //   URL 变更 → 断旧重连（播控可能换 IP / 重签 token）
                if (m_PushModule.State != PushStreamState.Idle)
                {
                    if (m_PushModule.CurrentWhipUrl == url)
                    {
                        MVXRSDKLog.Info($"StreamManager: NotifyLive(start) URL 未变，幂等忽略 url={url}");
                        return;
                    }
                    MVXRSDKLog.Info($"StreamManager: NotifyLive(start) URL 变更，断旧重连 old={m_PushModule.CurrentWhipUrl} new={url}");
                    m_PushModule.Stop(StreamStopReason.ConfigChanged);
                }

                m_PushModule.Start(url);
            }
            else
            {
                // server 主动 stop：清缓存 URL + 取消自动重试，不再尝试恢复
                s_LastStreamUrl = null;
                s_RetryAttempts = 0;
                CancelStreamRetry();
                m_PushModule.Stop(StreamStopReason.ServerStop);
            }
        }

        /// <summary>判断错误码是否值得自动重试——仅运行时可恢复错误（ICE / DTLS / 连接断开），
        /// 不对 4xx 鉴权 / 404 / codec / 无源 等配置类错误重试。</summary>
        private static bool IsRetriableStreamError(MVXRSDKErrorCode code)
        {
            switch (code)
            {
                case MVXRSDKErrorCode.IceConnectionFailed:
                case MVXRSDKErrorCode.DtlsHandshakeFailed:
                case MVXRSDKErrorCode.ConnectionLost:
                case MVXRSDKErrorCode.IceGatheringTimeout:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>OnPushStreamFailed 后调度一次自动重试。重试节奏由 StreamConfig 控制；
        /// 达到上限后停止，等 server 再推 NotifyLive 或业务侧主动触发。</summary>
        private static void ScheduleStreamRetry()
        {
            var delays = StreamConfig.Active.PushStreamRetryDelaysMs;
            if (delays == null || delays.Length == 0)
            {
                MVXRSDKLog.Info("StreamManager: PushStreamRetryDelaysMs 为空，不自动重试");
                return;
            }
            if (s_RetryAttempts >= delays.Length)
            {
                MVXRSDKLog.Warning($"StreamManager: 推流连续失败 {s_RetryAttempts} 次，自动重试已耗尽，等 server 再推 NotifyLive 或业务侧重启");
                return;
            }
            int delayMs = delays[s_RetryAttempts];
            int attemptIdx = s_RetryAttempts + 1;
            s_RetryAttempts++;

            CancelStreamRetry();
            int genSnapshot = s_StreamRetryGen;  // capture：协程内对比这个值判断是否已被作废
            MVXRSDKLog.Info($"StreamManager: 调度推流自动重试 attempt={attemptIdx}/{delays.Length} delay={delayMs}ms url={s_LastStreamUrl}");
            s_StreamRetryCoroutine = MonoSystem.Start_Coroutine(StreamRetryCoroutine(delayMs, attemptIdx, delays.Length, genSnapshot));
        }

        private static IEnumerator StreamRetryCoroutine(int delayMs, int attemptIdx, int totalAttempts, int gen)
        {
            yield return new WaitForSecondsRealtime(delayMs / 1000f);

            // gen 检查：协程等待期间发生了 NotifyLive(stop) / 新 NotifyLive / UnInitSDK 会 ++gen
            // 任何这些场景下，本协程的 Start 都不应该执行，直接 break。这比靠 s_LastStreamUrl
            // 检查更稳——后者在 yield 之后到 Start 调用之间还有 race 窗口
            if (gen != s_StreamRetryGen)
            {
                MVXRSDKLog.Info($"StreamManager: 推流自动重试取消（gen 已变 {gen}→{s_StreamRetryGen}，已被新 NotifyLive 或 stop 作废）");
                yield break;
            }

            string urlToTry = s_LastStreamUrl;
            if (string.IsNullOrEmpty(urlToTry))
            {
                MVXRSDKLog.Info("StreamManager: 推流自动重试取消（缓存 url 已清空）");
                yield break;
            }
            if (m_PushModule == null || m_PushModule.State != PushStreamState.Idle)
            {
                MVXRSDKLog.Info($"StreamManager: 推流自动重试取消（PushModule.State={m_PushModule?.State}，已有其它路径接管）");
                yield break;
            }

            MVXRSDKLog.Info($"StreamManager: 执行推流自动重试 attempt={attemptIdx}/{totalAttempts} url={urlToTry}");
            // 注：先调 Start 再清 field 顺序很重要，避免 Start 失败时 field 还指向已完成的协程
            m_PushModule.Start(urlToTry);
            s_StreamRetryCoroutine = null;
            // 后续：
            //   - Connected → HandlePushStarted，不会再触发 ScheduleStreamRetry，attempts 在下次 NotifyLive 重置
            //   - Failed → HandlePushFailed → 再次 ScheduleStreamRetry（attempts++，下一档延迟）
        }

        private static void CancelStreamRetry()
        {
            // ++gen 让已 yield 完成、还没到 Start 的协程也会被 gen 检查作废
            s_StreamRetryGen++;
            if (s_StreamRetryCoroutine != null)
            {
                MonoSystem.Stop_Coroutine(s_StreamRetryCoroutine);
                s_StreamRetryCoroutine = null;
            }
        }

        private static void OnSocketReconnectFailed()
        {
            m_PushModule?.HandleSocketReconnectFailed();
        }

        /// <summary>
        /// SC_DIRECTOR_SELECTED 路由：解析 pb 后直接 raise <see cref="MVXRSDK.OnDirectorSelected"/>
        /// 把基本类型透传给业务，不做任何编排。
        /// </summary>
        private static void OnDirectorSelectedPush(int errorCode, byte[] buffer)
        {
            if (errorCode != 0)
            {
                MVXRSDKLog.Warning($"StreamManager: 收到 DirectorSelectedPush errorCode={errorCode}");
                return;
            }
            if (!SocketSystem.TryParse<global::DirectorSelected>(buffer, out var msg, "Stream.DirectorSelected")) return;
            MVXRSDKLog.Info($"StreamManager: 收到 DirectorSelected deviceId={msg.DeviceId} isPrimary={msg.IsPrimary} slot={msg.Slot} duration={msg.DurationSec}");
            MVXRSDK.RaiseDirectorSelected(msg.DeviceId, msg.IsPrimary, msg.Slot, msg.DurationSec);
        }

        // === Module 事件 → Facade event + EventSystem 转发 ===

        private static void HandlePushStarted(string streamServerIp)
        {
            EventSystem.EventTrigger(MVXRSDKEventType.PUSH_STREAM_STARTED, streamServerIp);
            MVXRSDK.RaisePushStreamStarted(streamServerIp);
        }

        private static void HandlePushStopped(StreamStopReason reason)
        {
            // 停流后清空 stats 快照，避免业务侧拿到陈旧数据
            s_LatestStats = null;
            EventSystem.EventTrigger(MVXRSDKEventType.PUSH_STREAM_STOPPED, reason);
            MVXRSDK.RaisePushStreamStopped(reason);
        }

        private static void HandlePushStats(StreamStats stats)
        {
            s_LatestStats = stats;
            MVXRSDK.RaisePushStreamStats(stats);
        }

        private static void HandlePushFailed(MVXRSDKErrorCode code, string msg)
        {
            EventSystem.EventTrigger(MVXRSDKEventType.PUSH_STREAM_FAILED, code, msg);
            MVXRSDK.RaisePushStreamFailed(code, msg);

            // 仅对可恢复运行时错误自动重试，且需有缓存 URL（说明上一次是真有 NotifyLive 触发过）
            if (IsRetriableStreamError(code) && !string.IsNullOrEmpty(s_LastStreamUrl))
            {
                ScheduleStreamRetry();
            }
        }

        private static void HandleRecordResult(MVXRSDKErrorCode code, string errorMsg)
        {
            EventSystem.EventTrigger(MVXRSDKEventType.RECORD_RESULT, code, errorMsg);
            MVXRSDK.RaiseRecordResult(code, errorMsg);
        }

        // === Debug 入口：模拟一条 NotifyLive，供脱离中控的离线/直连测试使用 ===

        /// <summary>
        /// 模拟一条 NotifyLive 走真业务链路（绕开 SocketSystem），供 Offline / 手测场景使用。
        /// 前置条件：SDK 已 InitMVXRSDK（PushModule 已构造、画面源已 SetStreamCamera/SetStreamSource）。
        /// 走真 WHIP POST + WebRTC 协商；事件链路与正式 NotifyLive 完全一致。
        /// </summary>
        internal static void Debug_SimulateNotifyLive(string streamServerIp, bool start)
        {
            MVXRSDKLog.Info($"[Debug] 模拟 NotifyLive start={start} url={streamServerIp} → 走真链路");
            ApplyNotifyLive(streamServerIp, start);
        }
    }
}
