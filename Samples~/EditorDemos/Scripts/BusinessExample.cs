using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>业务接入：准入通知和独立远端录制命令；不把请求受理当业务完成。</summary>
    public sealed class BusinessExample : MonoBehaviour
    {
        public string deviceId = "editor-business";
        [Header("中控接入（进入Play自动连接）")]
        public ControlConnectionMode connectionMode = ControlConnectionMode.ControlServer;
        public string controlServerUrl = "";
        [Header("独立远端录制命令")]
        public string recordingTargetId = "";
        public SdkClient Client { get; private set; }
        public OperationHandle LastOperation { get; private set; }

        private void Start()
        {
            if (Client == null) ConnectFromInspector();
        }
        [ContextMenu("SDK/连接中控")]
        private void ConnectFromInspector()
        {
            if (!DemoConsole.RequirePlay(this)) return;
            DemoConsole.Result(this, "启动", StartClient());
        }

        /// <summary>创建真实中控客户端并订阅准入；不启用推流，不自动录制或扣费。</summary>
        public SdkResult StartClient()
        {
            if (Client != null) return SdkResult.Fail(SdkErrorCode.Busy, "等待旧客户端释放");
            var options = new SdkOptions
            {
                DeviceId = deviceId,
                Session = new SessionOptions { Mode = connectionMode == ControlConnectionMode.Directory ? SessionConnectionMode.Directory : SessionConnectionMode.ControlServer, ControlServerUrl = controlServerUrl },
                Spatial = new SpatialOptions { Mode = SpatialMode.Disabled, ShowObstacles = false, ShowFloor = false },
                Devices = new DeviceOptions { ReportingEnabled = false, AvatarsEnabled = false },
                ShowNetworkFailureHud = false
            };
            try { Client = MVXRSDK.Create(options); }
            catch (ArgumentException) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, "检查输入配置"); }
            catch (InvalidOperationException) { return SdkResult.Fail(SdkErrorCode.Busy, "已有其他活动客户端"); }
            Client.Admission.StartRequested += OnAdmission;
            var result = Client.Start();
            if (!result.Success) Shutdown();
            return result;
        }
        private void OnAdmission(GameStartRequest request) => Debug.Log("收到准入通知：" + request.Source + "。业务自行决定开局，不自动扣费。", this);
        private bool CanSend()
        {
            // 示例只有一个取消入口，保留唯一在途句柄，拒绝后续点击覆盖它。
            if (LastOperation != null && !LastOperation.IsCompleted)
            { Debug.Log("已有命令等待结果，请先等待或取消本地等待。", this); return false; }
            if (Client != null && Client.State == SdkClientState.Running) return true;
            Debug.Log("请先启动客户端；业务命令只在显式调用时发送。", this); return false;
        }
        /// <summary>显式请求目标远端录制10秒；成功不代表文件完成，在途时返回null。</summary>
        public OperationHandle StartRecording()
        {
            if (!CanSend()) return null;
            var owner = Client;
            LastOperation = Client.Recording.Start(new RecordingRequest
            { TargetId = string.IsNullOrWhiteSpace(recordingTargetId) ? deviceId : recordingTargetId, DurationSeconds = 10 }, result => OnResult(owner, result));
            return LastOperation;
        }
        private void OnResult(SdkClient owner, OperationResult result)
        {
            if (this == null || !ReferenceEquals(Client, owner)) return;
            string message = $"请求#{result.RequestId}：{result.Result.Code}；缓存={result.WasCached}；远端结果未知={result.RemoteOutcomeUnknown}。受理不代表录制文件已生成，未知结果不自动重试。";
            if (result.Success) Debug.Log(message, this);
            else Debug.LogWarning(message, this);
        }
        /// <summary>异步释放客户端和在途本地等待；完成前保留旧客户端引用。</summary>
        [ContextMenu("SDK/关闭连接")]
        public void Shutdown()
        {
            var closing = Client;
            if (closing == null || closing.State == SdkClientState.Stopping) return;
            closing.Admission.StartRequested -= OnAdmission;
            closing.Shutdown(result => { if (ReferenceEquals(Client, closing)) Client = null; LastOperation = null; DemoConsole.Result(this, "关闭", result); });
        }
        [ContextMenu("业务/远端录制10秒")]
        private void RecordFromMenu() => StartRecording();
        /// <summary>仅取消本地等待，不撤回远端已执行的命令。</summary>
        [ContextMenu("业务/取消本地等待")]
        public void CancelWait() => LastOperation?.Cancel();

        private void OnDestroy()
        {
            if (Client != null) { Client.Admission.StartRequested -= OnAdmission; Client.Dispose(); }
        }
    }
}
