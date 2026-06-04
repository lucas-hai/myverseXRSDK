using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace MyVerseXRSDK.Streaming
{
    /// <summary>
    /// URP 单相机零开销画面捕获：通过 CameraCaptureBridge 在主相机渲染管线尾部
    /// 插一次 cmd.Blit，把当前帧 Blit 到推流 RT。
    ///
    /// 设计要点：
    /// - 不创建辅助相机，不修改主相机的 targetTexture / clearFlags / cullingMask
    /// - 用 RenderPipelineManager.beginCameraRendering 作为"主相机首次准备渲染"的稳定信号；
    ///   XR 下此时眼贴图尺寸已就绪（Awake/Start 阶段 mainCamera.pixelWidth/Height 可能为 0）
    /// - 优先用 XRSettings.eyeTextureDesc 拷贝完整描述符（wh + format + sRGB + MSAA 全匹配 XR 单眼），
    ///   非 XR 模式回退到 mainCamera.pixelWidth/Height
    /// - 仅 URP（依赖 CameraCaptureBridge 在 UniversalRenderer 里的 CapturePass 注入）
    ///
    /// 使用：
    /// <code>
    /// _cap = new CameraStreamCapture(Camera.main);
    /// _cap.Ready += rt => MVXRSDK.SetStreamSource(rt);
    /// // ...
    /// _cap.Dispose();
    /// </code>
    /// </summary>
    public sealed class CameraStreamCapture : IDisposable
    {
        /// <summary>推流 RT；Ready 触发后才非空。</summary>
        public RenderTexture Texture { get; private set; }

        /// <summary>RT 是否已根据首帧渲染尺寸建好。</summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// 暂停画面 Blit。true 时 OnCameraCapture 直接 return，不消耗 GPU。
        /// 典型用法：SDK 端 source 被 Detach（Director 切走）时置 true，Attach 时置 false。
        /// </summary>
        public bool Paused { get; set; }

        /// <summary>RT 建好后触发；订阅时若已 ready 会立即回调一次。</summary>
        public event Action<RenderTexture> Ready
        {
            add
            {
                m_Ready += value;
                if (IsReady && Texture != null) value?.Invoke(Texture);
            }
            remove { m_Ready -= value; }
        }
        private event Action<RenderTexture> m_Ready;

        private Camera m_MainCamera;
        private Action<RenderTargetIdentifier, CommandBuffer> m_CaptureAction;
        private bool m_Disposed;
        // 帧率节流：m_CaptureInterval 秒到点才 Blit；0 = 不节流（按主循环 fps 全量推）
        private readonly double m_CaptureInterval;
        private double m_NextCaptureTimeUnscaled;

        /// <summary>
        /// </summary>
        /// <param name="mainCamera">业务主相机。</param>
        /// <param name="targetFps">推流帧率上限；&lt;=0 表示不节流跟主循环走。</param>
        public CameraStreamCapture(Camera mainCamera, int targetFps)
        {
            if (mainCamera == null) throw new ArgumentNullException(nameof(mainCamera));
            m_MainCamera = mainCamera;
            m_CaptureInterval = targetFps > 0 ? 1.0 / targetFps : 0.0;

            // 先 hook capture：Bridge 回调在 RT 建好前会被守卫 return，不会出错
            CameraCaptureBridge.enabled = true;
            m_CaptureAction = OnCameraCapture;
            CameraCaptureBridge.AddCaptureAction(m_MainCamera, m_CaptureAction);

            // 监听首次 beginCameraRendering 拿稳定尺寸
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        /// <summary>切换捕获的目标相机（DirectorModule 切镜等场景）。</summary>
        public void SwitchCamera(Camera newCamera)
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(CameraStreamCapture));
            if (newCamera == null) throw new ArgumentNullException(nameof(newCamera));
            if (newCamera == m_MainCamera) return;

            if (m_MainCamera != null && m_CaptureAction != null)
                CameraCaptureBridge.RemoveCaptureAction(m_MainCamera, m_CaptureAction);

            m_MainCamera = newCamera;
            CameraCaptureBridge.AddCaptureAction(m_MainCamera, m_CaptureAction);
            // Texture 复用：尺寸/格式应当一致（DirectorModule 内部已保证）；
            // 如果未来要支持异格相机，这里需要 Texture 重建
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

            if (m_MainCamera != null && m_CaptureAction != null)
                CameraCaptureBridge.RemoveCaptureAction(m_MainCamera, m_CaptureAction);
            m_CaptureAction = null;

            if (Texture != null)
            {
                Texture.Release();
                UnityEngine.Object.Destroy(Texture);
                Texture = null;
            }
            IsReady = false;
            m_Ready = null;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera cam)
        {
            if (IsReady || cam != m_MainCamera) return;

            // 优先用 XR 眼贴图描述符（像素级匹配 XR 输出），非 XR 退化到 camera pixel 尺寸
            if (XRSettings.isDeviceActive && XRSettings.eyeTextureWidth > 0)
            {
                Texture = CreateRT_FromXRDesc();
                MVXRSDKLog.Info($"CameraStreamCapture: XR eyeTextureDesc → RT {Texture.width}x{Texture.height}（缩放由上层 Graphics.Blit 完成）");
            }
            else
            {
                Texture = CreateRT_FromSize(cam.pixelWidth, cam.pixelHeight);
                MVXRSDKLog.Info($"CameraStreamCapture: 非 XR → RT {Texture.width}x{Texture.height}（缩放由上层 Graphics.Blit 完成）");
            }
            IsReady = true;

            // 一次性回调：之后不再需要监听
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

            m_Ready?.Invoke(Texture);
        }

        private void OnCameraCapture(RenderTargetIdentifier source, CommandBuffer cmd)
        {
            // RT 尚未建好或被外部暂停时跳过；省一次 GPU Blit（Director 切走期间约 0.2-0.5ms）
            if (!IsReady || Texture == null || Paused) return;

            // 帧率节流：未到下次时间直接返回，省 GPU + 降编码器输入频率
            // PICO 主循环 72/90Hz，targetFps=30 时大约每 2-3 帧 Blit 一次
            if (m_CaptureInterval > 0)
            {
                double now = Time.unscaledTimeAsDouble;
                if (now < m_NextCaptureTimeUnscaled) return;
                m_NextCaptureTimeUnscaled = now + m_CaptureInterval;
            }

            cmd.Blit(source, new RenderTargetIdentifier(Texture));
        }

        /// <summary>
        /// 按长边上限同比例缩放原始 wh，保持画面不形变。H.264 要求宽高都是偶数，统一向下偶数对齐。
        /// 长边 &lt;= maxLongSide 或 maxLongSide &lt;=0 时直接返回原尺寸（仅做偶数对齐）。
        /// 推流尺寸缩放在 RenderTextureStreamSource 层完成；Rig 用本 helper 算出
        /// "推流目标尺寸" 传给 source 构造，使 InternalRT 按缩后尺寸建。
        /// </summary>
        public static (int w, int h) ComputeStreamSize(int srcW, int srcH, int maxLongSide)
        {
            int w = srcW, h = srcH;
            if (maxLongSide > 0)
            {
                int longSide = Mathf.Max(srcW, srcH);
                if (longSide > maxLongSide)
                {
                    float scale = (float)maxLongSide / longSide;
                    w = Mathf.RoundToInt(srcW * scale);
                    h = Mathf.RoundToInt(srcH * scale);
                }
            }
            // 偶数对齐 + 下限保护
            w &= ~1;
            h &= ~1;
            if (w < 2) w = 2;
            if (h < 2) h = 2;
            return (w, h);
        }

        private static RenderTexture CreateRT_FromXRDesc()
        {
            // 拷贝 XR 眼贴图完整描述符，但收紧为推流用：单 slice / 无 MSAA / 无 mipmap
            // 不改 width/height——保持与 XR 源同尺寸是 cmd.Blit Tex2DArray→Tex2D 稳定的前提
            var desc = XRSettings.eyeTextureDesc;
            desc.dimension = TextureDimension.Tex2D;
            desc.volumeDepth = 1;
            desc.vrUsage = VRTextureUsage.None;
            desc.msaaSamples = 1;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            var rt = new RenderTexture(desc)
            {
                name = "MVXRStreamRT",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            rt.Create();
            return rt;
        }

        private static RenderTexture CreateRT_FromSize(int w, int h)
        {
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "MVXRStreamRT",
                antiAliasing = 1,
                useMipMap = false,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            rt.Create();
            return rt;
        }
    }
}
