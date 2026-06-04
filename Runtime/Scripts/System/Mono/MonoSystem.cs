using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 整个游戏只有一个 Update / LateUpdate / FixedUpdate 派发中心。
    /// 由 <see cref="MVXRSDK.EnsureBootstrap"/> 在 RuntimeInitializeOnLoad(BeforeSceneLoad) 阶段
    /// 通过 AddComponent 挂到 MVXRSDKManager GameObject 上；Awake 自绑定 static instance，
    /// 因此场景 Awake 阶段（早于 InitMVXRSDK）调用监听 API 也能拿到 instance。
    /// </summary>
    internal class MonoSystem : MonoBehaviour
    {
        private static MonoSystem instance;
        private Action updateEvent;
        private Action lateUpdateEvent;
        private Action fixedUpdateEvent;

        private void Awake()
        {
            // Unity 在 AddComponent 立即调 Awake；MVXRSDK.EnsureBootstrap 完成那一刻 instance 就绑好了
            if (instance == null) instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>
        /// SDK UnInit 末尾调用：只停 SDK 自家协程；Update / LateUpdate / FixedUpdate 监听**不动**——
        /// 它们的所有权属于订阅方：SDK 模块在自己的 UnInitSDK 显式 Remove*Listener；
        /// 外部 MonoBehaviour（如玩家相机上的 NetworkTransform / SpaceObstacles）走 OnEnable/OnDisable 对子。
        /// 强行清空会吞掉跨 Init/UnInit 循环的长生命周期外部订阅。
        /// </summary>
        internal static void ResetForUnInit()
        {
            if (instance == null) return;
            instance.StopAllCoroutines();
            instance.coroutineDic.Clear();
        }

        #region 生命周期函数

        // 监听 API 的 `if (instance == null) return` 是极端时序保护——理论上 Awake 已绑定，
        // 但 OnDestroy 触发后到对象真销毁还有一段时间，此期间静态字段为 null，调用应静默返回。

        public static void AddUpdateListener(Action action)
        {
            if (instance == null) return;
            instance.updateEvent += action;
        }

        public static void RemoveUpdateListener(Action action)
        {
            if (instance == null) return;
            instance.updateEvent -= action;
        }

        public static void AddLateUpdateListener(Action action)
        {
            if (instance == null) return;
            instance.lateUpdateEvent += action;
        }

        public static void RemoveLateUpdateListener(Action action)
        {
            if (instance == null) return;
            instance.lateUpdateEvent -= action;
        }

        public static void AddFixedUpdateListener(Action action)
        {
            if (instance == null) return;
            instance.fixedUpdateEvent += action;
        }

        public static void RemoveFixedUpdateListener(Action action)
        {
            if (instance == null) return;
            instance.fixedUpdateEvent -= action;
        }

        private void Update()
        {
            updateEvent?.Invoke();
        }
        private void LateUpdate()
        {
            lateUpdateEvent?.Invoke();
        }
        private void FixedUpdate()
        {
            fixedUpdateEvent?.Invoke();
        }

        #endregion

        #region 协程
        private Dictionary<object, List<Coroutine>> coroutineDic = new Dictionary<object, List<Coroutine>>();
        private static ObjectPoolModule poolModule = new ObjectPoolModule();

        /// <summary>启动一个协程序</summary>
        public static Coroutine Start_Coroutine(IEnumerator coroutine)
        {
            if (instance == null) return null;
            return instance.StartCoroutine(coroutine);
        }

        /// <summary>启动一个协程序并且绑定某个对象</summary>
        public static Coroutine Start_Coroutine(object obj, IEnumerator coroutine)
        {
            if (instance == null) return null;
            Coroutine _coroutine = instance.StartCoroutine(coroutine);
            if (!instance.coroutineDic.TryGetValue(obj, out List<Coroutine> coroutineList))
            {
                coroutineList = poolModule.GetObject<List<Coroutine>>();
                if (coroutineList == null) coroutineList = new List<Coroutine>();
                instance.coroutineDic.Add(obj, coroutineList);
            }
            coroutineList.Add(_coroutine);
            return _coroutine;
        }

        /// <summary>停止一个协程序并基于某个对象</summary>
        public static void Stop_Coroutine(object obj, Coroutine routine)
        {
            if (instance == null) return;
            if (instance.coroutineDic.TryGetValue(obj, out List<Coroutine> coroutineList))
            {
                instance.StopCoroutine(routine);
                coroutineList.Remove(routine);
            }
        }

        /// <summary>停止一个协程序</summary>
        public static void Stop_Coroutine(Coroutine routine)
        {
            if (instance == null) return;
            instance.StopCoroutine(routine);
        }

        /// <summary>停止某个对象的全部协程</summary>
        public static void StopAllCoroutine(object obj)
        {
            if (instance == null) return;
            if (instance.coroutineDic.Remove(obj, out List<Coroutine> coroutineList))
            {
                for (int i = 0; i < coroutineList.Count; i++)
                {
                    instance.StopCoroutine(coroutineList[i]);
                }
                coroutineList.Clear();
                poolModule.PushObject(coroutineList);
            }
        }

        /// <summary>整个系统全部协程都会停止</summary>
        public static void StopAllCoroutine()
        {
            if (instance == null) return;
            foreach (List<Coroutine> item in instance.coroutineDic.Values)
            {
                item.Clear();
                poolModule.PushObject(item);
            }
            instance.coroutineDic.Clear();
            instance.StopAllCoroutines();
        }
        #endregion
    }
}
