using System;
using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 音频混音 System：两路 PCM 输入（游戏音频 + 麦克风）→ mono 48kHz 环形缓冲 → 喂 AudioStreamTrack。
    ///
    /// 设计要点：
    /// 1. **零分配热路径**：DequeueMixed 由调用方传入预分配 buffer，混音循环用栈/Array.Clear，
    ///    音频线程（21ms 一次回调）无 GC 压力。
    /// 2. **环形缓冲**：固定容量 32768 样本（~683ms @ 48k），写满则丢弃最新（"drop newest"）。
    ///    避免 Queue&lt;float&gt; 的 per-sample 装箱与无限增长。
    /// 3. **采样率自适应**：支持 44.1k / 48k 输入；其他采样率丢弃并 Warning。
    ///    跨调用边界用 lastSample + 分数 pos 保持线性插值连续。
    /// 4. **静态门面**：与 MonoSystem/SocketSystem 一致，对外暴露静态方法；测试可通过 ResetForTest 切隔离。
    /// </summary>
    internal static class AudioMixingSystem
    {
        private const int TargetSampleRate = 48000;
        // ~683ms @ 48k mono；足够吸收主线程 / 编码线程的临时抖动而不溢出
        private const int RingCapacitySamples = 32768;

        // === 两路独立 ring buffer + 锁 ===
        private static readonly float[] s_GameRing = new float[RingCapacitySamples];
        private static int s_GameRead;
        private static int s_GameWrite;
        private static int s_GameCount;
        private static readonly object s_GameLock = new object();

        private static readonly float[] s_MicRing = new float[RingCapacitySamples];
        private static int s_MicRead;
        private static int s_MicWrite;
        private static int s_MicCount;
        private static readonly object s_MicLock = new object();

        // === 跨调用的线性插值状态 ===
        private static ResampleState s_GameResample = new ResampleState();
        private static ResampleState s_MicResample = new ResampleState();

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
            ClearAllBuffers();
            s_Initialized = true;
            MVXRSDKLog.Info("AudioMixingSystem: 初始化完成");
        }

        public static void Dispose()
        {
            if (!s_Initialized) return;
            DetachFromTrack();
            ClearAllBuffers();
            s_Initialized = false;
            MVXRSDKLog.Info("AudioMixingSystem: 已反初始化");
        }

        // === 业务接口（外部线程：通常是主线程 / 麦克风线程）===

        public static void PushGameAudio(float[] pcm, int sampleRate, int channels)
        {
            if (!s_Initialized) return;
            PushInternal(pcm, sampleRate, channels, s_GameRing, s_GameLock, ref s_GameRead, ref s_GameWrite, ref s_GameCount, ref s_GameResample, "game");
        }

        public static void PushMicAudio(float[] pcm, int sampleRate, int channels)
        {
            if (!s_Initialized) return;
            PushInternal(pcm, sampleRate, channels, s_MicRing, s_MicLock, ref s_MicRead, ref s_MicWrite, ref s_MicCount, ref s_MicResample, "mic");
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
        /// 从两路环形缓冲取 frameSize 个 mono 样本，按位相加 + 限幅写入 dst[0..frameSize)。
        /// 调用方负责 dst 的分配；本方法不分配任何对象（零 GC）。
        /// 缓冲不足时按 0 填充该样本位（保持时序）。
        /// </summary>
        internal static void DequeueMixed(float[] dst, int frameSize)
        {
            if (dst == null || dst.Length < frameSize) return;

            // 分别从两路 ring 取，避免持锁期间相加（减少锁内时间）
            for (int i = 0; i < frameSize; i++) dst[i] = 0f;

            // 注意：分两次 lock，期间另一路可被其他线程 push；可接受，单路样本一致性即可
            ReadAdd(s_GameRing, s_GameLock, ref s_GameRead, ref s_GameCount, dst, frameSize);
            ReadAdd(s_MicRing, s_MicLock, ref s_MicRead, ref s_MicCount, dst, frameSize);

            // 限幅，防止两路叠加溢出
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
            ref ResampleState resample,
            string tag)
        {
            if (pcm == null || pcm.Length == 0) return;
            if (channels < 1 || channels > 2)
            {
                MVXRSDKLog.Warning($"AudioMixingSystem[{tag}]: 不支持的通道数 {channels}（仅 1/2），丢弃");
                return;
            }
            if (sampleRate != 48000 && sampleRate != 44100)
            {
                MVXRSDKLog.Warning($"AudioMixingSystem[{tag}]: 不支持的采样率 {sampleRate}Hz（仅 48k/44.1k），丢弃");
                return;
            }

            int srcFrames = pcm.Length / channels;
            if (srcFrames == 0) return;

            // 路径 1：48k 输入直通（最常见路径，最快）
            if (sampleRate == TargetSampleRate)
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

            // 路径 2：44.1k → 48k 线性插值
            ResampleAndPush(pcm, channels, srcFrames, sampleRate, ring, lockObj, ref readIdx, ref writeIdx, ref count, ref resample);
        }

        private static void ResampleAndPush(
            float[] pcm, int channels, int srcFrames, int sampleRate,
            float[] ring, object lockObj,
            ref int readIdx, ref int writeIdx, ref int count,
            ref ResampleState rs)
        {
            // 比例 = 源帧/目标帧；每输出 1 帧消费 ratio 个源帧
            double ratio = (double)sampleRate / TargetSampleRate;

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
            lock (s_MicLock)
            {
                Array.Clear(s_MicRing, 0, s_MicRing.Length);
                s_MicRead = 0; s_MicWrite = 0; s_MicCount = 0;
                s_MicResample = new ResampleState();
            }
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>仅供单测：完全清空状态，无 Init 副作用日志。</summary>
        internal static void ResetForTest()
        {
            DetachFromTrack();
            ClearAllBuffers();
            s_Initialized = false;
        }
#endif
    }
}
