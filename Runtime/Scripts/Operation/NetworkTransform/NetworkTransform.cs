using System.Collections;
using System.Collections.Generic;
using Google.Protobuf;
using UnityEngine;

namespace MyVerseXRSDK
{
    public class NetworkTransform : MonoBehaviour
    {
        // 角色类型：上报者 / 接收者
        // 约束：上报者不接受数据，接收者不上报数据

        private NetworkAnimation anim;
        private Renderer meshRenderer;

        void Awake()
        {

            if (messageType == MessageType.Receiver)
            {
                anim = GetComponent<NetworkAnimation>();
                if (anim == null)
                {
                    anim = gameObject.AddComponent<NetworkAnimation>();
                }

                meshRenderer = GetComponentInChildren<Renderer>(true);
            }

        }


        public enum MessageType
        {

            None,

            Reporter, // 上报者：负责上报本地位姿，不处理外部数据

            Receiver  // 接收者：负责接收并应用外部位姿，不进行上报
        }

        [Header("消息类型")]
        [SerializeField] internal MessageType messageType = MessageType.None; // 默认作为接收者
                                                                              // 平滑移动与旋转的运行参数

        private float positionLerpSpeed = 10f;   // 位置插值速度（越大越快接近目标）
        private float rotationLerpSpeed = 10f;   // 旋转插值速度（越大越快接近目标）
        private float positionSnapDistance = 0.001f; // 位置吸附阈值（米），比较时使用距离平方
        private float rotationSnapAngle = 0.5f;      // 旋转吸附阈值（角度足够小直接对齐）

        // 定时上报参数（上传功能暂不实现，仅提供占位与调度）

        private float uploadInterval = 0.2f;       // 上报时间间隔（秒）
        private float uploadPosThresholdXZ = 0.001f; // 位置变化阈值（米，仅比较 XZ 平面，使用距离平方）
        private float uploadRotThresholdY = 0.5f;   // 角度变化阈值（度，仅比较 Y 轴角度）
        private float _nextUploadTime = 0f;
        private Vector3 _lastUploadedLocalPos;                      // 上次上报的本地位置（仅使用 XZ）
        private float _lastUploadedLocalRotY;                       // 上次上报的本地欧拉角 Y 值（度）

        // 目标驱动插值：将平滑处理搬到 Update 中，避免调用频率不稳定导致的卡顿
        private bool _hasTargetPos = false;         // 是否存在目标位置
        private Vector3 _targetPosition;            // 目标位置（本地坐标）
        private bool _hasTargetRot = false;         // 是否存在目标旋转
        private Quaternion _targetRotation;         // 目标旋转（本地坐标）


        private Vector3 _uploadPosXZ;
        private Vector3 _uploadEulerY;
        private Vector2 rolePos;
        private Vector2 m_XRPos;
        private float m_NextDistanceCheckTime = 0f;
        // 该虚影的可见距离阈值（米）：其他房间固定 2m；同房间由外部传参（默认 2m）。创建/同步时由表现层 SetDisplayDistance 设置。
        private float m_DisplayDistance = MVXRSDKConfig.NORMAL_DISTANCE;


        void OnUpdate()
        {
            // 仅接收者执行插值；上报者不处理外部数据
            if (messageType == MessageType.Receiver)
            { // 位置插值与吸附
                if (_hasTargetPos)
                {
                    float pt = Mathf.Clamp01(positionLerpSpeed * Time.deltaTime);
                    transform.localPosition = Vector3.Lerp(transform.localPosition, _targetPosition, pt);

                    float sqrDist = (transform.localPosition - _targetPosition).sqrMagnitude;
                    float sqrThreshold = positionSnapDistance * positionSnapDistance;
                    if (sqrDist <= sqrThreshold)
                    {
                        transform.localPosition = _targetPosition;
                        _hasTargetPos = false; // 对齐后停止插值，直到下一次设置目标
                    }
                }

                // 旋转插值与吸附
                if (_hasTargetRot)
                {
                    float rt = Mathf.Clamp01(rotationLerpSpeed * Time.deltaTime);
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, _targetRotation, rt);

                    if (Quaternion.Angle(transform.localRotation, _targetRotation) <= rotationSnapAngle)
                    {
                        transform.localRotation = _targetRotation;
                        _hasTargetRot = false; // 对齐后停止插值，直到下一次设置目标
                    }
                }

                if (_hasTargetPos || _hasTargetRot)
                {
                    anim.PlayMoveClip();
                }
                else
                {
                    anim.PlayIdleClip();
                }

                if (Time.time >= m_NextDistanceCheckTime)
                {
                    DistanceDetection();
                    m_NextDistanceCheckTime = Time.time + 1f;
                }
            }
            else if (messageType == MessageType.Reporter)
            {
                if (Time.time < _nextUploadTime) return;

                Vector3 localPos = transform.localPosition;
                Vector3 localEuler = transform.localEulerAngles;

                float dx = localPos.x - _lastUploadedLocalPos.x;
                float dz = localPos.z - _lastUploadedLocalPos.z;
                float sqrPlanar = dx * dx + dz * dz;
                float sqrPosThreshold = uploadPosThresholdXZ * uploadPosThresholdXZ;

                float deltaY = Mathf.Abs(Mathf.DeltaAngle(_lastUploadedLocalRotY, localEuler.y));

                bool posChanged = sqrPlanar >= sqrPosThreshold;
                bool rotChanged = deltaY >= uploadRotThresholdY;

                if (posChanged || rotChanged)
                {
                    if (posChanged) _lastUploadedLocalPos = localPos;
                    if (rotChanged) _lastUploadedLocalRotY = localEuler.y;

                    _uploadPosXZ.Set(localPos.x, 0f, localPos.z);
                    _uploadEulerY.Set(0f, localEuler.y, 0f);

                    UploadTransform(_uploadPosXZ, _uploadEulerY);
                }

                _nextUploadTime = Time.time + uploadInterval;
            }

        }


        private void DistanceDetection()
        {


            if (meshRenderer == null) return;
            // SelfTransform 未注册 / 已随 XR 销毁时无参考点，跳过本次可见性判定（对齐 SpaceObstacles）
            var self = MVXRSDK.SelfTransform;
            if (self == null) return;
            rolePos.x = transform.localPosition.x;
            rolePos.y = transform.localPosition.z;
            m_XRPos.x = self.localPosition.x;
            m_XRPos.y = self.localPosition.z;
            float dx = rolePos.x - m_XRPos.x;
            float dy = rolePos.y - m_XRPos.y;
            float distSqr = dx * dx + dy * dy;
            float thresholdSqr = m_DisplayDistance * m_DisplayDistance;
            if (distSqr > thresholdSqr)
            {
                meshRenderer.enabled = false;
            }
            else
            {
                meshRenderer.enabled = true;
            }

        }
        /// <summary>
        /// 兼容旧调用：同时平滑移动与旋转（包装调用）
        /// </summary>
        public void SmoothMove(Vector3 targetPosition, Vector3 targetEulerAngles)
        {
            // 角色限制：上报者不接受数据（直接返回）
            if (messageType != MessageType.Receiver) return;
            float sqrDist = (transform.localPosition - targetPosition).sqrMagnitude;
            float sqrThreshold = positionSnapDistance * positionSnapDistance;
            if (sqrDist > sqrThreshold)
            {
                _targetPosition = targetPosition;
                _hasTargetPos = true;
            }

            Quaternion targetRot = Quaternion.Euler(targetEulerAngles);
            float deltaAngle = Quaternion.Angle(transform.localRotation, targetRot);
            if (deltaAngle > rotationSnapAngle)
            {
                _targetRotation = targetRot;
                _hasTargetRot = true;
            }
        }

        //
        // 定时上报（占位实现）：
        // - 开关：autoUpload 控制自动启动
        // - 时间间隔：uploadInterval 控制上报频率
        // - 上报内容：世界空间下的 position 与 eulerAngles

        private void OnEnable()
        {
            MonoSystem.AddUpdateListener(OnUpdate);
            _lastUploadedLocalPos = transform.localPosition;
            _lastUploadedLocalRotY = transform.localEulerAngles.y;
        }

        private void OnDisable()
        {
            MonoSystem.RemoveUpdateListener(OnUpdate);
        }

        private void OnDestroy()
        {
            MonoSystem.RemoveUpdateListener(OnUpdate);
        }


        /// <summary>
        /// 手动启动定时上报（若已启动则忽略）
        /// </summary>
        public void StartUpload()
        {
            // 角色限制：接收者不上报数据（直接返回）
            if (messageType != MessageType.Reporter) return;

        }




        /// <summary>
        /// 实际上传逻辑 
        /// </summary>
        private void UploadTransform(Vector3 position, Vector3 eulerAngles)
        {
            // 仅在房间已分配时上传（None/Undistributed 阶段网络帧无意义）
            if (MVXRSDK.RoomAllocationStatus != RoomAllocationStatus.Allocated) return;

            // 兜底：DeviceId 为空说明 SN 未取到就 Init 了。Protobuf 字符串字段禁 null，
            // 直接赋值会抛 ArgumentNullException —— 这里拦下并打错误日志，提示按文档先取 SN 再 Init
            if (string.IsNullOrEmpty(MVXRSDK.DeviceId))
            {
                MVXRSDKLog.Error("位置上传中止：DeviceId(SN) 为空。请先成功获取 PICO SN 码再调用 InitMVXRSDK");
                return;
            }

            var Position = new SynPosition.Types.DevicePosition()
            {
                DeviceId = MVXRSDK.DeviceId,
                RoleModeId = "10001",//sdk中应该是采用通用模型，这里写死兼容老版本
                X = position.x,
                Y = 0,
                Z = position.z,
            };

            var RoomId = RoomManager.RoomId;

            var Rotation = new SynPosition.Types.DeviceRotation()
            {

                X = 0,
                Y = eulerAngles.y,
                Z = 0,

            };
            SynPosition.Types.Request request = new SynPosition.Types.Request();
            request.Position = Position;
            request.RoomId = RoomId;
            request.Rotation = Rotation;

            SocketSystem.SendMessage(MyVerseXRSDK.MessageType.CS_UPDATEDE_USER_POSITION, request.ToByteString());
        }

        /// <summary>
        /// 设置角色并按角色自动管理上报协程
        /// Reporter：若启用 autoUpload，则启动上报；Receiver：停止上报。
        /// </summary>
        public void SetRole(MessageType type)
        {
            if (messageType == type) return;
            messageType = type;

        }

        /// <summary>设置该虚影的可见距离阈值（米，仅 Receiver 的可见性判定使用）。&lt;=0 回退默认 2m。</summary>
        internal void SetDisplayDistance(float meters)
        {
            m_DisplayDistance = meters > 0f ? meters : MVXRSDKConfig.NORMAL_DISTANCE;
        }



    }
}
