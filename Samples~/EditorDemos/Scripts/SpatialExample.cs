using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>Inspector配置后进入Play自动连接中控并绑定普通Rig；状态看Console，不提供运行时UI。</summary>
    public sealed class SpatialExample : MonoBehaviour
    {
        public Transform motionRoot, calibrationRoot, trackingRoot, head;
        public Transform leftHand, rightHand;
        [Header("中控接入（进入Play自动连接）")]
        public ControlConnectionMode connectionMode = ControlConnectionMode.ControlServer;
        [Tooltip("必须属于中控设备管理；由网页控制台开房并拉入的设备标识")]
        public string deviceId = "editor-spatial";
        [Tooltip("仅测试直连填写完整HTTP(S)中控地址；正式模式无需填写，不是房间WebSocket地址")]
        public string controlServerUrl = "";
        public SdkClient Client { get; private set; }
        private SpatialBindingHandle binding;
        private bool sameRoom;

        private void Start()
        {
            if (Client == null) ConnectFromInspector();
        }
        [ContextMenu("SDK/连接中控")]
        private void ConnectFromInspector() => LogResult("启动", StartDemo());

        /// <summary>按当前配置创建和启动；成功只代表本地运行，入房与空间校准仍等待中控。</summary>
        public SdkResult StartDemo()
        {
            if (Client != null) return SdkResult.Fail(SdkErrorCode.Busy, "请先关闭旧客户端");
            if (motionRoot == null || calibrationRoot == null || trackingRoot == null || head == null)
                return SdkResult.Fail(SdkErrorCode.InvalidBinding, "需要四个普通Transform引用");
            var options = new SdkOptions
            {
                DeviceId = deviceId,
                Session = new SessionOptions
                {
                    Mode = connectionMode == ControlConnectionMode.Directory ? SessionConnectionMode.Directory : SessionConnectionMode.ControlServer,
                    ControlServerUrl = controlServerUrl
                },
                // 不覆盖地块来源；区域、障碍物和设备数据只由中控会话接收。
                Spatial = new SpatialOptions(),
                ShowNetworkFailureHud = false
            };
            try { Client = MVXRSDK.Create(options); }
            catch (ArgumentException) { return SdkResult.Fail(SdkErrorCode.InvalidArgument, "检查设备标识与完整中控HTTP(S)地址"); }
            catch (InvalidOperationException) { return SdkResult.Fail(SdkErrorCode.Busy, "其他客户端尚未安全释放"); }
            var bound = Client.Space.BindRig(new SpatialRigBinding(motionRoot, calibrationRoot, trackingRoot, head));
            if (!bound.Success) { Client.Dispose(); Client = null; return bound.Result; }
            binding = bound.Handle;
            Client.Space.CalibrationChanged += OnCalibration;
            Client.Session.Changed += OnSessionChanged;
            var result = Client.Start();
            if (!result.Success) { Shutdown(); return result; }
            return SdkResult.Ok;
        }
        private void OnCalibration(CalibrationResult value) => Debug.Log("[空间Demo] 校准结果已更新，请检查地图、头手与SDK代理的位置。", this);
        private void OnSessionChanged(SessionSnapshot value) => Debug.Log("[空间Demo] 会话状态：" + value.State, this);
        /// <summary>模拟业务整体移动，不移动独立地图，也不改写SDK校准节点。</summary>
        [ContextMenu("空间/业务根平移1m并旋转30度")]
        public void MoveBusinessRoot() { motionRoot.position += Vector3.right; motionRoot.Rotate(0, 30, 0, Space.World); }
        /// <summary>解除节点绑定并异步关闭，释放完成前不能重新创建。</summary>
        [ContextMenu("SDK/关闭连接")]
        public void Shutdown()
        {
            var closing = Client;
            if (closing == null || closing.State == SdkClientState.Stopping) return;
            closing.Space.CalibrationChanged -= OnCalibration;
            closing.Session.Changed -= OnSessionChanged;
            binding?.Dispose(); binding = null;
            closing.Shutdown(result => { if (ReferenceEquals(Client, closing)) Client = null; LogResult("关闭", result); });
        }
        [ContextMenu("空间/模拟头部前移0.25m")]
        private void MoveHead() => head.localPosition += Vector3.forward * 0.25f;

        [ContextMenu("空间/切换同房间虚影")]
        private void ToggleSameRoom()
        {
            if (!RequireRunning()) return;
            sameRoom = !sameRoom;
            LogResult("虚影策略", Client.Devices.SetSameRoomVisibility(sameRoom, 2));
        }
        [ContextMenu("空间/查询本机房间坐标")]
        private void QueryRoomPose()
        {
            if (!RequireRunning()) return;
            if (Client.Space.TryGetRoomPose(out var pose)) Debug.Log("[空间Demo] 本机R坐标：" + pose.position.ToString("F3"), this);
            else Debug.LogWarning("[空间Demo] 当前参考帧不可用，请检查会话和校准结果。", this);
        }
        [ContextMenu("空间/撤销参考帧（非设备recenter）")]
        private void InvalidateReferenceFrame()
        {
            if (RequireRunning()) Client.Space.InvalidateTrackingOrigin();
        }

        private bool RequireRunning()
        {
            if (Client != null && Client.State == SdkClientState.Running) return true;
            Debug.LogWarning("[空间Demo] 客户端未运行。", this);
            return false;
        }
        private void LogResult(string action, SdkResult result)
        {
            if (result.Success) Debug.Log("[空间Demo] " + action + "成功", this);
            else Debug.LogError("[空间Demo] " + action + "失败：" + result.Code + "；" + result.Message, this);
        }
        private void OnDestroy()
        {
            if (Client == null) return;
            Client.Space.CalibrationChanged -= OnCalibration;
            Client.Session.Changed -= OnSessionChanged;
            binding?.Dispose(); Client.Dispose();
        }
    }
}
