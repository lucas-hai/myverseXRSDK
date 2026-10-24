using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>SDK虚影的被动表现组件，仅接受所属设备服务的R帧位姿与Tick，不注册全局更新或上报。</summary>
    internal sealed class NetworkTransform : MonoBehaviour
    {
        private NetworkAnimation animationView;
        private Pose target;
        private bool moving;
        internal GameObject Object => gameObject;

        internal void InitializePose(Pose initial)
        {
            animationView = GetComponent<NetworkAnimation>();
            animationView?.Initialize();
            target = initial;
            moving = false;
            transform.SetLocalPositionAndRotation(initial.position, initial.rotation);
            gameObject.SetActive(true);
            animationView?.PlayIdleClip();
        }

        internal void SetTarget(Pose pose) => target = pose;

        internal void Tick(float deltaTime)
        {
            bool nextMoving = (transform.localPosition - target.position).sqrMagnitude > 0.000001f ||
                              Quaternion.Angle(transform.localRotation, target.rotation) > 0.1f;
            if (nextMoving)
            {
                float factor = 1f - Mathf.Exp(-12f * Mathf.Max(0, deltaTime));
                transform.localPosition = Vector3.Lerp(transform.localPosition, target.position, factor);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, target.rotation, factor);
            }
            else transform.SetLocalPositionAndRotation(target.position, target.rotation);
            if (animationView == null || nextMoving == moving) return;
            moving = nextMoving;
            if (moving) animationView.PlayMoveClip();
            else animationView.PlayIdleClip();
        }
    }
}
