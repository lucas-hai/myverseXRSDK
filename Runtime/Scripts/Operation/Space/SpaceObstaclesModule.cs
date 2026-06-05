using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal class ObstacleData
    {
        public GameSceneData data;
        public GameObject obstacleGO;
    }

    /// <summary>表现层：根据 Store 数据 + XR Offset Node 注册状态，维护障碍物 GO 生命周期。</summary>
    /// <remarks>
    /// 不变量：m_ObstacleDict.Count &gt; 0 ⟹ MVXRSDK.XROffsetNode != null
    /// 当 XR Node 未注册时，Store 数据变更只更新缓存（在 Store 内），本模块不创建 GO；
    /// XR Node 注册时按最新 Store 全量构建；XR Node 注销时清空所有 GO。
    /// </remarks>
    internal class SpaceObstaclesModule
    {
        private readonly SpaceStateStore m_Store;
        private readonly Dictionary<string, ObstacleData> m_ObstacleDict = new();

        public SpaceObstaclesModule(SpaceStateStore store)
        {
            m_Store = store;
        }

        public void InitSDK()
        {
            m_Store.OnObstaclesChanged += OnObstaclesChanged;
            MVXRSDK.OnXROffsetNodeRegistered   += OnXROffsetNodeRegistered;
            MVXRSDK.OnXROffsetNodeUnregistered += OnXROffsetNodeUnregistered;
        }

        public void UnInitSDK()
        {
            m_Store.OnObstaclesChanged -= OnObstaclesChanged;
            MVXRSDK.OnXROffsetNodeRegistered   -= OnXROffsetNodeRegistered;
            MVXRSDK.OnXROffsetNodeUnregistered -= OnXROffsetNodeUnregistered;
            ClearAll();
        }

        // ====== 事件入口 ======

        private void OnObstaclesChanged(IReadOnlyList<GameSceneData> obstacles)
        {
            var parent = MVXRSDK.XROffsetNode;
            if (parent == null)
            {
                // XR 节点未注册：不创建 GO，数据已在 Store；等待 OnXROffsetNodeRegistered 回放
                MVXRSDKLog.Info("OnObstaclesChanged: XR 节点未注册，跳过 GO 构建（数据已缓存）");
                return;
            }
            Reconcile(obstacles, parent);
        }

        private void OnXROffsetNodeRegistered(Transform newNode)
        {
            // 热替换或首次注册：清空旧 GO，再按最新缓存重建
            ClearAll();
            if (m_Store.LatestObstacles == null) return;
            Reconcile(m_Store.LatestObstacles, newNode);
        }

        private void OnXROffsetNodeUnregistered()
        {
            ClearAll();
        }

        // ====== 核心 reconcile：现有 diff 逻辑搬迁 ======

        private void Reconcile(IReadOnlyList<GameSceneData> obstacles, Transform parent)
        {
            if (obstacles == null) return;

            var serverCount = obstacles.Count;
            var clientCount = m_ObstacleDict.Count;
            if (serverCount > clientCount)      AddObstacle(obstacles, parent);
            else if (serverCount < clientCount) RemoveObstacle(obstacles);
            else                                 UpdateObstacle(obstacles, parent);
        }

        private void AddObstacle(IReadOnlyList<GameSceneData> obstacles, Transform parent)
        {
            foreach (var obstacle in obstacles)
            {
                if (m_ObstacleDict.TryGetValue(obstacle.Id, out _)) continue;

                var data = ResSystem.GetOrNew<ObstacleData>();
                data.data = obstacle;
                data.obstacleGO = CreateObstacle(obstacle, parent);
                if (data.obstacleGO == null)
                {
                    MVXRSDKLog.Error($"AddObstacle: 创建障碍物 {obstacle.Id} 失败");
                    ResSystem.PushObjectInPool(data);
                    continue;
                }
                m_ObstacleDict.Add(obstacle.Id, data);
            }
        }

        private void RemoveObstacle(IReadOnlyList<GameSceneData> obstacles)
        {
            var serverIds = new HashSet<string>();
            foreach (var o in obstacles) serverIds.Add(o.Id);

            var toRemove = new List<string>();
            foreach (var key in m_ObstacleDict.Keys)
                if (!serverIds.Contains(key)) toRemove.Add(key);

            foreach (var key in toRemove)
            {
                if (!m_ObstacleDict.TryGetValue(key, out var data)) continue;
                ResSystem.PushGameObjectInPool(GetObstacleKeyName((ObstacleType)data.data.ModuleId), data.obstacleGO);
                ResSystem.PushObjectInPool(data);
                m_ObstacleDict.Remove(key);
            }
        }

        private void UpdateObstacle(IReadOnlyList<GameSceneData> obstacles, Transform parent)
        {
            foreach (var obstacle in obstacles)
            {
                if (!m_ObstacleDict.TryGetValue(obstacle.Id, out var data)) continue;

                if (obstacle.ModuleId != data.data.ModuleId)
                {
                    // 类型变更：回收旧 GO，按新类型重建
                    ResSystem.PushGameObjectInPool(GetObstacleKeyName((ObstacleType)data.data.ModuleId), data.obstacleGO);
                    data.obstacleGO = CreateObstacle(obstacle, parent);
                    if (data.obstacleGO == null)
                    {
                        MVXRSDKLog.Error($"UpdateObstacle: 重建障碍物 {obstacle.Id} 失败");
                        continue;
                    }
                }
                else
                {
                    var scale = ResolveScale(obstacle);
                    data.obstacleGO.transform.localPosition = new Vector3(obstacle.Position.X, obstacle.Position.Y, obstacle.Position.Z);
                    data.obstacleGO.transform.localScale = scale;
                }
                data.data = obstacle;
            }
        }

        private GameObject CreateObstacle(GameSceneData obstacle, Transform parent)
        {
            var type = (ObstacleType)obstacle.ModuleId;
            GameObject go;
            switch (type)
            {
                case ObstacleType.Oval:
                    go = ResSystem.InstantiateGameObject(MVXRSDKConfig.OBSTACLE_PREFAB_OVAL_PATH, parent, MVXRSDKConfig.KEYNAME_OBSTACLE_OVAL);
                    break;
                case ObstacleType.Rect:
                    go = ResSystem.InstantiateGameObject(MVXRSDKConfig.OBSTACLE_PREFAB_RECT_PATH, parent, MVXRSDKConfig.KEYNAME_OBSTACLE_RECT);
                    break;
                default:
                    MVXRSDKLog.Error($"CreateObstacle: 未知障碍物类型 {type}");
                    return null;
            }
            if (go == null) { MVXRSDKLog.Error($"CreateObstacle: 实例化失败 {obstacle.Id}"); return null; }

            var sob = go.GetComponent<SpaceObstacles>();
            if (sob == null) { MVXRSDKLog.Error($"CreateObstacle: 障碍物 {obstacle.Id} 缺少 SpaceObstacles 组件"); return null; }
            sob.SetObstacleInfo(type, obstacle.Radius, obstacle.Len, obstacle.Width);

            go.transform.localPosition = new Vector3(obstacle.Position.X, obstacle.Position.Y, obstacle.Position.Z);
            go.transform.localScale = ResolveScale(obstacle);
            return go;
        }

        private static Vector3 ResolveScale(GameSceneData o)
        {
            switch ((ObstacleType)o.ModuleId)
            {
                case ObstacleType.Oval: return new Vector3(o.Radius, 3, o.Radius);
                case ObstacleType.Rect: return new Vector3(o.Len,    3, o.Width);
                default:                return Vector3.one;
            }
        }

        private static string GetObstacleKeyName(ObstacleType type)
        {
            switch (type)
            {
                case ObstacleType.Oval: return MVXRSDKConfig.KEYNAME_OBSTACLE_OVAL;
                case ObstacleType.Rect: return MVXRSDKConfig.KEYNAME_OBSTACLE_RECT;
                default:
                    MVXRSDKLog.Error($"GetObstacleKeyName: 未知障碍物类型 {type}");
                    return null;
            }
        }

        private void ClearAll()
        {
            foreach (var kv in m_ObstacleDict)
            {
                // GO 可能已随 XR 子树销毁，判空避免把已销毁对象入池（对齐 ReleaseAllRoles）
                if (kv.Value.obstacleGO != null)
                    ResSystem.PushGameObjectInPool(GetObstacleKeyName((ObstacleType)kv.Value.data.ModuleId), kv.Value.obstacleGO);
                ResSystem.PushObjectInPool(kv.Value);
            }
            m_ObstacleDict.Clear();
        }
    }
}
