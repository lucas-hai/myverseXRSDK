using System;
using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 推流音频缓冲 System：游戏音 PCM 输入 → mono 工作采样率环形缓冲 → 喂 AudioStreamTrack。
    /// （曾有麦克风第二路混音，需求取消后移除；类名保留混音语义作为将来的音源扩展点。）
    ///
    /// 设计要点：
    /// 1. **零分配热路径**：DequeueMixed 由调用方传入预分配 buffer，循环用栈/Array.Clear，
    ///    音频线程（21ms 一次回调）无 GC 压力。
    /// 2. **环形缓冲**：固定容量 32768 样本（~683ms @ 48k，PICO 24k 时 ~1.37s），写满则丢弃最旧。
    ///    避免 Queue&lt;float&gt; 的 per-sample 装箱与无限增长。
    /// 3. **采样率自适应**：工作采样率 = 设备输出采样率（Init 时锁定；PICO 4U 实测 24000）。
    ///    输入与工作率相同直通，否则线性插值重采样；跨调用边界用 lastSample + 分数 pos 保持连续。
    /// 4. **静态门面**：与 MonoSystem/SocketSystem 一致，对外暴露静态方法；测试可通过 ResetForTest 切隔离。
    /// </summary>
    internal static class AudioMixingSystem
    {
        // 工作采样率 = 设备输出采样率：游戏音 OnAudioFilterRead 的产生节奏与 feeder 回调的消费节奏
        // 都由设备输出率驱动，取同值 ring 写入/消费流速才平衡，且最常见输入（游戏音）直通零重采样。
        // 48000 仅是 Init 前 / 读不到输出率时的兜底
        private static int s_MixSampleRate = 48000;

        /// <summary>ring 内数据的真实采样率。AudioStreamFeeder.SetData 必须按它声明，否则 WebRTC 端音调/速度失真。</summary>
        internal static int MixSampleRate => s_MixSampleRate;

        // ~683ms @ 48k mono；足够吸收主线程 / 编码线程的临时抖动而不溢出
        private const int RingCapacitySamples = 32768;

        // === 游戏音 ring buffer + 锁 ===
        private static readonly float[] s_GameRing = new float[RingCapacitySamples];
        private static int s_GameRead;
        private static int s_GameWrite;
        private static int s_GameCount;
        private static readonly object s_GameLock = new object();

        // === 跨调用的线性插值状态 ===
        private static ResampleState s_GameResample = new ResampleState();

        // === Feeder 状态：由 AttachToTrack 创建，DetachFromTrack 销毁 ===
        private static GameObject s_FeederGo;
        private static AudioStreamFeeder s_Feeder;
        private static bool s_Initialized;

        private struct ResampleState
        {
            public float LastSample;  // 上次 batch 的最后一帧 mono（用于跨调用线性插值的左端点）
            public double Pos;         // 当前在虚拟源域的浮点位置（含 LastSample 作为虚拟 frame 0）
            public bool HasLast;
        }

        // === 生命周期 ===

        public static void Init()
        {
            if (s_Initialized)
            {
                MVXRSDKLog.Warning("AudioMixingSystem: 已初始化，跳过重复 Init");
                return;
            }
            // 主线程 API：Init 由 InitMVXRSDK 的同步本地阶段在主线程调用
            int outputRate = AudioSettings.outputSampleRate;
            s_MixSampleRate = outputRate > 0 ? outputRate : 48000;
            ClearAllBuffers();
            s_Initialized = true;
            MVXRSDKLog.Info($"AudioMixingSystem: 初始化完成 mixSampleRate={s_MixSampleRate}");
        }

        public static void Dispose()
        {
            if (!s_Initialized) return;
            DetachFromTrack();
            ClearAllBuffers();
            s_Initialized = false;
            MVXRSDKLog.Info("AudioMixingSystem: 已反初始化");
        }

        // === 业务接口（游戏音 Tap 在音频线程调入）===

        public static void PushGameAudio(float[] pcm, int sampleRate, int channels)
        {
            if (!s_Initialized) return;
            PushInternal(pcm, sampleRate, channels, s_GameRing, s_GameLock, ref s_GameRead, ref s_GameWrite, ref s_GameCount, ref s_GameResample);
        }

        // === Feeder 绑定（由 WebRTCSystem 调用）===

        public static void AttachToTrack(AudioStreamTrack track)
        {
            if (!s_Initialized)
            {
                MVXRSDKLog.Error("AudioMixingSystem.AttachToTrack: 未初始化");
                return;
            }
            if (track == null)
            {
                MVXRSDKLog.Warning("AudioMixingSystem.AttachToTrack: track 为 null，音频通道将持续静音");
                return;
            }
            if (s_FeederGo != null)
            {
                MVXRSDKLog.Warning("AudioMixingSystem.AttachToTrack: 已存在 feeder，先 Detach 旧的");
                DetachFromTrack();
            }

            s_FeederGo = new GameObject("MVXRSDK_AudioStreamFeeder");
            UnityEngine.Object.DontDestroyOnLoad(s_FeederGo);
            s_Feeder = s_FeederGo.AddComponent<AudioStreamFeeder>();
            s_Feeder.Initialize(track);
        }

        public static void DetachFromTrack()
        {
            if (s_Feeder != null)
            {
                // 先停 feeder（置 volatile 标志 + Stop AudioSource），让出至少一个音频回调周期再 Destroy
                s_Feeder.StopFeeding();
                s_Feeder = null;
            }
            if (s_FeederGo != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(s_FeederGo);
                else
                    UnityEngine.Object.DestroyImmediate(s_FeederGo);
                s_FeederGo = null;
            }
            // 清空缓冲，避免下次 Attach 时残留旧音频
            ClearAllBuffers();
        }

        // === 内部接口（由 AudioStreamFeeder 在音频线程调用）===

        /// <summary>
        /// 从环形缓冲取 frameSize 个 mono 样本 + 限幅写入 dst[0..frameSize)。
        /// 调用方负责 dst 的分配；本方法不分配任何对象（零 GC）。
        /// 缓冲不足时按 0 填充该样本位（保持时序）。
        /// </summary>
        internal static void DequeueMixed(float[] dst, int frameSize)
        {
            if (dst == null || dst.Length < frameSize) return;

            for (int i = 0; i < frameSize; i++) dst[i] = 0f;
            ReadAdd(s_GameRing, s_GameLock, ref s_GameRead, ref s_GameCount, dst, frameSize);

            // 限幅：OnAudioFilterRead 的 master mix 本身可能超出 [-1,1]
            //（Unity 只在最终 DAC 输出前 clamp），喂编码器前限幅防爆音
            for (int i = 0; i < frameSize; i++)
            {
                if (dst[i] > 1f) dst[i] = 1f;
                else if (dst[i] < -1f) dst[i] = -1f;
            }
        }

        private static void ReadAdd(float[] ring, object lockObj, ref int readIdx, ref int count, float[] dst, int frameSize)
        {
            lock (lockObj)
            {
                int avail = count;
                int take = avail < frameSize ? avail : frameSize;
                for (int i = 0; i < take; i++)
                {
                    dst[i] += ring[readIdx];
                    readIdx++;
                    if (readIdx >= RingCapacitySamples) readIdx = 0;
                }
                count -= take;
                // 不足部分保持 0（已 ZeroBuffer），下游编码器接收静音帧
            }
        }

        // === 实现细节：mono 转换 + 线性重采样 + ring push ===

        private static void PushInternal(
            float[] pcm, int sampleRate, int channels,
            float[] ring, object lockObj,
            ref int readIdx, ref int writeIdx, ref int count,
            ref ResampleState resample)
        {
            if (pcm == null || pcm.Length == 0) return;
            // 入参校验（含报错）由 MVXRSDK.ValidatePcmArgs 负责抛异常；这里是音频线程热路径
            // （游戏音 Tap 从 OnAudioFilterRead 调进来，音频线程禁止打日志），只做防御性静默丢弃
            if (channels < 1 || channels > 2) return;
            if (sampleRate <= 0) return;

            int srcFrames = pcm.Length / channels;
            if (srcFrames == 0) return;

            // 路径 1：输入采样率 == 工作采样率，直通（游戏音典型路径，最快）
            if (sampleRate == s_MixSampleRate)
            {
                lock (lockObj)
                {
                    for (int i = 0; i < srcFrames; i++)
                    {
                        float mono = channels == 1 ? pcm[i] : (pcm[i * 2] + pcm[i * 2 + 1]) * 0.5f;
                        RingPush(ring, ref readIdx, ref writeIdx, ref count, mono);
                    }
                }
                // 同步更新 resample 状态，以备后续切换到 44.1k 时不丢失上下文
                if (channels == 1) resample.LastSample = pcm[srcFrames - 1];
                else resample.LastSample = (pcm[(srcFrames - 1) * 2] + pcm[(srcFrames - 1) * 2 + 1]) * 0.5f;
                resample.HasLast = true;
                resample.Pos = 0;  // 直通模式不维持小数位置
                return;
            }

            // 路径 2：任意采样率 → 工作采样率 线性插值。上采样（如 44.1k→48k）质量足够；
            // 高于工作率的输入降采样（如 48k 麦克风→24k）无低通会有轻微混叠，
            // 语音 Nyquist 12k 内不受影响，局域网推流场景可接受
            ResampleAndPush(pcm, channels, srcFrames, sampleRate, ring, lockObj, ref readIdx, ref writeIdx, ref count, ref resample);
        }

        private static void ResampleAndPush(
            float[] pcm, int channels, int srcFrames, int sampleRate,
            float[] ring, object lockObj,
            ref int readIdx, ref int writeIdx, ref int count,
            ref ResampleState rs)
        {
            // 比例 = 源帧/目标帧；每输出 1 帧消费 ratio 个源帧
            double ratio = (double)sampleRate / s_MixSampleRate;

            // 虚拟源数组：若 HasLast，frame 0 = LastSample，frame 1..srcFrames = pcm
            // 否则 frame 0..srcFrames-1 = pcm
            bool hasLast = rs.HasLast;
            float lastFromPrev = rs.LastSample;
            int virtualFrames = hasLast ? srcFrames + 1 : srcFrames;
            double pos = rs.Pos;

            lock (lockObj)
            {
                // 必须保证 i0+1 在范围内：pos < virtualFrames - 1
                while (pos < virtualFrames - 1)
                {
                    int i0 = (int)pos;
                    double frac = pos - i0;
                    float s0 = GetVirtualMono(pcm, channels, hasLast, lastFromPrev, i0);
                    float s1 = GetVirtualMono(pcm, channels, hasLast, lastFromPrev, i0 + 1);
                    float interp = (float)(s0 * (1.0 - frac) + s1 * frac);
                    RingPush(ring, ref readIdx, ref writeIdx, ref count, interp);
                    pos += ratio;
                }
            }

            // 保存状态：下次的虚拟 frame 0 = 本次 pcm 最后一帧
            float lastMono = channels == 1
                ? pcm[srcFrames - 1]
                : (pcm[(srcFrames - 1) * 2] + pcm[(srcFrames - 1) * 2 + 1]) * 0.5f;
            // 下次 pos = 本次 pos - 偏移；偏移 = virtualFrames - 1（因为本次最后一帧将作为下次 frame 0）
            rs.Pos = pos - (virtualFrames - 1);
            rs.LastSample = lastMono;
            rs.HasLast = true;
            // 若 pos 退化为负（理论上不会，但浮点容差处理）
            if (rs.Pos < 0) rs.Pos = 0;
        }

        // 取"虚拟源数组"frame i 的 mono 样本（避免 local function 捕获 ref 参数的限制）
        private static float GetVirtualMono(float[] pcm, int channels, bool hasLast, float lastFromPrev, int i)
        {
            if (hasLast)
            {
                if (i == 0) return lastFromPrev;
                int p = i - 1;
                return channels == 1 ? pcm[p] : (pcm[p * 2] + pcm[p * 2 + 1]) * 0.5f;
            }
            return channels == 1 ? pcm[i] : (pcm[i * 2] + pcm[i * 2 + 1]) * 0.5f;
        }

        private static void RingPush(float[] ring, ref int readIdx, ref int writeIdx, ref int count, float sample)
        {
            // 满则丢弃最旧（推进 read 指针，保留新数据）。对实时音频选择"drop oldest"比"drop newest"听感更连贯
            if (count >= RingCapacitySamples)
            {
                readIdx++;
                if (readIdx >= RingCapacitySamples) readIdx = 0;
                count--;
            }
            ring[writeIdx] = sample;
            writeIdx++;
            if (writeIdx >= RingCapacitySamples) writeIdx = 0;
            count++;
        }

        private static void ClearAllBuffers()
        {
            lock (s_GameLock)
            {
                Array.Clear(s_GameRing, 0, s_GameRing.Length);
                s_GameRead = 0; s_GameWrite = 0; s_GameCount = 0;
                s_GameResample = new ResampleState();
            }
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>仅供单测：完全清空状态，无 Init 副作用日志。</summary>
        internal static void ResetForTest()
        {
            DetachFromTrack();
            ClearAllBuffers();
            s_Initialized = false;
            s_MixSampleRate = 48000;
        }

        /// <summary>仅供单测：以指定工作采样率初始化，绕开对 AudioSettings.outputSampleRate 的依赖。</summary>
        internal static void InitForTest(int mixSampleRate)
        {
            ResetForTest();
            s_MixSampleRate = mixSampleRate;
            s_Initialized = true;
        }
#endif
    }
}
