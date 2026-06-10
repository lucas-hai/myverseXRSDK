using System;

namespace MyVerseXRSDK
{
    /// <summary>
    /// MVXRSDK Facade 的对外事件 + 内部 Raise 入口。
    /// 拆分自原 MVXRSDK.cs（v2 整理），无行为变化；仅作可读性/可维护性收敛。
    /// </summary>
    public static partial class MVXRSDK
    {
        // ============================== 积分扣除 ==============================

        /// <summary>
        /// 积分验证结果事件（**仅由中控房间系统主动推送时触发**）。
        ///
        /// 使用约定（接入方二选一，不要同时使用）：
        /// - 接入中控房间系统：仅订阅本事件，由中控启动游戏时 SDK 自动触发
        /// - 不接入中控、自助验证：仅调用 <see cref="TransactionVerification(Action{bool})"/> 并使用其回调
        ///
        /// 同时订阅本事件 + 调用 TransactionVerification(cb) 不会重复扣费（两条路径独立），
        /// 但容易导致业务侧重复处理。商业接入请二选一。
        /// </summary>
        public static event Action<bool> OnTransactionVerification;

        /// <summary>仅供 SDK 内部触发；外部代码请用 `+=`/`-=` 订阅事件。</summary>
        internal static void RaiseTransactionVerification(bool ok)
        {
            OnTransactionVerification?.Invoke(ok);
        }

        /// <summary>清空 OnTransactionVerification 的所有订阅（仅 UnInit 调用）。</summary>
        /// <remarks>event 字段在其声明类内部允许直接赋空清空所有订阅。</remarks>
        private static void ClearTransactionVerificationSubscribers()
        {
            OnTransactionVerification = null;
        }

        // ============================== 推流 / 录屏 ==============================

        /// <summary>推流开始。参数：streamServerIp（来自 NotifyLive.StreamServerIp）。</summary>
        public static event Action<string> OnPushStreamStarted;

        /// <summary>推流停止。参数：停止原因（v2 替换旧 bool active 的歧义参数）。</summary>
        public static event Action<StreamStopReason> OnPushStreamStopped;

        /// <summary>推流失败。参数：统一错误码（4xxx 段）、错误信息。</summary>
        public static event Action<MVXRSDKErrorCode, string> OnPushStreamFailed;

        /// <summary>推流实时统计（默认每 1s 触发一次，间隔可由 StreamConfig.StatsReportIntervalMs 配置）。</summary>
        public static event Action<StreamStats> OnPushStreamStats;

        /// <summary>录屏请求结果回调。参数：错误码（Ok=0 表成功）、错误信息（成功时为空字符串）。</summary>
        public static event Action<MVXRSDKErrorCode, string> OnRecordResult;

        /// <summary>
        /// 中控仲裁结果透传（含本机和其它客户端）。SDK 仅做 pb→基本类型翻译，不做编排。
        /// 业务侧（典型挂 MVXRStreamRig 旁的脚本）订阅本事件，判 deviceId==<see cref="DeviceId"/>
        /// 决定是否调 rig.SwitchCameraTemporary 等切镜行为。
        /// 参数：deviceId、isPrimary（是否主位）、slot（槽位下标）、durationSec（持续秒数）。
        /// </summary>
        public static event Action<string, bool, int, int> OnDirectorSelected;

        /// <summary>
        /// SDK 接受推流指令、开始建立会话时触发（早于 OnPushStreamStarted 约 1~2s 的握手期）。
        /// NotifyLive(start) 即"本机被选中"的信号——业务应在此回调中 SetStreamSource 接相机，
        /// 使首帧即有画面；没被选中就不接源（避免相机白渲染）。SDK 内部自动重试不重复触发。
        /// 参数：streamServerIp。
        /// </summary>
        public static event Action<string> OnPushStreamStarting;

        /// <summary>
        /// 中控对 DirectorInsert 请求的应答。success 仅表示请求被受理，
        /// 是否真的被选中推流仍以 NotifyLive（即 OnPushStreamStarting）为准。
        /// 参数校验失败 / WS 未连接 / 应答错误 / Response.success=false 都触发 false。
        /// </summary>
        public static event Action<bool> OnDirectorRequestResult;

        /// <summary>
        /// 全局错误聚合事件（v2）。任何域的失败回调都会同步广播一份到这里，便于
        /// 业务侧统一接监控/上报（不再需要分别订阅 OnPushStreamFailed / OnRecordResult 等）。
        /// 参数：错误码、错误信息、来源模块名（"Stream" / "Record" / "Socket" / "Room" 等）。
        /// </summary>
        public static event Action<MVXRSDKErrorCode, string, string> OnError;

        internal static void RaisePushStreamStarted(string streamServerIp) => OnPushStreamStarted?.Invoke(streamServerIp);
        internal static void RaisePushStreamStopped(StreamStopReason reason) => OnPushStreamStopped?.Invoke(reason);
        internal static void RaisePushStreamFailed(MVXRSDKErrorCode code, string msg)
        {
            OnPushStreamFailed?.Invoke(code, msg);
            RaiseError(code, msg, "Stream");
        }
        internal static void RaisePushStreamStats(StreamStats stats) => OnPushStreamStats?.Invoke(stats);
        internal static void RaiseRecordResult(MVXRSDKErrorCode code, string errorMsg)
        {
            OnRecordResult?.Invoke(code, errorMsg);
            if (code != MVXRSDKErrorCode.Ok) RaiseError(code, errorMsg, "Record");
        }
        internal static void RaiseDirectorSelected(string deviceId, bool isPrimary, int slot, int durationSec) =>
            OnDirectorSelected?.Invoke(deviceId, isPrimary, slot, durationSec);
        internal static void RaisePushStreamStarting(string streamServerIp) => OnPushStreamStarting?.Invoke(streamServerIp);
        internal static void RaiseDirectorRequestResult(bool success) => OnDirectorRequestResult?.Invoke(success);

        /// <summary>SDK 内部任何失败路径都可调本方法上报到全局 OnError，便于业务统一监控。</summary>
        internal static void RaiseError(MVXRSDKErrorCode code, string msg, string source)
            => OnError?.Invoke(code, msg, source);

        /// <remarks>event 字段在其声明类内部允许直接赋空清空所有订阅。</remarks>
        private static void ClearStreamEventSubscribers()
        {
            OnPushStreamStarted = null;
            OnPushStreamStopped = null;
            OnPushStreamFailed = null;
            OnPushStreamStats = null;
            OnRecordResult = null;
            OnDirectorSelected = null;
            OnPushStreamStarting = null;
            OnDirectorRequestResult = null;
            OnError = null;
        }
    }
}
