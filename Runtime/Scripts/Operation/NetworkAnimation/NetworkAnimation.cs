using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    [RequireComponent(typeof(Animation))]
    internal sealed class NetworkAnimation : MonoBehaviour
    {
        private Animation anim;

        [SerializeField]
        private AnimationClip moveClip;
        [SerializeField]
        private AnimationClip idleClip;

        internal void Initialize()
        {
            if (anim != null) return;
            if (moveClip == null || idleClip == null)
                throw new System.InvalidOperationException("角色表现资源缺少移动或静止动画");
            anim = GetComponent<Animation>();
            if (anim == null)
            {
                anim = gameObject.AddComponent<Animation>();
            }
            moveClip.wrapMode = WrapMode.Loop;
            idleClip.wrapMode = WrapMode.Loop;
            moveClip.legacy = true;
            idleClip.legacy = true;
            anim.AddClip(idleClip, idleClip.name);
            anim.AddClip(moveClip, moveClip.name);
        }

        internal void PlayMoveClip()
        {
            anim.Play(moveClip.name);
        }

        internal void PlayIdleClip()
        {
            anim.Play(idleClip.name);
        }
    }
}
