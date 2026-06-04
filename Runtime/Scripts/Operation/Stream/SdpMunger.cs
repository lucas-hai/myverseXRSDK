using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MyVerseXRSDK
{
    /// <summary>
    /// SDP 文本处理：H.264 强制 + BWE 上限。
    /// com.unity.webrtc 3.0.0-pre.x 可能默认选 VP8（issue #990），必须客户端 munge
    /// SDP 强制 H.264，否则 Pico 走软编 VP8 导致 CPU 爆掉。
    /// </summary>
    internal static class SdpMunger
    {
        /// <summary>
        /// SDP 是否包含 H.264 codec 声明。
        /// 用于 ForceH264Only 之前的预检：若 com.unity.webrtc 在该平台未启用 H.264，
        /// 直接调 ForceH264Only 会把 m=video payload list 删空，mediamtx 拒绝
        /// </summary>
        public static bool ContainsH264(string sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return false;
            return Regex.IsMatch(sdp, @"^a=rtpmap:\d+\s+H264/", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        }

        /// <summary>
        /// 只保留 H.264（及其 RTX 重传 payload），删除其他视频 codec 的所有 payload。
        /// 用白名单而非黑名单：com.unity.webrtc 输出 SDP 里 RTX payload（a=rtpmap:N rtx/90000 +
        /// a=fmtp:N apt=PT）若指向被删的 VP8/VP9 payload，会导致 libwebrtc 报
        /// "Failed to set local video description recv parameters"，必须一并删掉。
        /// </summary>
        public static string ForceH264Only(string sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return sdp;

            var lines = SplitSdpLines(sdp);

            // Pass 1: 找出所有 H.264 的 payload type
            var h264Payloads = new HashSet<string>();
            foreach (var line in lines)
            {
                var m = Regex.Match(line, @"^a=rtpmap:(\d+)\s+H264/", RegexOptions.IgnoreCase);
                if (m.Success) h264Payloads.Add(m.Groups[1].Value);
            }

            // Pass 2: 找出指向 H.264 的 RTX payload —— a=fmtp:RTX_PT apt=H264_PT
            // 这些 RTX 必须连同 H.264 一起保留，否则 m=video 行的 RTX payload 引用会断裂
            var rtxForH264 = new HashSet<string>();
            foreach (var line in lines)
            {
                // RFC 4566 允许 `apt = 96` 写法（key/value 间空格）；正则容空格，避免漏 RTX 导致 m=video payload 引用断裂
                var fmtp = Regex.Match(line, @"^a=fmtp:(\d+)\s+apt\s*=\s*(\d+)", RegexOptions.IgnoreCase);
                if (fmtp.Success && h264Payloads.Contains(fmtp.Groups[2].Value))
                {
                    rtxForH264.Add(fmtp.Groups[1].Value);
                }
            }

            var keepPayloads = new HashSet<string>(h264Payloads);
            foreach (var pt in rtxForH264) keepPayloads.Add(pt);

            // Pass 3: 用 currentMedia 状态机分段处理——
            // 只在 m=video 段重写 payload list + 过滤非 keep 的 a=rtpmap/fmtp/rtcp-fb；
            // m=audio / m=application 等其它段原样保留。
            // 必要性：旧实现不区分 media section，会把 audio 段的 opus rtpmap/fmtp/rtcp-fb
            // 误判为"指向非 H.264 payload"而删除，导致 mediamtx 拿不到 opus codec 描述 → 协商失败/音频静音。
            var sb = new StringBuilder();
            string currentMedia = null;  // null = session-level header；进入 m= 行后切换
            foreach (var line in lines)
            {
                if (line.StartsWith("m="))
                {
                    var parts = line.Split(' ');
                    // m=<media> <port> <proto> <fmt...>；parts[0] = "m=video" 等
                    currentMedia = parts[0].Length > 2 ? parts[0].Substring(2) : null;

                    if (currentMedia == "video")
                    {
                        // 仅 video 行需要按 keepPayloads 裁剪 payload list
                        var kept = new List<string>();
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (i < 3) { kept.Add(parts[i]); continue; }  // m=video 9 UDP/...
                            if (keepPayloads.Contains(parts[i])) kept.Add(parts[i]);
                        }
                        sb.Append(string.Join(" ", kept));
                        sb.Append("\r\n");
                        continue;
                    }
                    sb.Append(line);
                    sb.Append("\r\n");
                    continue;
                }

                // a=rtpmap / a=fmtp / a=rtcp-fb 过滤：只在 video section 内生效，
                // 否则会误伤 audio section 中 opus 等动态 PT 的属性行
                if (currentMedia == "video")
                {
                    var attrPt = Regex.Match(line, @"^a=(?:rtpmap|fmtp|rtcp-fb):(\d+)");
                    if (attrPt.Success && !keepPayloads.Contains(attrPt.Groups[1].Value))
                    {
                        continue;
                    }
                }

                sb.Append(line);
                sb.Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>在第一个 m=video 行之后插入 b=AS:{kbps}（视频带宽上限）。重复调用幂等。kbps&lt;=0 跳过不动 SDP。</summary>
        public static string SetBandwidth(string sdp, int kbps)
        {
            if (string.IsNullOrEmpty(sdp)) return sdp;
            if (kbps <= 0) return sdp;  // 关闭带宽限制：不插入 b=AS 行，让 BWE 自适应

            // 已有 b=AS:N 则替换；否则在第一个 m=video 之后插入
            var existing = new Regex(@"^b=AS:\d+", RegexOptions.Multiline);
            if (existing.IsMatch(sdp))
            {
                return existing.Replace(sdp, $"b=AS:{kbps}", 1);
            }

            var lines = SplitSdpLines(sdp);
            var sb = new StringBuilder();
            bool inserted = false;
            foreach (var line in lines)
            {
                sb.Append(line);
                sb.Append("\r\n");
                if (!inserted && line.StartsWith("m=video"))
                {
                    sb.Append($"b=AS:{kbps}\r\n");
                    inserted = true;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 校验 ForceH264Only 之后的 SDP 仍是有效的：
        /// 1) 存在 m=video 行；
        /// 2) m=video payload 列表非空（munge 后至少留下一个 codec payload）；
        /// 3) 至少存在一条 a=rtpmap:&lt;PT&gt; 指向 m=video 上声明的 payload。
        /// 任一条件不满足 → 返回 false。
        /// 用途：ForceH264Only 若把所有 payload 删空（比如 com.unity.webrtc 偏门构建只输出 VP8 + RTX，
        /// 但 ContainsH264 的预检由于 a=rtpmap 含 "H264" 字串误判通过），后续 SetLocalDescription
        /// 必报错。提前在客户端拦截，给出明确的 CodecNegotiationFailed 错误码。
        /// </summary>
        public static bool ValidateVideoPayload(string sdp)
        {
            if (string.IsNullOrEmpty(sdp)) return false;

            // m=video <port> <proto> <payloads...>，payloads 必须非空
            var mLine = Regex.Match(sdp, @"^m=video\s+\d+\s+\S+(.*)$", RegexOptions.Multiline);
            if (!mLine.Success) return false;
            var payloadStr = mLine.Groups[1].Value.Trim();
            if (string.IsNullOrEmpty(payloadStr)) return false;

            var payloads = payloadStr.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (payloads.Length == 0) return false;

            // 至少有一条 a=rtpmap:<pt> 对应 m=video 声明的 payload
            var payloadSet = new HashSet<string>(payloads);
            var rtpmapRe = new Regex(@"^a=rtpmap:(\d+)\s+", RegexOptions.Multiline);
            foreach (Match rm in rtpmapRe.Matches(sdp))
            {
                if (payloadSet.Contains(rm.Groups[1].Value)) return true;
            }
            return false;
        }

        /// <summary>
        /// 兼容 CRLF / LF 行尾并过滤空行（含 Split 末尾产生的空字符串）。
        /// 必须过滤空行，否则 sb 重组后会出现 "\r\n\r\n"（空 SDP 行），
        /// com.unity.webrtc 的 SetLocalDescription 会报 "Invalid SDP line"。
        /// </summary>
        private static string[] SplitSdpLines(string sdp)
        {
            var normalized = sdp.Replace("\r\n", "\n");
            return normalized.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
