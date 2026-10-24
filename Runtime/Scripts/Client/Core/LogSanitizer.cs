using System;
using System.Text.RegularExpressions;

namespace MyVerseXRSDK
{
    internal static class LogSanitizer
    {
        private const string Hidden = "[已隐藏]";
        private static readonly Regex Address = Pattern(@"\b(?:https?|wss?)://[^\s<>""']+");
        private static readonly Regex Credentials = Pattern(@"([""']?(?:authorization|proxy-authorization|token|access[_-]?token|refresh[_-]?token|password|passwd|secret|api[_-]?key|credential)[""']?\s*[:=]\s*)(?:(?:Bearer|Basic)\s+[^\s,;}]+|""[^""]*""|'[^']*'|[^\s,;}]+)");

        internal static string Clean(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            // 先限制输入工作量；地址整体隐藏，避免路径段或未命名查询参数携带令牌。
            if (message.Length > 4096) message = message.Substring(0, 4096) + "…[已截断]";
            try
            {
                message = Address.Replace(message, Hidden);
                return Credentials.Replace(message, match => match.Groups[1].Value + Hidden);
            }
            catch (RegexMatchTimeoutException) { return "[日志内容已隐藏：脱敏超时]"; }
        }

        private static Regex Pattern(string pattern) => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50));
    }
}
