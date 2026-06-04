using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// Camera 画面源：让 Camera 直接渲染到推流 RT，零额外 GPU Blit。
    /// 推荐用法——业务侧专门挂一台"直播相机"，SDK 接管 targetTexture。
    ///
    /// 性能：Camera.targetTexture = RT 后，Camera 渲染管线直接输出到该 RT，
    /// 无 Graphics.Blit 中间步骤。72/90Hz 渲染下 GPU 开销 ≈ 0。
    ///
    /// 业务约定：目标相机平时可以 enabled=false（不上屏、不烧 GPU）；
    /// Attach 时 SDK 自动强制启用，Detach 时还原回 Attach 前的值——
    /// 这样切镜目标相机默认不参与游戏渲染，被选中时才独立渲染到推流 RT，
    /// 屏幕上始终只看到业务主相机（有 targetTexture 的相机不上屏，Unity 规则）。
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

        public int Width { get; }
        public int Height { get; }
        public string DisplayName => m_Camera != null ? $"Camera({m_Camera.name})" : "Camera(disposed)";

        public event Action OnAttached;
        public event Action OnDetached;

        public CameraStreamSource(Camera camera, int width, int height)
        {
            m_Camera = camera;
            Width = width;
            Height = height;
        }

        public void Attach(RenderTexture targetRT)
        {
            if (m_Attached)
            {
                MVXRSDKLog.Warning($"CameraStreamSource: 重复 Attach 被忽略 {DisplayName}");
                return;
            }
            if (m_Camera == null || targetRT == null)
            {
                MVXRSDKLog.Error("CameraStreamSource.Attach: camera 或 targetRT 为 null");
                return;
            }
            m_OriginalTarget = m_Camera.targetTexture;
            m_OriginalEnabled = m_Camera.enabled;
            m_AttachedTarget = targetRT;
            m_Camera.targetTexture = targetRT;
            // 强制启用：业务相机平时可以 disabled（不上屏、不烧 GPU），切镜时由 SDK 临时启用
            m_Camera.enabled = true;
            m_Attached = true;

            try { OnAttached?.Invoke(); }
            catch (Exception ex) { MVXRSDKLog.Error($"CameraStreamSource: OnAttached 回调异常 {ex}"); }
        }

        public void Detach()
        {
            if (!m_Attached) return;
            // 只在 targetTexture 仍是我们设置的那张 RT 时还原；
            // 业务侧若中途自己改了 targetTexture，不覆盖业务的赋值
            if (m_Camera != null && m_Camera.targetTexture == m_AttachedTarget)
            {
                m_Camera.targetTexture = m_OriginalTarget;
            }
            // 还原 enabled 到 Attach 前快照——默认 disabled 的相机切回后继续不渲染
            if (m_Camera != null)
            {
                m_Camera.enabled = m_OriginalEnabled;
            }
            m_OriginalTarget = null;
            m_AttachedTarget = null;
            m_Attached = false;

            try { OnDetached?.Invoke(); }
            catch (Exception ex) { MVXRSDKLog.Error($"CameraStreamSource: OnDetached 回调异常 {ex}"); }
        }

        public void Dispose()
        {
            Detach();
            m_Camera = null;
        }
    }
}
