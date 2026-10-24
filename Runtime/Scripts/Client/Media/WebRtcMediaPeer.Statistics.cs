using System;
using System.Collections;
using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed partial class WebRtcMediaPeer
    {
        private RTCStatsReportAsyncOperation pendingStats;
        private ulong lastVideoBytes;
        private ulong lastAudioBytes;
        private double lastStatsTime;

        private IEnumerator CollectStatistics()
        {
            while (!closing)
            {
                yield return new WaitForSecondsRealtime(Math.Max(0.1f, options.StatisticsIntervalSeconds));
                if (closing) yield break;
                pendingStats = peer.GetStats();
                yield return pendingStats;
                if (closing) yield break;
                var completed = pendingStats;
                pendingStats = null;
                if (completed.IsError)
                { host.Log.Write(SdkLogLevel.Warning, "Streaming", "[MVXRSDK] 获取媒体统计失败：" + completed.Error.message); continue; }
                using var report = completed.Value;
                if (report == null) continue;
                Statistics?.Invoke(BuildStatistics(report));
            }
        }

        private StreamingStatistics BuildStatistics(RTCStatsReport report)
        {
            ulong videoBytes = 0;
            ulong audioBytes = 0;
            long lost = 0;
            double rtt = 0;
            foreach (var entry in report.Stats)
            {
                if (entry.Value is RTCOutboundRTPStreamStats outbound)
                {
                    if (outbound.kind == "video") videoBytes += outbound.bytesSent;
                    else if (outbound.kind == "audio") audioBytes += outbound.bytesSent;
                }
                else if (entry.Value is RTCRemoteInboundRtpStreamStats inbound) lost += (long)inbound.packetsLost;
                else if (entry.Value is RTCIceCandidatePairStats pair && pair.nominated) rtt = pair.currentRoundTripTime * 1000;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = lastStatsTime > 0 ? now - lastStatsTime : 0;
            double videoRate = elapsed > 0 && videoBytes >= lastVideoBytes ? (videoBytes - lastVideoBytes) * 0.008 / elapsed : 0;
            double audioRate = elapsed > 0 && audioBytes >= lastAudioBytes ? (audioBytes - lastAudioBytes) * 0.008 / elapsed : 0;
            lastVideoBytes = videoBytes; lastAudioBytes = audioBytes; lastStatsTime = now;
            return new StreamingStatistics(epoch, now, videoRate, audioRate, rtt, lost);
        }
    }
}
