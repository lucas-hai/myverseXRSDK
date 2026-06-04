using System;
using System.Collections;
using UnityEngine;

namespace MyVerseXRSDK.Streaming
{
    /// <summary>
    /// 推流采集装配 + 切镜业务入口 MonoBehaviour：
    /// 把 CameraStreamCapture / GameAudioStreamCapture / MicrophoneStreamCapture
    /// 拧在一起暴露给 Inspector，并提供"时长切镜 + 自动切回"业务能力。
    ///
    /// 职责边界：
    /// - 准备并提供画面 / 音频源给 SDK（内部调 MVXRSDK.SetStreamSource(IStreamSource)）
    /// - 切镜业务编排：业务调 <see cref="SwitchCameraTemporary"/>，Rig 走 IStreamSource
    ///   通道切到目标相机、倒计时到期后自动切回主相机 source。
    /// - 不调 MVXRSDK.InitMVXRSDK（业务自己 Init，控制时机/模式）。
    /// - 不调 Debug_SimulateNotifyLive / SendDirectorRequest（这些是业务/测试组件的职责）。
    ///
    /// SDK 层只暴露"切到某个 IStreamSource"原语；切镜的产品规则（倒计时、本机判定、切回）在本类里。
    /// </summary>
    [AddComponentMenu("MyVerse XR SDK/Stream Rig")]
    [DisallowMultipleComponent]
    public sealed class MVXRStreamRig : MonoBehaviour
    {
        [Header("画面源")]
        [Tooltip("业务主相机：玩家看到的画面，配置不会被修改。留空则不推画面。")]
        public Camera mainCamera;

        [Header("音频源")]
        [Tooltip("游戏音 AudioListener：通过 OnAudioFilterRead 抓 master mix。留空则不推游戏音。")]
        public AudioListener gameAudioListener;

        [Tooltip("是否采集麦克风推给 SDK。注意会占用麦克风设备，可能与 Pico 语音 SDK 冲突。")]
        public bool captureMicrophone = false;

        [Tooltip("麦克风采样率，SDK 仅支持 48000 / 44100。")]
        public int micSampleRate = 48000;

        [Tooltip("麦克风设备名，留空使用系统默认。")]
        public string micDevice = "";

        [Header("切镜（业务）")]
        [Tooltip("可选：场景预设的切镜目标相机列表，配合 SwitchCameraTemporary(index, durationSec) 使用。\n" +
                 "约定：\n" +
                 "1) URP Render Type = Base（Overlay 会被忽略 targetTexture 导致定格）\n" +
                 "2) Camera.enabled 默认设 false，Rig 切镜时由底层 CameraStreamSource 自动 enable，切回还原")]
        public Camera[] directorCameras;

        [Header("推流配置 Asset")]
        [Tooltip("推流视频编码参数 Asset（Fps / 长边像素 / 码率 / H.264）。\n" +
                 "创建方式：Project 右键 → Create → MyVerse XR SDK → Stream Config。\n" +
                 "Rig OnEnable 时自动 Apply 写入 StreamConfig.Active。\n" +
                 "留空时全部走 SDK 默认（Fps=30 / 长边 1280 / 码率 3500 / H.264 强制）。")]
        public StreamConfigAsset streamConfigAsset;

        private CameraStreamCapture m_Camera;
        private GameAudioStreamCapture m_GameAudio;
        private MicrophoneStreamCapture m_Microphone;
        // 主相机画面源；Rig 始终持有这个引用，切镜走完倒计时切回它
        private RenderTextureStreamSource m_StreamSource;
        // 当前切镜中的相机源；非 null 表示处于切镜状态
        private CameraStreamSource m_PendingCameraSource;
        private Coroutine m_RestoreCoroutine;
        // 主相机首帧拿到 RT、但 SDK 尚未 Init 时暂存；协程等 IsReady=true 后再 attach
        private RenderTexture m_PendingAttachRT;
        private Coroutine m_WaitInitCoroutine;
        // 推流目标尺寸（StreamConfig.StreamMaxLongSide 同比例缩后的 wh）；attach 时算一次，切镜复用
        private int m_StreamWidth;
        private int m_StreamHeight;

        /// <summary>当前画面 RT；首帧渲染后才非空。</summary>
        public RenderTexture StreamTexture => m_Camera?.Texture;

        /// <summary>当前是否处于切镜状态（被 <see cref="SwitchCameraTemporary"/> 切到副相机且未到期切回）。</summary>
        public bool IsInDirectorSwitch => m_PendingCameraSource != null;

        /// <summary>画面 RT 就绪 + 已交给 SDK 后触发；订阅时若已就绪会立即回调一次。</summary>
        public event Action<RenderTexture> Ready
        {
            add
            {
                m_Ready += value;
                var rt = StreamTexture;
                if (rt != null) value?.Invoke(rt);
            }
            remove { m_Ready -= value; }
        }
        private event Action<RenderTexture> m_Ready;

        /// <summary>切镜成功后触发；参数 = 当前切到的目标 Camera + 倒计时秒数。</summary>
        public event Action<Camera, int> OnSwitched;

        /// <summary>切回主相机源后触发（无论是倒计时到期还是业务主动调 <see cref="RestoreOriginalCamera"/>）。</summary>
        public event Action OnRestored;

        private void OnEnable()
        {
            // 挂了 Asset 就 Apply，否则保持 SDK 默认（StreamConfig.Active 不动）
            // 之后所有读取（CameraStreamCapture / AttachPendingSource）统一走 StreamConfig.Active
            if (streamConfigAsset != null)
            {
                streamConfigAsset.Apply();
            }

            if (mainCamera != null)
            {
                m_Camera = new CameraStreamCapture(mainCamera, StreamConfig.Active.Fps);
                m_Camera.Ready += OnCameraReady;
            }

            if (gameAudioListener != null)
            {
                m_GameAudio = new GameAudioStreamCapture(gameAudioListener);
            }

            if (captureMicrophone)
            {
                m_Microphone = new MicrophoneStreamCapture(
                    string.IsNullOrEmpty(micDevice) ? null : micDevice,
                    micSampleRate);
            }
        }

        private void OnCameraReady(RenderTexture rt)
        {
            if (m_StreamSource != null) return; // 守一手：避免 Ready 被重订阅导致重复建 source

            // SDK 还没 Init（业务异步 InitMVXRSDK 慢于主相机首帧渲染），暂存 RT 等协程轮询
            // 这避免了 TextureProviderSystem.SwitchSource: 未初始化 报错 + 后续推流没源的问题
            m_PendingAttachRT = rt;
            if (MVXRSDK.IsReady)
            {
                AttachPendingSource();
            }
            else if (m_WaitInitCoroutine == null)
            {
                m_WaitInitCoroutine = StartCoroutine(Co_WaitSdkInitAndAttach());
            }
        }

        private IEnumerator Co_WaitSdkInitAndAttach()
        {
            // 每帧检查 SDK 是否 Ready；轻量级 yield 不影响性能
            while (!MVXRSDK.IsReady) yield return null;
            m_WaitInitCoroutine = null;
            AttachPendingSource();
        }

        private void AttachPendingSource()
        {
            if (m_PendingAttachRT == null) return;
            var rt = m_PendingAttachRT;
            m_PendingAttachRT = null;

            // 按 StreamConfig.StreamMaxLongSide 同比例缩 → InternalRT 实际尺寸（编码器输入尺寸）
            // 缩放在 RenderTextureStreamSource.BlitTick 的 Graphics.Blit 里做（跨尺寸/格式安全）
            // CameraStreamCapture 的 cmd.CopyTexture 走原 XR 单眼尺寸（同尺寸 GPU 拷贝）
            var cfg = StreamConfig.Active;
            var (w, h) = CameraStreamCapture.ComputeStreamSize(rt.width, rt.height, cfg.StreamMaxLongSide);
            m_StreamWidth = w;
            m_StreamHeight = h;
            Debug.Log($"[MVXRStreamRig] 推流目标尺寸 src {rt.width}x{rt.height} → stream {w}x{h} (maxLongSide={cfg.StreamMaxLongSide}, fps={cfg.Fps})");

            // 用 IStreamSource 路径而非 RenderTexture 重载——这样能拿到 source 引用、订阅生命周期事件
            m_StreamSource = new RenderTextureStreamSource(rt, w, h);
            m_StreamSource.OnAttached += OnStreamSourceAttached;
            m_StreamSource.OnDetached += OnStreamSourceDetached;
            MVXRSDK.SetStreamSource(m_StreamSource);
            // SetStreamSource 内部同步 Attach，OnStreamSourceAttached 已在此刻触发
            m_Ready?.Invoke(rt);
        }

        // SDK 端 source 进入"被使用"状态：恢复主相机 Blit
        private void OnStreamSourceAttached()
        {
            if (m_Camera != null) m_Camera.Paused = false;
        }

        // SDK 端 source 进入"未被使用"状态（Director 切走、ClearSource、业务切到其它 source）：
        // 暂停 Blit，省 GPU。再次被 Attach 时自动恢复
        private void OnStreamSourceDetached()
        {
            if (m_Camera != null) m_Camera.Paused = true;
        }

        private void OnDisable()
        {
            // 等待 SDK Init 的协程也要清掉（场景未推流就停了，避免 SDK 后续 Init 时悬空 attach）
            if (m_WaitInitCoroutine != null)
            {
                StopCoroutine(m_WaitInitCoroutine);
                m_WaitInitCoroutine = null;
            }
            m_PendingAttachRT = null;

            // 切镜中被 Disable：先取消倒计时，避免协程在 Disable 后继续
            if (m_RestoreCoroutine != null)
            {
                StopCoroutine(m_RestoreCoroutine);
                m_RestoreCoroutine = null;
            }
            m_PendingCameraSource = null;

            // 先反订阅、再主动 ClearSource，避免 SDK 内部 Detach 时回调到已 Dispose 的 m_Camera
            if (m_StreamSource != null)
            {
                m_StreamSource.OnAttached -= OnStreamSourceAttached;
                m_StreamSource.OnDetached -= OnStreamSourceDetached;
                // 主动清，让 SDK 释放对我们 RT 的引用（避免下帧 BlitTick 撞到已 Destroy 的 RT）
                MVXRSDK.ClearStreamSource();
                m_StreamSource = null;
            }
            m_Camera?.Dispose();
            m_Camera = null;
            m_GameAudio?.Dispose();
            m_GameAudio = null;
            m_Microphone?.Dispose();
            m_Microphone = null;
            m_Ready = null;
            OnSwitched = null;
            OnRestored = null;
        }

        // ===== 切镜业务（时长切镜 + 自动切回）=====

        /// <summary>
        /// 时长切镜：把推流画面源临时切到 <paramref name="target"/>，<paramref name="durationSec"/>
        /// 秒后自动切回主相机源。底层走 SDK 的 IStreamSource 通道，CameraCaptureBridge 主相机
        /// Blit 会自动 Pause（IStreamSource.OnDetached → CameraStreamCapture.Paused）。
        ///
        /// 重复调用会抢占：取消上一次倒计时、Detach 上一次相机源、切到新相机。
        /// 推流尚未启动（m_StreamSource 还没建好）时拒绝并打 warning。
        /// </summary>
        public void SwitchCameraTemporary(Camera target, int durationSec)
        {
            if (target == null)
            {
                Debug.LogError("[MVXRStreamRig] SwitchCameraTemporary: target 为 null");
                return;
            }
            if (durationSec <= 0)
            {
                Debug.LogError($"[MVXRStreamRig] SwitchCameraTemporary: durationSec={durationSec} 必须 > 0");
                return;
            }
            if (m_StreamSource == null || m_Camera?.Texture == null)
            {
                Debug.LogWarning("[MVXRStreamRig] SwitchCameraTemporary: 主相机画面源未就绪，等 Ready 后再切");
                return;
            }
            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("[MVXRStreamRig] SwitchCameraTemporary: Rig 未启用，忽略");
                return;
            }

            // 抢占：取消旧倒计时（不需要手动 Detach 旧 source——SwitchSource 内部会 Detach）
            if (m_RestoreCoroutine != null)
            {
                StopCoroutine(m_RestoreCoroutine);
                m_RestoreCoroutine = null;
            }

            // 切镜源尺寸必须 = 当前 InternalRT 尺寸（即 StreamConfig.StreamMaxLongSide 缩后），否则
            // TextureProviderSystem 拒绝切源不断流。用 AttachPendingSource 时缓存的 m_StreamWidth/Height
            m_PendingCameraSource = new CameraStreamSource(target, m_StreamWidth, m_StreamHeight);
            MVXRSDK.SetStreamSource(m_PendingCameraSource);
            // 上面 SetStreamSource 触发原 m_StreamSource.Detach → OnStreamSourceDetached
            // → m_Camera.Paused=true（主相机 Blit 停），新 CameraStreamSource.Attach
            // → target.targetTexture=InternalRT + target.enabled=true（SDK 自动接管）

            m_RestoreCoroutine = StartCoroutine(Co_RestoreAfter(durationSec));

            try { OnSwitched?.Invoke(target, durationSec); }
            catch (Exception ex) { Debug.LogError($"[MVXRStreamRig] OnSwitched 回调异常 {ex}"); }
        }

        /// <summary>按 <see cref="directorCameras"/> 数组下标切镜。</summary>
        public void SwitchCameraTemporary(int directorCameraIndex, int durationSec)
        {
            if (directorCameras == null || directorCameraIndex < 0 || directorCameraIndex >= directorCameras.Length)
            {
                Debug.LogError($"[MVXRStreamRig] SwitchCameraTemporary: 下标 {directorCameraIndex} 越界（directorCameras.Length={directorCameras?.Length ?? 0}）");
                return;
            }
            SwitchCameraTemporary(directorCameras[directorCameraIndex], durationSec);
        }

        /// <summary>业务主动切回主相机源，不等倒计时。无切镜中状态时无副作用。</summary>
        public void RestoreOriginalCamera()
        {
            if (m_RestoreCoroutine != null)
            {
                StopCoroutine(m_RestoreCoroutine);
                m_RestoreCoroutine = null;
            }
            RestoreImmediate();
        }

        private IEnumerator Co_RestoreAfter(int durationSec)
        {
            yield return new WaitForSeconds(durationSec);
            m_RestoreCoroutine = null;
            RestoreImmediate();
        }

        private void RestoreImmediate()
        {
            if (m_PendingCameraSource == null) return;
            if (m_StreamSource == null)
            {
                // 极端：切镜中 Rig 被 Disable（OnDisable 会清 m_StreamSource）；什么都不做
                m_PendingCameraSource = null;
                return;
            }
            // 切回主相机源：触发 m_PendingCameraSource.Detach（还原 target.targetTexture/enabled）+
            // m_StreamSource.Attach（恢复主相机 Blit via OnAttached → Paused=false）
            MVXRSDK.SetStreamSource(m_StreamSource);
            m_PendingCameraSource = null;

            try { OnRestored?.Invoke(); }
            catch (Exception ex) { Debug.LogError($"[MVXRStreamRig] OnRestored 回调异常 {ex}"); }
        }
    }
}
