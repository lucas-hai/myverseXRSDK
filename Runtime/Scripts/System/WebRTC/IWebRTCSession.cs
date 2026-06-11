using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// WebRTC 会话抽象接口。真实实现 = `WebRTCSystem`；测试可注入 mock。
    ///
    /// 状态流：
    ///   Start(rt) → 内部 createOffer + munge + setLocalDesc + 等 ICE gathering
    ///             → 触发 OnLocalSdpReady(sdpOffer)
    ///   外部把 sdpOffer 通过 WhipClient.Post 发到 mediamtx，拿到 sdpAnswer
    ///   SetRemoteAnswer(sdpAnswer) → 内部 setRemoteDesc → 等连接建立
    ///                              → 触发 OnConnected 或 OnFailed
    ///   推流期间 OnDisconnected 表示异常断流（实现内部用，未对外订阅）
    ///   Dispose() 关闭 PC + 清资源；**一次性**，Dispose 后不可 reuse，需 new 一个新实例
    ///
    /// 音频源：实现内部通过 AudioMixingSystem.AttachToTrack 绑定，不再作为参数。
    /// 业务侧通过 AudioMixingSystem.PushGameAudio 提供游戏音 PCM（推流不含麦克风语音）。
    ///
    /// 所有 event 回调保证在 Unity 主线程触发（实现依赖 com.unity.webrtc 内部 SynchronizationContext），
    /// 订阅方无需考虑线程同步问题。
    /// </summary>
    internal interface IWebRTCSession : IDisposable
    {
        event Action<string> OnLocalSdpReady;     // sdp offer（已 munge，可直接 POST）
        event Action OnConnected;                  // PeerConnectionState=Connected
        event Action OnDisconnected;               // 推流期间网络中断（保留供未来扩展，当前 SDK 内未订阅）
        event Action<MVXRSDKErrorCode, string> OnFailed;   // 统一错误码（v2 PR-4）
        event Action<StreamStats> OnStatsUpdated; // 每 StreamConfig.StatsReportIntervalMs 一次

        void Start(RenderTexture videoSource);
        void SetRemoteAnswer(string sdpAnswer);
        // 注：无独立 Stop()——结束会话直接调 Dispose()（IDisposable），统一资源释放语义
    }
}
