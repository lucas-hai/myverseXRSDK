using UnityEngine;

namespace MyVerseXRSDK.Streaming
{
    /// <summary>
    /// 显式绑定客户端的音频/配置装配组件，不创建SDK实例、不承载画面推流，也不采麦克风。
    /// 业务创建客户端后调用Bind；画面使用client.Director或client.Streaming的域接口。
    /// </summary>
    [AddComponentMenu("MyVerse XR SDK/Stream Rig")]
    [DisallowMultipleComponent]
    public sealed class MVXRStreamRig : MonoBehaviour
    {
        [Header("音频源")]
        [Tooltip("游戏音 AudioListener：通过 OnAudioFilterRead 抓 master mix。留空则不推游戏音。")]
        public AudioListener gameAudioListener;

        [Header("推流配置 Asset")]
        [Tooltip("完整媒体配置。Bind(client)时应用；再次启用Rig不会覆盖客户端运行时配置。非默认音频缓冲容量须在创建客户端时通过ToMediaOptions配置。")]
        public StreamConfigAsset streamConfigAsset;

        private SdkClient client;
        private AudioCaptureHandle m_GameAudio;

        /// <summary>将本Rig绑定到明确客户端并应用完整配置；重复绑定同实例只刷新音频绑定，不覆盖运行时配置。</summary>
        public SdkResult Bind(SdkClient owner)
        {
            if (owner == null || owner.State == SdkClientState.Stopping || owner.State == SdkClientState.Disposed || owner.State == SdkClientState.Faulted)
                return SdkResult.Fail(SdkErrorCode.InvalidState, "Rig需要存活客户端");
            if (!ReferenceEquals(client, owner) && streamConfigAsset != null)
            {
                var configured = streamConfigAsset.ApplyTo(owner.Streaming);
                if (!configured.Success) return configured;
            }
            if (!ReferenceEquals(client, owner)) { m_GameAudio?.Dispose(); m_GameAudio = null; }
            client = owner;
            return RefreshCapture();
        }

        private void OnEnable()
        {
            if (client == null || client.State == SdkClientState.Stopping || client.State == SdkClientState.Disposed) return;
            var result = RefreshCapture();
            if (!result.Success) client.Log.Write(SdkLogLevel.Error, "Streaming", "Rig音频绑定失败：" + result.Message, result.Code);
        }

        private SdkResult RefreshCapture()
        {
            if (!isActiveAndEnabled || gameAudioListener == null)
            {
                m_GameAudio?.Dispose(); m_GameAudio = null;
                return SdkResult.Ok;
            }
            try
            {
                m_GameAudio = client.Streaming.CaptureGameAudio(gameAudioListener);
                return SdkResult.Ok;
            }
            catch (System.Exception error) { return SdkResult.Fail(SdkErrorCode.InvalidState, error.Message); }
        }

        private void OnDisable()
        {
            m_GameAudio?.Dispose();
            m_GameAudio = null;
        }
    }
}
