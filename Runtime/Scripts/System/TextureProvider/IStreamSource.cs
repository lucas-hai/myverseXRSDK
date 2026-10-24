using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 独立相机或普通外部RT的画面源。接受后由客户端拥有，拒绝时仍归调用方；不采集XR eye buffer或屏幕。
    /// Attach/Detach/Tick均由所属Runtime在主线程调用；实现不得接入全局帧调度或销毁借用的业务对象。
    /// </summary>
    public interface IStreamSource : IDisposable
    {
        /// <summary>借用的相机或纹理是否仍可用；失效后Runtime清源并继续输出黑帧。</summary>
        bool IsAvailable { get; }

        /// <summary>由所属Runtime驱动一次画面更新；不代表本帧已经被编码或送达。</summary>
        void Tick(double now, int framesPerSecond);

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
