using System;
using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流画面 System：管理一张与 <see cref="VideoStreamTrack"/> 绑定的"长生命周期"RT，
    /// 通过 <see cref="SwitchSource"/> 切换不同 <see cref="IStreamSource"/>。
    ///
    /// v3（切镜化重构）语义：
    /// - InternalRT <b>固定尺寸</b>：StreamConfig.StreamMaxLongSide 按 16:9（默认 1280x720），
    ///   不再由首个画面源决定 → 任意源可热切，原"同尺寸"约束消失。
    /// - RT 生命周期 = Init→Dispose：ClearSource 只清黑不释放（推流会话可能仍绑定该 RT）。
    /// - 一相机推流保护（设计 §4.2）：会话活跃 + 已有源时 SwitchSource 丢弃新源；
    ///   活跃 + 无源（黑帧等待）或空闲时正常接源。
    /// </summary>
    internal static class TextureProviderSystem
    {
        private static RenderTexture s_InternalRT;
        private static IStreamSource s_Current;
        private static bool s_Initialized;
        // 推流会话是否活跃（Starting/Started）；由 StreamManager 注入，默认视为不活跃
        private static Func<bool> s_SessionActive = () => false;

        /// <summary>内部 RT（VideoStreamTrack 绑定）。未创建时为 null，EnsureInternalRT 创建。</summary>
        public static RenderTexture InternalRT => s_InternalRT;

        /// <summary>当前生效的画面源（调试 / 状态查询用）。</summary>
        public static IStreamSource CurrentSource => s_Current;

        public static bool HasSource => s_Current != null;

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

        /// <summary>注入"推流会话活跃"判定（一相机推流保护用）。传 null 恢复默认不活跃。</summary>
        public static void SetSessionActivePredicate(Func<bool> predicate)
        {
            s_SessionActive = predicate ?? (() => false);
        }

        public static void Dispose()
        {
            if (!s_Initialized) return;
            ClearSource();
            ReleaseInternalRT();
            s_SessionActive = () => false;
            s_Initialized = false;
            MVXRSDKLog.Info("TextureProviderSystem: 已反初始化");
        }

        /// <summary>
        /// 确保 InternalRT 已按 StreamConfig 创建（无源启动推黑帧的前置）。
        /// PushStreamModule 的 getSource 依赖本方法在会话启动时拿到非空 RT。
        /// </summary>
        public static RenderTexture EnsureInternalRT()
        {
            if (!s_Initialized)
            {
                MVXRSDKLog.Error("TextureProviderSystem.EnsureInternalRT: 未初始化");
                return null;
            }
            if (s_InternalRT == null) CreateInternalRT();
            return s_InternalRT;
        }

        /// <summary>
        /// 切换画面源。规则（设计 §4.2 一相机推流保护）：
        /// <list type="bullet">
        /// <item>会话活跃 + 已有源 → 丢弃（Warning），不排队不抢占，保护观众画面。</item>
        /// <item>会话活跃 + 无源 → 接上（被选中后的标准接源路径）。</item>
        /// <item>会话空闲 → 正常替换（换源自由）。</item>
        /// <item>newSource == null → 等价 ClearSource。</item>
        /// </list>
        /// 被丢弃的 newSource 由本方法 Dispose 防泄漏；切换时旧源仅 Detach 不 Dispose。
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

            if (s_SessionActive() && s_Current != null)
            {
                MVXRSDKLog.Warning(
                    $"TextureProviderSystem.SwitchSource: 推流进行中已有画面源 {s_Current.DisplayName}，" +
                    $"丢弃新源 {newSource.DisplayName}（一相机推流保护，不排队不抢占）");
                TryDisposeRejected(newSource);
                return false;
            }

            if (EnsureInternalRT() == null)
            {
                TryDisposeRejected(newSource);
                return false;
            }

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
            catch (Exception ex) { MVXRSDKLog.Warning($"TextureProviderSystem: 释放被拒 source 异常 {ex.Message}"); }
        }

        /// <summary>
        /// 清空当前 source；InternalRT <b>保留并清黑</b>——推流会话可能仍绑定该 RT
        /// （如切场景过渡期），不清黑观众会看到冻结的最后一帧。RT 在 Dispose 时才释放。
        /// </summary>
        public static void ClearSource()
        {
            if (s_Current != null)
            {
                try { s_Current.Detach(); } catch (Exception ex) { MVXRSDKLog.Warning($"TextureProviderSystem: Detach 异常 {ex.Message}"); }
                try { s_Current.Dispose(); } catch (Exception ex) { MVXRSDKLog.Warning($"TextureProviderSystem: Dispose 异常 {ex.Message}"); }
                s_Current = null;
            }
            ClearRTToBlack();
        }

        /// <summary>
        /// 相机销毁自保护（设计 §4.3）：attach 中的 CameraStreamSource 发现相机已销毁时回调。
        /// 正常路径是业务在场景卸载前主动 ClearStreamSource，这里是误用兜底。
        /// </summary>
        internal static void HandleSourceCameraDestroyed(IStreamSource source)
        {
            if (!ReferenceEquals(s_Current, source)) return;
            MVXRSDKLog.Warning("TextureProviderSystem: 画面源相机已被销毁（场景卸载前未 ClearStreamSource？），自动清源推黑帧");
            ClearSource();
        }

        /// <summary>固定推流尺寸：长边取 maxLongSide（≤0 时 1280），16:9，偶数对齐（H.264 要求）。</summary>
        internal static (int width, int height) ComputeFixedSize(int maxLongSide)
        {
            int longSide = maxLongSide > 0 ? maxLongSide : 1280;
            int w = longSide & ~1;
            int h = (int)Math.Round(longSide * 9.0 / 16.0) & ~1;
            return (w, h);
        }

        private static void CreateInternalRT()
        {
            var (width, height) = ComputeFixedSize(StreamConfig.Active.StreamMaxLongSide);
            // 严格对齐 com.unity.webrtc 包源码 CameraExtension.cs:44-68 的 CaptureStreamTrack 内部实现（A 级证据）：
            //   format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType)  ← 必须用 RenderTextureFormat
            //   rt = new RenderTexture(width, height, depthValue, format)                       ← 4 参构造
            //   depth 默认 RenderTextureDepth.Depth24（int 值 24）                              ← 必须有深度缓冲
            //   rt.Create()                                                                     ← 必须显式 Create
            // C 级实测（2026-05-15, Editor Win D3D11 + mediamtx 内网）：
            //   错：GraphicsFormat + RenderTextureDescriptor(w,h,format,0) → 协议层全绿但播放端黑屏
            //   对：上面对齐 CaptureStreamTrack 的参数 → 正常推流
            var format = WebRTC.GetSupportedRenderTextureFormat(SystemInfo.graphicsDeviceType);
            var rt = new RenderTexture(width, height, 24, format, RenderTextureReadWrite.Default)
            {
                name = "MVXRSDK_InternalStreamRT"
            };
            if (!rt.Create())
            {
                MVXRSDKLog.Error($"TextureProviderSystem: RenderTexture.Create 失败 {width}x{height} format={format}");
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                return;
            }
            s_InternalRT = rt;
            ClearRTToBlack();   // 新建 RT 内容未定义，先清黑避免推垃圾帧
            MVXRSDKLog.Info($"TextureProviderSystem: 创建内部 RT {width}x{height} format={format} depth=24（固定尺寸，Dispose 才释放）");
        }

        private static void ClearRTToBlack()
        {
            if (s_InternalRT == null) return;
            var prev = RenderTexture.active;
            RenderTexture.active = s_InternalRT;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = prev;
        }

        private static void ReleaseInternalRT()
        {
            if (s_InternalRT == null) return;
            s_InternalRT.Release();
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(s_InternalRT);
            else
                UnityEngine.Object.DestroyImmediate(s_InternalRT);
            s_InternalRT = null;
        }
    }
}
