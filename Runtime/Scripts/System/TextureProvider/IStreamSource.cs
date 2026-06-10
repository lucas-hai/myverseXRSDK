using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流画面源抽象。SDK 内置 2 个实现：<see cref="CameraStreamSource"/>、<see cref="RenderTextureStreamSource"/>。
    /// 业务侧可自行实现本接口（如 XR 立体合成、多摄拼接、屏幕捕获等"喂画面工具"），
    /// 通过 <see cref="TextureProviderSystem.SwitchSource"/> 注册即可纳入推流链路。
    ///
    /// 切源不断流约定：SDK 维护一张长生命周期 RT（VideoStreamTrack 绑定），
    /// SwitchSource 时只 Detach 旧 source + Attach 新 source 到同一张 RT；
    /// InternalRT 为固定尺寸（StreamConfig.StreamMaxLongSide 按 16:9），任意源可热切；
    /// 推流会话活跃且已有源时 SwitchSource 会丢弃新源（一相机推流保护）。
    /// </summary>
    public interface IStreamSource : IDisposable
    {
        /// <summary>画面源宽（像素，仅信息展示用；不再参与 InternalRT 尺寸决策）。</summary>
        int Width { get; }

        /// <summary>画面源高（像素，仅信息展示用）。</summary>
        int Height { get; }

        /// <summary>用于日志/调试展示，如 "Camera(MainCam)" / "RT(1280x720)"。</summary>
        string DisplayName { get; }

        /// <summary>
        /// 绑定到 SDK 内部 RT。实现要把自己的画面渲染/Blit 到 <paramref name="targetRT"/>。
        /// 同一 source 在被 Detach 之前不会重复 Attach。
        /// </summary>
        void Attach(RenderTexture targetRT);

        /// <summary>
        /// 解绑。实现需要还原它对外部对象的修改（如 Camera.targetTexture 还原）、
        /// 取消 LateUpdate 钩子等。Detach 后允许再次 Attach（同一或不同 targetRT)。
        /// </summary>
        void Detach();

        /// <summary>
        /// 本 source 被 SDK Attach 后触发（已开始喂画面）。
        /// 上层（如 MVXRStreamRig）可据此恢复采集；Director 切回时也会触发。
        /// </summary>
        event Action OnAttached;

        /// <summary>
        /// 本 source 被 SDK Detach 后触发（已不再喂画面，但仍可能再次被 Attach）。
        /// 上层可据此暂停采集，避免空跑 GPU；Director 切走时会触发。
        /// </summary>
        event Action OnDetached;
    }
}
