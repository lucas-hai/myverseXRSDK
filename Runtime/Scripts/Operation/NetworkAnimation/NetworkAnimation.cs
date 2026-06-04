using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    [RequireComponent(typeof(Animation))]
    public class NetworkAnimation : MonoBehaviour
    {
        private Animation anim;

        [SerializeField]
        private AnimationClip moveClip;
        [SerializeField]
        private AnimationClip idleClip;

        void Awake()
        {
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

        void Start()
        {
            anim.Play(idleClip.name);
        }

        public void PlayMoveClip()
        {
            anim.Play(moveClip.name);
        }

        public void PlayIdleClip()
        {
            anim.Play(idleClip.name);
        }
    }
}
