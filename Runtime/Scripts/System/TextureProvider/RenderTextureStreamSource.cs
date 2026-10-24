using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// RenderTexture 画面源：业务侧自己渲染到任意格式 RT，SDK 每帧 Graphics.Blit 转换格式 +
    /// 写到推流 RT。
    ///
    /// 格式 / 尺寸不匹配由 Graphics.Blit 处理。外部纹理始终借用，Dispose不销毁它。
    /// 帧调度由所属Runtime驱动，不注册全局监听；不承诺LateUpdate在相机渲染之后。
    /// </summary>
    public sealed class RenderTextureStreamSource : IStreamSource
    {
        private RenderTexture m_External;
        private RenderTexture m_Target;
        private bool m_Attached;
        // 帧率来自所属Runtime的实时策略，不读全局配置。
        private double m_NextBlitTimeUnscaled;

        /// <summary>宽高 = 外部 RT 原始尺寸（仅信息展示）；推流尺寸由 InternalRT 固定决定，Blit 自动缩放。</summary>
        public int Width => m_External != null ? m_External.width : 0;
        public int Height => m_External != null ? m_External.height : 0;
        public string DisplayName => m_External != null
            ? $"RT({m_External.width}x{m_External.height})"
            : "RT(disposed)";

        public bool IsAvailable => m_External != null && m_External.dimension == UnityEngine.Rendering.TextureDimension.Tex2D;

        internal SdkLogger Log { private get; set; }

        public event Action OnAttached;
        public event Action OnDetached;

        /// <summary>
        /// v3：不再传目标尺寸——InternalRT 固定尺寸，BlitTick 的 Graphics.Blit 自动做
        /// 尺寸/格式转换（非 16:9 外部 RT 会拉伸）。
        /// </summary>
        public RenderTextureStreamSource(RenderTexture externalRT)
        {
            m_External = externalRT;
        }

        public void Attach(RenderTexture targetRT)
        {
            if (m_Attached)
            {
                if (m_Target == targetRT) return;
                throw new InvalidOperationException("画面源已绑定另一个输出，必须先解绑");
            }
            if (!IsAvailable || targetRT == null || targetRT == m_External)
                throw new ArgumentException("必须提供不同的普通2D源纹理和目标纹理，禁止自Blit或采集纹理数组");
            m_Target = targetRT;
            m_NextBlitTimeUnscaled = 0;
            m_Attached = true;

            try { OnAttached?.Invoke(); }
            catch (Exception ex) { LogCallbackException(ex); }
        }

        public void Detach()
        {
            if (!m_Attached) return;
            m_Target = null;
            m_Attached = false;

            try { OnDetached?.Invoke(); }
            catch (Exception ex) { LogCallbackException(ex); }
        }

        /// <summary>由所属客户端按非缩放时钟驱动；相机渲染与本调用的先后由宿主渲染循环决定。</summary>
        public void Tick(double now, int framesPerSecond)
        {
            if (!IsAvailable || m_Target == null) return;
            if (framesPerSecond > 0)
            {
                if (now < m_NextBlitTimeUnscaled) return;
                m_NextBlitTimeUnscaled = now + 1.0 / framesPerSecond;
            }
            Graphics.Blit(m_External, m_Target);
        }

        private void LogCallbackException(Exception error)
        {
            if (Log != null) Log.Exception("Streaming", error);
            else MVXRSDKLog.Error(LogSanitizer.Clean(error.ToString()));
        }

        public void Dispose()
        {
            Detach();
            m_External = null;
        }
    }
}
