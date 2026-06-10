using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// RenderTexture 画面源：业务侧自己渲染到任意格式 RT，SDK 每帧 Graphics.Blit 转换格式 +
    /// 写到推流 RT。
    ///
    /// 性能：每帧 1 次 GPU Blit，PICO 4U 上约 0.2-0.5ms GPU。
    /// 格式 / 尺寸不匹配由 Graphics.Blit 隐式处理（双线性缩放、格式转换）。
    ///
    /// LateUpdate 时机：保证业务本帧的所有渲染（含 OnPostRender）已完成，画面是稳定终态。
    /// </summary>
    public sealed class RenderTextureStreamSource : IStreamSource
    {
        private RenderTexture m_External;
        private RenderTexture m_Target;
        private bool m_Attached;
        // 帧率节流时间戳；运行时每帧从 StreamConfig.Active.Fps 读上限，业务改 fps 立即生效
        private double m_NextBlitTimeUnscaled;

        /// <summary>宽高 = 外部 RT 原始尺寸（仅信息展示）；推流尺寸由 InternalRT 固定决定，Blit 自动缩放。</summary>
        public int Width => m_External != null ? m_External.width : 0;
        public int Height => m_External != null ? m_External.height : 0;
        public string DisplayName => m_External != null
            ? $"RT({m_External.width}x{m_External.height})"
            : "RT(disposed)";

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
                MVXRSDKLog.Warning($"RenderTextureStreamSource: 重复 Attach 被忽略 {DisplayName}");
                return;
            }
            if (m_External == null || targetRT == null)
            {
                MVXRSDKLog.Error("RenderTextureStreamSource.Attach: externalRT 或 targetRT 为 null");
                return;
            }
            m_Target = targetRT;
            MonoSystem.AddLateUpdateListener(BlitTick);
            m_Attached = true;

            try { OnAttached?.Invoke(); }
            catch (Exception ex) { MVXRSDKLog.Error($"RenderTextureStreamSource: OnAttached 回调异常 {ex}"); }
        }

        public void Detach()
        {
            if (!m_Attached) return;
            MonoSystem.RemoveLateUpdateListener(BlitTick);
            m_Target = null;
            m_Attached = false;

            try { OnDetached?.Invoke(); }
            catch (Exception ex) { MVXRSDKLog.Error($"RenderTextureStreamSource: OnDetached 回调异常 {ex}"); }
        }

        private void BlitTick()
        {
            // 业务侧可能在推流期间销毁了 RT；保护性检查避免 NullRef
            if (m_External == null || m_Target == null) return;

            // 帧率节流：按 StreamConfig.Active.Fps 限速。PICO 主循环 90Hz、推流 30fps 时
            // 每 3 帧 Blit 一次，减少 GPU + 编码器负担。fps<=0 表示不节流。
            int fps = StreamConfig.Active.Fps;
            if (fps > 0)
            {
                double now = Time.unscaledTimeAsDouble;
                if (now < m_NextBlitTimeUnscaled) return;
                m_NextBlitTimeUnscaled = now + 1.0 / fps;
            }

            Graphics.Blit(m_External, m_Target);
        }

        public void Dispose()
        {
            Detach();
            m_External = null;
        }
    }
}
