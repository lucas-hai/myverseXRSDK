using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// WebRTC 会话 System 层实现，基于 com.unity.webrtc 3.0.0-pre.8。
    /// 流程：Start → createOffer → SDP munge（H.264 强制 + b=AS:3500）
    ///       → setLocalDescription → 等 ICE gathering complete → OnLocalSdpReady
    ///       外部 POST 拿 answer → SetRemoteAnswer → 等 Connected → OnConnected
    ///
    /// 线程模型：com.unity.webrtc 通过内部 s_syncContext（Unity 主线程 SynchronizationContext）
    /// 已经把 OnConnectionStateChange / OnIceConnectionChange / OnIceCandidate 等回调投递到主线程，
    /// 订阅方（PushStreamModule 等）拿到的所有 event 都在主线程，无需自行同步。
    ///
    /// 注：com.unity.webrtc 通过 Context.cs 的 [RuntimeInitializeOnLoadMethod(SubsystemRegistration)]
    ///     自动调用 InitializeInternal，无需 SDK 显式调用 WebRTC.Initialize。
    /// </summary>
    internal class WebRTCSystem : IWebRTCSession
    {
        public event Action<string> OnLocalSdpReady;
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<MVXRSDKErrorCode, string> OnFailed;
        public event Action<StreamStats> OnStatsUpdated;

        private RTCPeerConnection m_PC;
        private VideoStreamTrack m_VideoTrack;
        private AudioStreamTrack m_AudioTrack;
        // WebRTC.Update 协程：驱动 video track 的纹理上传，整个会话期间常驻
        private Coroutine m_WebRTCUpdateCo;
        // Stats 采集协程：连接建立后启动；每 StatsReportIntervalMs 调一次 GetStats()
        private Coroutine m_StatsCo;
        // 断流自愈协程：进入 Disconnected 时启动，DisconnectedSelfHealSec 秒内回 Connected 则取消，否则报 OnFailed
        private Coroutine m_SelfHealCo;
        // 握手期协程：NegotiateOffer 跑 CreateOffer + SDP munge + SetLocalDescription + 等 ICE gathering
        // SetRemoteCoroutine 跑 SetRemoteDescription。两者必须纳入 Dispose 清理，否则握手中 Dispose
        // 会把 m_PC 置 null，协程下次 yield 后解引用 NRE
        private Coroutine m_NegotiateCo;
        private Coroutine m_SetRemoteCo;
        private long m_LastBytesSent;
        private float m_LastStatsTime;
        private bool m_Disposed;

        // com.unity.webrtc 3.0.0-pre.8 H.264/VP8 编码器对 VideoStreamTrack 绑定 RT 的最小尺寸要求
        // （来自 native 异常 "Texture size is invalid. minWidth:145, maxWidth:4096 minHeight:49, maxHeight:4096"）
        // 低于此值时 new VideoStreamTrack(rt) 会抛，被 try/catch 包成 WebRTCInitFailed 而语义模糊。
        // 在 Start 入口预检 fail-fast，给业务方明确错误码定位到 RT 尺寸问题
        private const int kMinVideoWidth = 145;
        private const int kMinVideoHeight = 49;

        public void Start(RenderTexture videoSource)
        {
            if (videoSource == null)
            {
                OnFailed?.Invoke(MVXRSDKErrorCode.NoStreamSource, "videoSource is null");
                return;
            }

            // RT 尺寸预检：低于 webrtc 编码器最小值直接 fail-fast，不进入 native 调用
            // 典型踩坑：Editor Game View 窗口拖得过小（cam.pixelWidth/Height < 145×49）
            //         或 PICO XR 初始化异常 fallback 到非 XR 路径
            if (videoSource.width < kMinVideoWidth || videoSource.height < kMinVideoHeight)
            {
                OnFailed?.Invoke(MVXRSDKErrorCode.InvalidStreamSourceSize,
                    $"stream RT 尺寸 {videoSource.width}x{videoSource.height} 低于 webrtc 最小 {kMinVideoWidth}x{kMinVideoHeight}");
                return;
            }

            try
            {
                // 不配 STUN/TURN，依赖局域网/本机 host candidate（场景：仅局域网推流）
                var config = new RTCConfiguration { iceServers = Array.Empty<RTCIceServer>() };
                m_PC = new RTCPeerConnection(ref config);
            }
            catch (Exception ex)
            {
                OnFailed?.Invoke(MVXRSDKErrorCode.WebRTCInitFailed, $"RTCPeerConnection ctor failed: {ex.Message}");
                return;
            }

            // 注：com.unity.webrtc 用 delegate property 而非 C# event；回调发生在 native 内部线程
            m_PC.OnConnectionStateChange = OnConnectionStateChange;
            m_PC.OnIceConnectionChange = OnIceConnectionChange;
            m_PC.OnIceCandidate = OnIceCandidate;

            try
            {
                m_VideoTrack = new VideoStreamTrack(videoSource);
                m_PC.AddTrack(m_VideoTrack);

                m_AudioTrack = new AudioStreamTrack();
                m_PC.AddTrack(m_AudioTrack);
            }
            catch (Exception ex)
            {
                OnFailed?.Invoke(MVXRSDKErrorCode.WebRTCInitFailed, $"add track failed: {ex.Message}");
                return;
            }

            // 在 CreateOffer 之前应用 codec 偏好——videostreaming.html 官方推荐路径（A 级）。
            // 把 H.264 排在第一位让 mediamtx 协商时优先选；SDP munge ForceH264Only 仍保留作强制兜底
            ApplyVideoCodecPreference();

            // WebRTC.Update 协程必须每帧跑，否则 VideoStreamTrack 的纹理不会提交到 native 层
            m_WebRTCUpdateCo = MonoSystem.Start_Coroutine(WebRTC.Update());
            // 音频馈送：交给 AudioMixingSystem 接管 feeder GameObject 与音频线程驱动
            // 若业务侧未 push 任何 PCM，AudioMixingSystem 内部缓冲为空，track 持续静音
            AudioMixingSystem.AttachToTrack(m_AudioTrack);

            m_NegotiateCo = MonoSystem.Start_Coroutine(NegotiateOffer());
        }

        private IEnumerator NegotiateOffer()
        {
            // 每个 yield 之后必须重新检查 m_Disposed/m_PC，因为 Dispose 可能在握手过程中被调用——
            // 即使 StopCoroutines 已 stop 本协程，已经在执行中的 frame 仍可能继续到下一句
            if (m_Disposed || m_PC == null) yield break;
            var offerOp = m_PC.CreateOffer();
            yield return offerOp;
            if (m_Disposed || m_PC == null) yield break;
            if (offerOp.IsError)
            {
                OnFailed?.Invoke(MVXRSDKErrorCode.SdpNegotiationFailed, $"createOffer error: {offerOp.Error.message}");
                yield break;
            }

            // SDP munge：必须在 setLocalDescription 之前，否则本地协商已锁定 codec
            var desc = offerOp.Desc;
            var cfg = StreamConfig.Active;

            if (cfg.ForceH264)
            {
                // 预检：若 com.unity.webrtc 未编译进 H.264（部分 Android/Vulkan 构建），
                // 直接 ForceH264Only 会把 m=video payload 删空，mediamtx 必拒。提前报错
                if (!SdpMunger.ContainsH264(desc.sdp))
                {
                    OnFailed?.Invoke(MVXRSDKErrorCode.CodecNegotiationFailed,
                        "SDP offer 不含 H.264 payload，com.unity.webrtc 未启用 H.264 编码器");
                    yield break;
                }
                desc.sdp = SdpMunger.ForceH264Only(desc.sdp);
                // 二次校验：ForceH264Only 后 m=video 是否仍有有效 payload + rtpmap
                if (!SdpMunger.ValidateVideoPayload(desc.sdp))
                {
                    OnFailed?.Invoke(MVXRSDKErrorCode.CodecNegotiationFailed,
                        "SDP munge 后 m=video payload 校验失败");
                    yield break;
                }
            }
            desc.sdp = SdpMunger.SetBandwidth(desc.sdp, cfg.VideoBandwidthKbps);

            var setLocalOp = m_PC.SetLocalDescription(ref desc);
            yield return setLocalOp;
            if (m_Disposed || m_PC == null) yield break;
            if (setLocalOp.IsError)
            {
                OnFailed?.Invoke(MVXRSDKErrorCode.SdpNegotiationFailed, $"setLocalDescription error: {setLocalOp.Error.message}");
                yield break;
            }

            // 编码参数（min/max 码率 + maxFps）：SLD 后 sender encodings 才完整建立，此时调 SetParameters
            // 跳过 libwebrtc GCC 慢启动，避免接收端 ffmpeg 因首帧 PTS 步长不稳卡死
            ApplyVideoSenderParameters();

            // 等 ICE gathering complete（non-trickle，超时由 StreamConfig 控制）
            var startTime = Time.realtimeSinceStartup;
            int iceTimeoutSec = cfg.IceGatheringTimeoutSec;
            while (!m_Disposed && m_PC != null && m_PC.GatheringState != RTCIceGatheringState.Complete)
            {
                if (Time.realtimeSinceStartup - startTime > iceTimeoutSec)
                {
                    OnFailed?.Invoke(MVXRSDKErrorCode.IceGatheringTimeout, $"ICE gathering > {iceTimeoutSec}s");
                    yield break;
                }
                yield return null;
            }
            if (m_Disposed || m_PC == null) yield break;

            var finalSdp = m_PC.LocalDescription.sdp;
            m_NegotiateCo = null;  // 协程到达终态，清字段避免 StopCoroutines 重复 Stop 一个已结束协程
            OnLocalSdpReady?.Invoke(finalSdp);
        }

        public void SetRemoteAnswer(string sdpAnswer)
        {
            // Disposed 状态下不应再触发任何上报，避免虚假 OnFailed 反弹回上游 retry 逻辑
            if (m_Disposed) return;
            if (m_PC == null)
            {
                OnFailed?.Invoke(MVXRSDKErrorCode.SdpNegotiationFailed, "PC not initialized");
                return;
            }
            m_SetRemoteCo = MonoSystem.Start_Coroutine(SetRemoteCoroutine(sdpAnswer));
        }

        private IEnumerator SetRemoteCoroutine(string sdpAnswer)
        {
            if (m_Disposed || m_PC == null) yield break;
            var remoteDesc = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdpAnswer };
            var op = m_PC.SetRemoteDescription(ref remoteDesc);
            yield return op;
            // op 由 SetRemoteDescription 返回，本身不依赖 m_PC；但 OnFailed 不应在 Disposed 后触发
            if (m_Disposed) yield break;
            if (op.IsError)
            {
                OnFailed?.Invoke(MVXRSDKErrorCode.SdpNegotiationFailed, $"setRemoteDescription error: {op.Error.message}");
            }
            // 不主动等 connection state，OnConnectionStateChange 回调会触发 OnConnected
            m_SetRemoteCo = null;
        }

        // PeerConnection 回调均由 com.unity.webrtc 内部投递到主线程后再触发——
        // 证据链：RTCPeerConnection.cs:417-513 所有回调走 WebRTC.Sync(ptr, action)，
        //         WebRTC.cs:1153 Sync 调 s_syncContext.Post(...)，
        //         WebRTC.cs:744-745 s_syncContext 包装 Unity 主线程 SynchronizationContext，
        //         Internal/ExecutableUnitySynchronizationContext.cs:45+183-195 构造时挂入主线程 sync queue
        //         并以 ExecuteAndAppendNextExecute 自我递归 drain。
        // 所以这里不需要再做二次主线程投递，直接处理即可。
        // 用官方 SetCodecPreferences 路径把 H.264 排在 codec preference 首位。
        // 依据：videostreaming.html 明文示例 + RTCRtpSender.cs:84 GetCapabilities + RTCRtpTransceiver.cs:167 SetCodecPreferences。
        // 设置失败仅 Warning，不阻断流程——SDP munge ForceH264Only 仍保留作强制兜底（StreamConfig.ForceH264 控制）。
        // 注意：必须在 CreateOffer 之前调用，否则不生效。
        private void ApplyVideoCodecPreference()
        {
            if (!StreamConfig.Active.ForceH264) return;
            if (m_PC == null) return;

            try
            {
                var caps = RTCRtpSender.GetCapabilities(TrackKind.Video);
                if (caps.codecs == null || caps.codecs.Length == 0)
                {
                    MVXRSDKLog.Warning("WebRTCSystem: RTCRtpSender.GetCapabilities(Video) 返回空 codec 列表，跳过 SetCodecPreferences");
                    return;
                }

                // H.264 放第一，其他保留在后面（mediamtx 不支持时仍能 fallback；SDP munge 才是强制截断）
                var h264 = new List<RTCRtpCodecCapability>();
                var others = new List<RTCRtpCodecCapability>();
                foreach (var c in caps.codecs)
                {
                    if (c.mimeType != null && c.mimeType.Equals("video/H264", StringComparison.OrdinalIgnoreCase))
                        h264.Add(c);
                    else
                        others.Add(c);
                }
                if (h264.Count == 0)
                {
                    MVXRSDKLog.Warning("WebRTCSystem: 设备 codec 列表中没有 H.264，SetCodecPreferences 跳过（后续 SdpMunger.ContainsH264 预检会拦截）");
                    return;
                }
                h264.AddRange(others);
                var preferred = h264.ToArray();

                // 找 video transceiver。RTCPeerConnection.cs:339 GetTransceivers 返回 IEnumerable。
                // 当前 SDK 实现：AddTrack(video) 后 AddTrack(audio)，video transceiver 是第一个 video kind 的
                RTCRtpTransceiver videoTransceiver = null;
                foreach (var t in m_PC.GetTransceivers())
                {
                    if (t.Sender != null && t.Sender.Track == m_VideoTrack)
                    {
                        videoTransceiver = t;
                        break;
                    }
                }
                if (videoTransceiver == null)
                {
                    MVXRSDKLog.Warning("WebRTCSystem: 未找到 video transceiver，SetCodecPreferences 跳过");
                    return;
                }

                var err = videoTransceiver.SetCodecPreferences(preferred);
                if (err != RTCErrorType.None)
                {
                    MVXRSDKLog.Warning($"WebRTCSystem: SetCodecPreferences 返回 {err}（SDP munge 兜底仍生效）");
                }
                else
                {
                    // 每次 PeerConnection 重协商都会执行一次，运行期降到 Debug 减少噪音；失败仍是 Warning
                    MVXRSDKLog.Debug($"WebRTCSystem: SetCodecPreferences 成功，H.264 优先（共 {preferred.Length} 个 codec）");
                }
            }
            catch (Exception ex)
            {
                MVXRSDKLog.Warning($"WebRTCSystem: ApplyVideoCodecPreference 异常 {ex.Message}（SDP munge 兜底仍生效）");
            }
        }

        // 设置 video sender 的编码参数：min/max 码率 + 帧率上限。
        // 目的：跳过 libwebrtc GCC 慢启动期（默认初始 BWE ~100kbps）——PA9410 实测前 40s
        // 编码 fps 仅 2-30，jitter 飙到 140ms+，接收端 ffmpeg dup 帧卡死。
        // 设 minBitrate=1500kbps 后编码器从启动即按目标码率输出，PTS 步长稳定。
        //
        // 时机：必须在 SetLocalDescription 完成后调用——此时 transceiver 的 sender encodings
        // 才完整建立（AddTrack 创建 sender，但 default encoding 字段在 SLD 后才填充）。
        //
        // 失败不阻断协商：仅 Warning，让握手按默认参数继续；上线后通过 stats 验证是否生效。
        private void ApplyVideoSenderParameters()
        {
            if (m_PC == null || m_VideoTrack == null) return;
            var cfg = StreamConfig.Active;

            try
            {
                RTCRtpSender videoSender = null;
                foreach (var t in m_PC.GetTransceivers())
                {
                    if (t.Sender != null && t.Sender.Track == m_VideoTrack)
                    {
                        videoSender = t.Sender;
                        break;
                    }
                }
                if (videoSender == null)
                {
                    MVXRSDKLog.Warning("WebRTCSystem: 未找到 video sender，跳过 SetParameters");
                    return;
                }

                var p = videoSender.GetParameters();
                if (p.encodings == null || p.encodings.Length == 0)
                {
                    MVXRSDKLog.Warning("WebRTCSystem: video sender encodings 为空，跳过 SetParameters");
                    return;
                }

                // 单流推流场景只有 encodings[0]；simulcast 才会多条
                var enc = p.encodings[0];
                // RTCRtpEncodingParameters 的码率字段单位是 bps（不是 kbps）
                enc.maxBitrate   = (ulong)cfg.VideoBandwidthKbps * 1000UL;
                enc.minBitrate   = (ulong)cfg.VideoMinBitrateKbps * 1000UL;
                enc.maxFramerate = (uint)cfg.Fps;

                var err = videoSender.SetParameters(p);
                if (err.errorType != RTCErrorType.None)
                {
                    MVXRSDKLog.Warning($"WebRTCSystem: SetParameters 返回 {err.errorType}（{err.message}），编码参数回退默认");
                }
                else
                {
                    MVXRSDKLog.Info(
                        $"WebRTCSystem: SetParameters OK min={cfg.VideoMinBitrateKbps}kbps " +
                        $"max={cfg.VideoBandwidthKbps}kbps maxFps={cfg.Fps}");
                }
            }
            catch (Exception ex)
            {
                MVXRSDKLog.Warning($"WebRTCSystem: ApplyVideoSenderParameters 异常 {ex.Message}");
            }
        }

        private void OnConnectionStateChange(RTCPeerConnectionState state)
        {
            if (m_Disposed) return;
            MVXRSDKLog.Info($"WebRTCSystem: PeerConnectionState={state}");
            switch (state)
            {
                case RTCPeerConnectionState.Connected:
                    // 连接建立后启动 Stats 采集；幂等：已运行则不重启
                    if (m_StatsCo == null)
                    {
                        m_StatsCo = MonoSystem.Start_Coroutine(CollectStatsCoroutine());
                    }
                    CancelSelfHeal(success: true);  // 自愈窗口中收到 Connected → 取消超时
                    OnConnected?.Invoke();
                    break;
                case RTCPeerConnectionState.Disconnected:
                    // 短暂抖动：启动自愈窗口，期间不上报 OnFailed
                    if (m_SelfHealCo == null)
                    {
                        m_SelfHealCo = MonoSystem.Start_Coroutine(SelfHealCoroutine());
                    }
                    OnDisconnected?.Invoke();
                    break;
                case RTCPeerConnectionState.Failed:
                    CancelSelfHeal(success: false);
                    OnFailed?.Invoke(MVXRSDKErrorCode.IceConnectionFailed, "PeerConnectionState=Failed");
                    break;
            }
        }

        private void OnIceConnectionChange(RTCIceConnectionState state)
        {
            if (m_Disposed) return;
            // Failed/Disconnected 升 Warning 便于排查；其它转换保持 Debug 减少噪音
            if (state == RTCIceConnectionState.Failed || state == RTCIceConnectionState.Disconnected)
            {
                MVXRSDKLog.Warning($"WebRTCSystem: IceConnectionState={state}");
            }
            else
            {
                MVXRSDKLog.Debug($"WebRTCSystem: IceConnectionState={state}");
            }

            if (state == RTCIceConnectionState.Failed)
            {
                // 去重保护：PeerConnectionState=Disconnected 已启动自愈协程时，
                // ICE Failed 不再独立上报 OnFailed——交由自愈窗口超时统一处理，
                // 避免 StreamManager 收到双重失败 schedule 双重 retry
                if (m_SelfHealCo != null)
                {
                    MVXRSDKLog.Warning("WebRTCSystem: ICE Failed 但自愈窗口已启动，跳过重复 OnFailed 上报");
                    return;
                }
                OnFailed?.Invoke(MVXRSDKErrorCode.IceConnectionFailed, "IceConnectionState=Failed");
            }
        }

        private void OnIceCandidate(RTCIceCandidate candidate)
        {
            // 埋点：仅记日志便于排查 NAT 类型问题（host/srflx/relay）。
            // mediamtx WHIP 不支持 trickle，本实现走 non-trickle（等 gathering complete），candidate 仅记录不上报
            if (m_Disposed || candidate == null) return;
            MVXRSDKLog.Debug($"WebRTCSystem: ICE candidate {candidate.Candidate}");
        }

        // 断流自愈：等 DisconnectedSelfHealSec 秒；期间回 Connected 由 CancelSelfHeal 停掉本协程，
        // 不会执行到 OnFailed。否则上报 IceConnectionFailed，业务侧可订阅 OnPushStreamFailed 触发重启。
        //
        // 注：本实现不做 m_PC.RestartIce() 自动重协商——mediamtx 不支持 WHIP PATCH，
        // 完整 ICE Restart 等价于 DELETE 旧 session + 新 POST，涉及状态机大改，留 future work。
        // 业务侧短时间内收到 OnPushStreamFailed 后 Start 同一 URL 即可恢复推流。
        private IEnumerator SelfHealCoroutine()
        {
            int waitSec = StreamConfig.Active.DisconnectedSelfHealSec;
            if (waitSec <= 0) waitSec = 5;
            MVXRSDKLog.Warning($"WebRTCSystem: 进入断流自愈窗口 {waitSec}s");
            yield return new WaitForSecondsRealtime(waitSec);
            if (m_Disposed) yield break;
            m_SelfHealCo = null;  // 自身已结束，避免 CancelSelfHeal 再次 Stop_Coroutine
            MVXRSDKLog.Error($"WebRTCSystem: 自愈窗口 {waitSec}s 内未恢复，上报 IceConnectionFailed");
            OnFailed?.Invoke(MVXRSDKErrorCode.IceConnectionFailed, $"self-heal timeout {waitSec}s");
        }

        private void CancelSelfHeal(bool success)
        {
            if (m_SelfHealCo == null) return;
            MonoSystem.Stop_Coroutine(m_SelfHealCo);
            m_SelfHealCo = null;
            if (success) MVXRSDKLog.Info("WebRTCSystem: 断流已自愈，取消超时计时");
        }

        private IEnumerator CollectStatsCoroutine()
        {
            // 等首次间隔——刚 Connected 时 remote-inbound 还没回报，先等一拍再采
            while (!m_Disposed && m_PC != null)
            {
                int intervalMs = StreamConfig.Active.StatsReportIntervalMs;
                if (intervalMs < 100) intervalMs = 100;
                yield return new WaitForSecondsRealtime(intervalMs / 1000f);
                if (m_Disposed || m_PC == null) yield break;

                var op = m_PC.GetStats();
                yield return op;
                if (m_Disposed) yield break;

                if (op.IsError)
                {
                    MVXRSDKLog.Debug($"WebRTCSystem.GetStats error: {op.Error.message}");
                    continue;
                }

                var snapshot = BuildStreamStats(op.Value);
                try { OnStatsUpdated?.Invoke(snapshot); }
                catch (Exception ex) { MVXRSDKLog.Warning($"WebRTCSystem: stats subscriber threw {ex.Message}"); }
            }
        }

        private StreamStats BuildStreamStats(RTCStatsReport report)
        {
            var s = new StreamStats { Timestamp = Time.realtimeSinceStartup };
            if (report == null) return s;

            foreach (var kv in report.Stats)
            {
                var stat = kv.Value;
                if (stat is RTCOutboundRTPStreamStats outbound)
                {
                    // outbound-rtp 有 video 和 audio 两份，这里直接累加；业务通常关心总流量。
                    // com.unity.webrtc 这几个字段是 ulong/uint，必须显式 cast 到 StreamStats 的 long/int
                    s.BytesSent += (long)outbound.bytesSent;
                    s.PacketsSent += (long)outbound.packetsSent;
                    s.FramesEncoded += (int)outbound.framesEncoded;
                }
                else if (stat is RTCRemoteInboundRtpStreamStats remoteIn)
                {
                    s.PacketsLost += (long)remoteIn.packetsLost;
                    // jitter 单位是秒 → 毫秒
                    if (remoteIn.jitter > 0) s.JitterMs = remoteIn.jitter * 1000.0;
                    if (remoteIn.roundTripTime > 0) s.RttMs = remoteIn.roundTripTime * 1000.0;
                }
                else if (stat is RTCIceCandidatePairStats pair && pair.nominated)
                {
                    // 优先用 nominated 候选对的 RTT 与可用带宽
                    if (pair.availableOutgoingBitrate > 0)
                        s.AvailableOutgoingBitrateKbps = (int)(pair.availableOutgoingBitrate / 1000.0);
                    if (pair.currentRoundTripTime > 0)
                        s.RttMs = pair.currentRoundTripTime * 1000.0;
                }
            }

            // 估算瞬时视频码率：本次 BytesSent 与上次差值 / 时间差
            if (m_LastStatsTime > 0 && s.Timestamp > m_LastStatsTime)
            {
                long delta = s.BytesSent - m_LastBytesSent;
                if (delta > 0)
                {
                    float dt = s.Timestamp - m_LastStatsTime;
                    s.VideoBitrateKbps = (int)(delta * 8L / 1000L / dt);
                }
            }
            m_LastBytesSent = s.BytesSent;
            m_LastStatsTime = s.Timestamp;
            return s;
        }

        private void StopCoroutines()
        {
            if (m_WebRTCUpdateCo != null)
            {
                MonoSystem.Stop_Coroutine(m_WebRTCUpdateCo);
                m_WebRTCUpdateCo = null;
            }
            if (m_StatsCo != null)
            {
                MonoSystem.Stop_Coroutine(m_StatsCo);
                m_StatsCo = null;
            }
            if (m_SelfHealCo != null)
            {
                MonoSystem.Stop_Coroutine(m_SelfHealCo);
                m_SelfHealCo = null;
            }
            if (m_NegotiateCo != null)
            {
                MonoSystem.Stop_Coroutine(m_NegotiateCo);
                m_NegotiateCo = null;
            }
            if (m_SetRemoteCo != null)
            {
                MonoSystem.Stop_Coroutine(m_SetRemoteCo);
                m_SetRemoteCo = null;
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;

            // 顺序：feeder → 协程 → 显式 Close PC → tracks/PC Dispose
            // 避免音频线程访问已 dispose 的 track；Close 让 native 层先停止数据流，再 Dispose 句柄
            AudioMixingSystem.DetachFromTrack();
            StopCoroutines();
            try { m_PC?.Close(); }
            catch (Exception ex) { MVXRSDKLog.Warning($"WebRTCSystem.Dispose: pc.Close throw {ex.Message}"); }
            m_VideoTrack?.Dispose();
            m_AudioTrack?.Dispose();
            m_PC?.Dispose();
            m_VideoTrack = null;
            m_AudioTrack = null;
            m_PC = null;
        }
    }
}
