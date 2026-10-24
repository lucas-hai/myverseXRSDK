using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// Camera 画面源：让 Camera 直接渲染到推流 RT，零额外 GPU Blit。
    /// 推荐用法——业务侧专门挂一台"直播相机"，SDK 接管 targetTexture。
    ///
    /// Camera直接渲染到目标RT，不增加中间Blit；相机渲染本身仍消耗GPU。
    ///
    /// 业务约定：直播相机平时可以 enabled=false（不上屏、不烧 GPU）；
    /// Attach 时 SDK 自动强制启用，Detach 时还原回 Attach 前的值——
    /// 这样直播相机默认不参与游戏渲染，被中控选中（NotifyLive）接源后才渲染到推流 RT，
    /// 屏幕上始终只看到玩家视角相机（有 targetTexture 的相机不上屏，Unity 规则）。
    ///
    /// Detach 行为：还原 cam.targetTexture 和 cam.enabled 到 Attach 前的值。
    /// </summary>
    public sealed class CameraStreamSource : IStreamSource
    {
        private Camera m_Camera;
        private RenderTexture m_OriginalTarget;
        private RenderTexture m_AttachedTarget;
        private bool m_OriginalEnabled;
        private bool m_Attached;

        /// <summary>宽高 = attach 后的推流 RT 尺寸（仅信息展示）；未 attach 时为 0。</summary>
        public int Width => m_AttachedTarget != null ? m_AttachedTarget.width : 0;
        public int Height => m_AttachedTarget != null ? m_AttachedTarget.height : 0;
        public string DisplayName => m_Camera != null ? $"Camera({m_Camera.name})" : "Camera(disposed)";
        public bool IsAvailable => m_Camera != null && (!m_Attached || m_Camera.targetTexture == m_AttachedTarget);
        public void Tick(double now, int framesPerSecond) { }

        internal SdkLogger Log { private get; set; }

        public event Action OnAttached;
        public event Action OnDetached;

        /// <summary>
        /// v3：不再传宽高——InternalRT 固定尺寸（StreamConfig.StreamMaxLongSide 按 16:9），
        /// 相机 attach 后按 RT 尺寸渲染，比例永远正确。
        /// </summary>
        public CameraStreamSource(Camera camera)
        {
            m_Camera = camera;
        }

        public void Attach(RenderTexture targetRT)
        {
            if (m_Attached)
            {
                if (m_AttachedTarget == targetRT) return;
                throw new InvalidOperationException("画面源已绑定另一个输出，必须先解绑");
            }
            if (m_Camera == null || targetRT == null)
                throw new ArgumentException("相机与目标纹理必须有效");
            m_OriginalTarget = m_Camera.targetTexture;
            m_OriginalEnabled = m_Camera.enabled;
            m_AttachedTarget = targetRT;
            m_Camera.targetTexture = targetRT;
            // 强制启用：业务相机平时可以 disabled（不上屏、不烧 GPU），切镜时由 SDK 临时启用
            m_Camera.enabled = true;
            m_Attached = true;

            try { OnAttached?.Invoke(); }
            catch (Exception ex) { LogCallbackException(ex); }
        }

        public void Detach()
        {
            if (!m_Attached) return;
            // 业务重设targetTexture视为收回控制权；此时也不能覆盖其enabled设置。
            if (m_Camera != null && m_Camera.targetTexture == m_AttachedTarget)
            {
                m_Camera.targetTexture = m_OriginalTarget;
                m_Camera.enabled = m_OriginalEnabled;
            }
            m_OriginalTarget = null;
            m_AttachedTarget = null;
            m_Attached = false;

            try { OnDetached?.Invoke(); }
            catch (Exception ex) { LogCallbackException(ex); }
        }

        private void LogCallbackException(Exception error)
        {
            if (Log != null) Log.Exception("Streaming", error);
            else MVXRSDKLog.Error(LogSanitizer.Clean(error.ToString()));
        }

        public void Dispose()
        {
            Detach();
            m_Camera = null;
        }
    }
}
