using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed partial class WebRtcMediaPeer
    {
        private IEnumerator Negotiate()
        {
            var offer = peer.CreateOffer();
            yield return offer;
            if (closing) yield break;
            if (offer.IsError) { Fail("创建SDP offer失败：" + offer.Error.message, false); yield break; }
            var description = offer.Desc;
            if (options.ForceH264)
            {
                if (!SdpMunger.ContainsH264(description.sdp)) { Fail("设备SDP不含H.264编码能力", false); yield break; }
                description.sdp = SdpMunger.ForceH264Only(description.sdp);
                if (!SdpMunger.ValidateVideoPayload(description.sdp)) { Fail("H.264 SDP payload无效", false); yield break; }
            }
            description.sdp = SdpMunger.SetBandwidth(description.sdp, options.VideoBandwidthKbps);
            var local = peer.SetLocalDescription(ref description);
            yield return local;
            if (closing) yield break;
            if (local.IsError) { Fail("设置本地SDP失败：" + local.Error.message, false); yield break; }
            ApplySenderParameters();
            double deadline = Time.realtimeSinceStartupAsDouble + options.IceGatheringTimeoutSeconds;
            while (!closing && peer.GatheringState != RTCIceGatheringState.Complete)
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline) { Fail("ICE收集超时", true); yield break; }
                yield return null;
            }
            if (closing) yield break;
            negotiate = null;
            LocalOffer?.Invoke(peer.LocalDescription.sdp);
        }

        public void SetAnswer(string value)
        {
            if (closing) return;
            if (peer == null || string.IsNullOrWhiteSpace(value)) { Fail("SDP answer或会话无效", false); return; }
            host.Cancel(answer);
            answer = host.Run(ApplyAnswer(value));
        }

        private IEnumerator ApplyAnswer(string value)
        {
            var description = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = value };
            var operation = peer.SetRemoteDescription(ref description);
            yield return operation;
            if (closing) yield break;
            if (operation.IsError) Fail("设置远端SDP失败：" + operation.Error.message, false);
            answer = null;
        }

        private RTCRtpTransceiver VideoTransceiver()
        {
            foreach (var transceiver in peer.GetTransceivers())
                if (transceiver.Sender != null && transceiver.Sender.Track == videoTrack) return transceiver;
            return null;
        }

        private void ApplyCodecPreference()
        {
            if (!options.ForceH264) return;
            try
            {
                var capabilities = RTCRtpSender.GetCapabilities(TrackKind.Video);
                if (capabilities.codecs == null) return;
                var preferred = new List<RTCRtpCodecCapability>();
                var remaining = new List<RTCRtpCodecCapability>();
                foreach (var codec in capabilities.codecs)
                {
                    if (string.Equals(codec.mimeType, "video/H264", StringComparison.OrdinalIgnoreCase)) preferred.Add(codec);
                    else remaining.Add(codec);
                }
                if (preferred.Count == 0) return;
                preferred.AddRange(remaining);
                var transceiver = VideoTransceiver();
                if (transceiver == null) return;
                var result = transceiver.SetCodecPreferences(preferred.ToArray());
                if (result != RTCErrorType.None) host.Log.Write(SdkLogLevel.Warning, "Streaming", "[MVXRSDK] 设置codec偏好失败，继续使用SDP校验：" + result);
            }
            catch (Exception error) { host.Log.Write(SdkLogLevel.Warning, "Streaming", "[MVXRSDK] 设置codec偏好异常，继续使用SDP校验：" + error.Message); }
        }

        private void ApplySenderParameters()
        {
            try
            {
                var sender = VideoTransceiver()?.Sender;
                if (sender == null) return;
                var parameters = sender.GetParameters();
                if (parameters.encodings == null || parameters.encodings.Length == 0)
                { host.Log.Write(SdkLogLevel.Warning, "Streaming", "[MVXRSDK] 编码参数尚未建立，无法设置码率/帧率"); return; }
                var encoding = parameters.encodings[0];
                encoding.maxBitrate = (ulong)options.VideoBandwidthKbps * 1000;
                encoding.minBitrate = (ulong)options.VideoMinBitrateKbps * 1000;
                encoding.maxFramerate = (uint)options.FramesPerSecond;
                var result = sender.SetParameters(parameters);
                if (result.errorType != RTCErrorType.None) host.Log.Write(SdkLogLevel.Warning, "Streaming", "[MVXRSDK] 设置编码参数失败：" + result.message);
            }
            catch (Exception error) { host.Log.Write(SdkLogLevel.Warning, "Streaming", "[MVXRSDK] 设置编码参数异常：" + error.Message); }
        }
    }
}
