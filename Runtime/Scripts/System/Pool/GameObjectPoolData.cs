using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// GameObject对象池数据
    /// </summary>
    public class GameObjectPoolData
    {
        #region GameObjectPoolData持有的数据及初始化方法
        // 这一层物体的 父节点
        public Transform RootTransform;
        // 对象容器
        public Queue<GameObject> PoolQueue;
        // 容量限制 -1代表无限
        public int maxCapacity = -1;
        public GameObjectPoolData(int capacity = -1)
        {
            if (capacity == -1)
            {
                PoolQueue = new Queue<GameObject>();
            }
            else
            {
                PoolQueue = new Queue<GameObject>(capacity);
            }

        }
        public void Init(string assetPath, Transform poolRootObj, int capacity = -1)
        {
            // 创建父节点 并设置到对象池根节点下方
            GameObject go = PoolSystem.GetGameObject(PoolSystem.PoolLayerGameObjectName, poolRootObj);
            if (go.IsNull())
            {
                go = new GameObject(PoolSystem.PoolLayerGameObjectName);
                go.transform.SetParent(poolRootObj);
            }
            RootTransform = go.transform;
            RootTransform.name = assetPath;
            maxCapacity = capacity;
        }
        #endregion

        #region GameObjectPool数据相关操作
        /// <summary>
        /// 将对象放进对象池
        /// </summary>
        public bool PushObj(GameObject obj)
        {
            // 拒绝 null / 已销毁对象入池（Unity 重载 == 能识别已销毁对象），避免毒化队列
            if (obj == null) return false;

            // 检测是不是超过容量
            if (maxCapacity != -1 && PoolQueue.Count >= maxCapacity)
            {
                GameObject.Destroy(obj);
                return false;
            }

            // 先重父级 + 失活，再入队。
            // 若父级（如正在失活/销毁的 XR 子树）拒绝重父级，Unity 只打错误日志、不抛异常，
            // 此时对象仍挂在原父级下、注定随其销毁；若仍入队会变成池中"已销毁残留项"，
            // 后续 GetObj 取出即崩。故重父级未落到 RootTransform 时放弃入池。
            obj.transform.SetParent(RootTransform);
            if (obj.transform.parent != RootTransform)
            {
                MVXRSDKLog.Warning($"PushObj: 重父级失败（父级可能正在失活/销毁），放弃入池 {obj.name}");
                return false;
            }
            obj.SetActive(false);
            PoolQueue.Enqueue(obj);
            return true;
        }

        /// <summary>
        /// 从对象池中获取对象
        /// </summary>
        /// <returns></returns>
        public GameObject GetObj(Transform parent = null)
        {
            // 跳过已被外部销毁的残留项（如随 XR 子树销毁的障碍物/角色），
            // 否则对已销毁对象调 SetActive 会在设备端 IL2CPP 抛 NRE。
            GameObject obj = null;
            while (PoolQueue.Count > 0)
            {
                var candidate = PoolQueue.Dequeue();
                if (candidate != null) { obj = candidate; break; } // Unity 重载 == 识别已销毁对象
            }
            if (obj == null) return null; // 队列耗尽且无存活对象，交由上层回退到实例化

            // 显示对象
            obj.SetActive(true);
            // 设置父物体
            obj.transform.SetParent(parent);
            if (parent == null)
            {
                // 回归默认场景
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }
            return obj;
        }

        /// <summary>
        /// 销毁层数据
        /// </summary>
        /// <param name="pushThisToPool">将对象池层级挂接点也推送进对象池</param>
        public void Desotry(bool pushThisToPool = false)
        {
            maxCapacity = -1;
            if (!pushThisToPool)
            {
                // 真实销毁 这里由于删除层级根物体 会导致下方所有对象都被删除，所以不需要单独删除PoolQueue
                GameObject.Destroy(RootTransform.gameObject);
            }
            else
            {
                // 销毁队列中的全部游戏物体
                foreach (GameObject item in PoolQueue)
                {
                    GameObject.Destroy(item);
                }

                // 扔进对象池
                RootTransform.gameObject.name = PoolSystem.PoolLayerGameObjectName;
                PoolSystem.PushGameObject(RootTransform.gameObject);
                PoolSystem.PushObject(this);
            }
            // 队列清理
            PoolQueue.Clear();
            RootTransform = null;
        }
        #endregion

    }
}