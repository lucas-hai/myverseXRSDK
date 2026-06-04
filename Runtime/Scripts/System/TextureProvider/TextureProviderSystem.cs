using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流画面 System：管理一张与 <see cref="VideoStreamTrack"/> 绑定的"长生命周期"RT，
    /// 通过 <see cref="SwitchSource"/> 切换不同 <see cref="IStreamSource"/>（Camera / RT / 业务自定义）。
    ///
    /// 关键性质：**同尺寸切源不断流**。VideoStreamTrack 一旦绑定 RT 引用就不再改变，
    /// 切源时只换 Blit 来源 / 切换 Camera.targetTexture，编码器对此无感。
    ///
    /// 尺寸约束：首次 SwitchSource 时按 source.Width/Height 创建 RT；后续切源若尺寸不一致，
    /// 返回 false 拒绝切换（业务需先 ClearSource 释放 RT 再切到不同尺寸）。
    /// 这一约束源于 com.unity.webrtc 3.0 不支持 VideoStreamTrack 热切纹理。
    /// </summary>
    internal static class TextureProviderSystem
    {
        private static RenderTexture s_InternalRT;
        private static IStreamSource s_Current;
        private static bool s_Initialized;

        /// <summary>当前的内部 RT（与 VideoStreamTrack 绑定）。无源时为 null。</summary>
        public static RenderTexture InternalRT => s_InternalRT;

        /// <summary>当前生效的画面源（调试 / 状态查询用）。</summary>
        public static IStreamSource CurrentSource => s_Current;

        public static bool HasSource => s_Current != null && s_InternalRT != null;

        public static void Init()
        {
            if (s_Initialized)
            {
                MVXRSDKLog.Warning("TextureProviderSystem: 已初始化，跳过重复 Init");
                return;
            }
            s_Initialized = true;
            MVXRSDKLog.Info("TextureProviderSystem: 初始化完成");
        }

        public static void Dispose()
        {
            if (!s_Initialized) return;
            ClearSource();
            s_Initialized = false;
            MVXRSDKLog.Info("TextureProviderSystem: 已反初始化");
        }

        /// <summary>
        /// 切换画面源到 <paramref name="newSource"/>。
        /// <list type="bullet">
        /// <item>首次调用：按 source 的 Width/Height 创建内部 RT，并 Attach。</item>
        /// <item>新旧尺寸一致：Detach 旧 + Attach 新到同一 RT。**编码器无感、不断流**。</item>
        /// <item>新旧尺寸不一致：拒绝（返回 false + Warning）。业务需先 ClearSource 再切。</item>
        /// <item>newSource == null：等价于 ClearSource。</item>
        /// </list>
        /// 旧 source 仅 Detach 不 Dispose——若业务想立即释放需自己持有引用并显式 Dispose。
        /// **拒绝时（返回 false）的语义**：旧 source 保持 attached 不断流；newSource 不会被持有，
        /// 由本方法主动 Dispose 防止资源泄漏（业务侧通常 new 出来直接传入，没保存引用）。
        /// </summary>
        public static bool SwitchSource(IStreamSource newSource)
        {
            if (!s_Initialized)
            {
                MVXRSDKLog.Error("TextureProviderSystem.SwitchSource: 未初始化");
                TryDisposeRejected(newSource);
                return false;
            }

            if (newSource == null)
            {
                ClearSource();
                return true;
            }

            if (newSource.Width <= 0 || newSource.Height <= 0)
            {
                MVXRSDKLog.Error($"TextureProviderSystem.SwitchSource: 非法尺寸 {newSource.Width}x{newSource.Height}");
                TryDisposeRejected(newSource);
                return false;
            }

            // 首次：创建内部 RT
            if (s_InternalRT == null)
            {
                CreateInternalRT(newSource.Width, newSource.Height);
                if (s_InternalRT == null)
                {
                    TryDisposeRejected(newSource);
                    return false;  // 创建失败已在 CreateInternalRT 内记日志
                }
            }
            else if (s_InternalRT.width != newSource.Width || s_InternalRT.height != newSource.Height)
            {
                MVXRSDKLog.Warning(
                    $"TextureProviderSystem.SwitchSource: 拒绝切换，尺寸不一致 " +
                    $"当前 {s_InternalRT.width}x{s_InternalRT.height} → 新 {newSource.Width}x{newSource.Height}。" +
                    $"请先调用 ClearSource() 再切。旧 source 保持 attached 不断流。");
                TryDisposeRejected(newSource);
                return false;
            }

            // Detach 旧、Attach 新——同一张 RT，编码器无感
            s_Current?.Detach();
            s_Current = newSource;
            s_Current.Attach(s_InternalRT);
            MVXRSDKLog.Info($"TextureProviderSystem: 切源成功 → {s_Current.DisplayName}");
            return true;
        }

        /// <summary>统一释放被拒绝的 newSource，避免业务方"new + 传参 + 没保存引用"导致泄漏。</summary>
        private static void TryDisposeRejected(IStreamSource rejected)
        {
            if (rejected == null) return;
            try { rejected.Dispose(); }
            catch (System.Exception ex) { MVXRSDKLog.Warning($"TextureProviderSystem: 释放被拒 source 异常 {ex.Message}"); }
        }

        /// <summary>清空当前 source 并释放内部 RT。推流期间调用会导致 VideoStreamTrack 失去画面源。</summary>
        public static void ClearSource()
        {
            if (s_Current != null)
            {
                try { s_Current.Detach(); } catch (System.Exception ex) { MVXRSDKLog.Warning($"TextureProviderSystem: Detach 异常 {ex.Message}"); }
                try { s_Current.Dispose(); } catch (System.Exception ex) { MVXRSDKLog.Warning($"TextureProviderSystem: Dispose 异常 {ex.Message}"); }
                s_Current = null;
            }
            ReleaseInternalRT();
        }

        private static void CreateInternalRT(int width, int height)
        {
            // 严格对齐 com.unity.webrtc 包源码 CameraExtension.cs:44-68 的 CaptureStreamTrack 内部实现（A 级证据）：
            //   format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType)  ← 必须用 RenderTextureFormat
            //   rt = new RenderTexture(width, height, depthValue, format)                       ← 4 参构造
            //   depth 默认 RenderTextureDepth.Depth24（int 值 24）                              ← 必须有深度缓冲
            //   rt.Create()                                                                     ← 必须显式 Create
            // C 级实测（2026-05-15, Editor Win D3D11 + mediamtx 内网）：
            //   错：GraphicsFormat + RenderTextureDescriptor(w,h,format,0) → 协议层全绿但播放端黑屏
            //   对：上面对齐 CaptureStreamTrack 的参数 → 正常推流
            // 备注：videostreaming.html 文档示例写 depth=0 与同包源码默认 depth=24 矛盾，以源码 + 实测为准。
            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            var rt = new RenderTexture(width, height, 24, format, RenderTextureReadWrite.Default)
            {
                name = "MVXRSDK_InternalStreamRT"
            };
            if (!rt.Create())
            {
                MVXRSDKLog.Error($"TextureProviderSystem: RenderTexture.Create 失败 {width}x{height} format={format}");
                rt.Release();
                Object.Destroy(rt);
                return;
            }
            s_InternalRT = rt;
            MVXRSDKLog.Info($"TextureProviderSystem: 创建内部 RT {width}x{height} format={format} depth=24");
        }

        private static void ReleaseInternalRT()
        {
            if (s_InternalRT == null) return;
            s_InternalRT.Release();
            if (Application.isPlaying)
                Object.Destroy(s_InternalRT);
            else
                Object.DestroyImmediate(s_InternalRT);
            s_InternalRT = null;
        }
    }
}
