using UnityEngine;

//
// 简易轻量日志系统（仅控制打印输出，不做持久化保存）
// 使用说明：
// 1) 在任意代码处直接调用：
//    MVXRSDKLog.Debug("初始化完成");
//    MVXRSDKLog.Info("进入房间: {0}", roomId);
//    MVXRSDKLog.Warning("网络延迟: {0}ms", latency);
//    MVXRSDKLog.Error("加载失败: {0}", errorMessage);
// 2) 运行时控制：
//    MVXRSDKLog.SetEnabled(true/false);            // 全局启用/关闭日志
//    MVXRSDKLog.SetMinLevel(MVXRSDKLog.Level.Info); // 设置最低输出级别
//    MVXRSDKLog.SetTag("Gameplay");               // 设置统一标签前缀
//    MVXRSDKLog.SetPrefixProvider(() => "Login"); // 设置动态前缀（例如模块名）
// 3) 编译期完全关闭：
//    在 Player Settings 的 Scripting Define Symbols 添加：MVXRSDK_LOG_DISABLED
//    之后所有日志调用将被编译为不输出（零开销）。

namespace MyVerseXRSDK
{
    internal static class MVXRSDKLog
    {
        // 日志等级（从低到高），可通过 SetMinLevel 控制最低输出等级
        public enum Level
        {
            Debug = 0,
            Info = 1,
            Warning = 2,
            Error = 3,
            None = 4
        }

        // 是否启用日志输出（运行时可切换）
        private static bool _enabled = true;
        // 最低输出等级（低于该等级的日志不打印）
        private static Level _minLevel = Level.Debug;
        // 统一日志标签前缀
        private static string _tag = "MVXRSDK";
        // 动态前缀提供者（例如返回当前模块名/场景名）
        private static System.Func<string> _prefixProvider = null;

        // 设置是否启用日志
        public static void SetEnabled(bool value) { _enabled = value; }

        // 设置最低输出等级
        public static void SetMinLevel(Level value) { _minLevel = value; }

        // 设置统一标签前缀（可为空字符串）
        public static void SetTag(string value) { _tag = value ?? string.Empty; }

        // 设置动态前缀提供者（可用于输出模块名等附加信息）
        public static void SetPrefixProvider(System.Func<string> provider) { _prefixProvider = provider; }

#if MVXRSDK_LOG_DISABLED
    // 编译期禁用：直接返回不输出
    private static bool ShouldLog(Level level) => false;
#else
        // 运行时判断是否输出
        private static bool ShouldLog(Level level) => _enabled && level >= _minLevel;
#endif

        // 组装最终输出内容
        private static string ComposeMessage(Level level, string msg)
        {
            var prefix = _prefixProvider != null ? _prefixProvider() : null;
            if (!string.IsNullOrEmpty(prefix))
            {
                return $"[{_tag}][{level}][{prefix}] {msg}";
            }
            return $"[{_tag}][{level}] {msg}";
        }

        // Debug 等级输出
        public static void Debug(object message)
        {
            if (!ShouldLog(Level.Debug)) return;
            UnityEngine.Debug.Log(ComposeMessage(Level.Debug, message?.ToString()));
        }

        // Info 等级输出
        public static void Info(object message)
        {
            if (!ShouldLog(Level.Info)) return;
            UnityEngine.Debug.Log(ComposeMessage(Level.Info, message?.ToString()));
        }

        // Warning 等级输出
        public static void Warning(object message)
        {
            if (!ShouldLog(Level.Warning)) return;
            UnityEngine.Debug.LogWarning(ComposeMessage(Level.Warning, message?.ToString()));
        }

        // Error 等级输出
        public static void Error(object message)
        {
            if (!ShouldLog(Level.Error)) return;
            UnityEngine.Debug.LogError(ComposeMessage(Level.Error, message?.ToString()));
        }

        // 支持格式化字符串重载（string.Format）——级别短路前置，避免被过滤后仍执行 string.Format + params 数组分配
        public static void Debug(string format, params object[] args)
        {
            if (!ShouldLog(Level.Debug)) return;
            UnityEngine.Debug.Log(ComposeMessage(Level.Debug, string.Format(format, args)));
        }
        public static void Info(string format, params object[] args)
        {
            if (!ShouldLog(Level.Info)) return;
            UnityEngine.Debug.Log(ComposeMessage(Level.Info, string.Format(format, args)));
        }
        public static void Warning(string format, params object[] args)
        {
            if (!ShouldLog(Level.Warning)) return;
            UnityEngine.Debug.LogWarning(ComposeMessage(Level.Warning, string.Format(format, args)));
        }
        public static void Error(string format, params object[] args)
        {
            if (!ShouldLog(Level.Error)) return;
            UnityEngine.Debug.LogError(ComposeMessage(Level.Error, string.Format(format, args)));
        }

        /// <summary>是否启用某级别日志输出（用于业务侧/Diagnostics 自检）。</summary>
        public static bool IsLevelEnabled(Level level) => ShouldLog(level);

        // 简易断言：条件不满足时按 Error 级别输出
        public static void Assert(bool condition, object message)
        {
            if (condition) return;
            if (!ShouldLog(Level.Error)) return;
            UnityEngine.Debug.LogError(ComposeMessage(Level.Error, $"断言失败: {message}"));
        }
    }
}
