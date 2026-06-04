using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>表现层：把 Store 的 offset/rotation 应用到所有已注册 Scene Root Nodes。</summary>
    /// <remarks>
    /// Scene Root Node 不依赖 XR Offset Node，两类节点独立。
    /// 注册时机晚于推送：注册成功后立即用 Store.LatestOffset 对该新节点应用一次。
    /// </remarks>
    internal class SpatialAlternationModule
    {
        private readonly SpaceStateStore m_Store;
        private readonly List<Transform> m_SceneRootNodes = new();

        public SpatialAlternationModule(SpaceStateStore store)
        {
            m_Store = store;
        }

        public void InitSDK()
        {
            m_Store.OnOffsetChanged += OnOffsetChanged;
        }

        public void UnInitSDK()
        {
            m_Store.OnOffsetChanged -= OnOffsetChanged;
        }

        public void RegisterSceneRootNode(Transform node)
        {
            if (node == null)
            {
                MVXRSDKLog.Warning("RegisterSceneRootNode: 注册节点为 null，已忽略");
                return;
            }
            RemoveDestroyedNodes();
            if (m_SceneRootNodes.Contains(node))
            {
                MVXRSDKLog.Warning("RegisterSceneRootNode: 该节点已注册，无法重复注册");
                return;
            }
            m_SceneRootNodes.Add(node);
            MVXRSDKLog.Info($"RegisterSceneRootNode: 注册成功，当前数量 {m_SceneRootNodes.Count}");

            // 回放：若 Store 已有缓存，立即对新节点应用一次（不影响其它已注册节点）
            if (m_Store.LatestOffset.HasValue && m_Store.LatestRotation.HasValue)
            {
                node.localPosition    = m_Store.LatestOffset.Value;
                node.localEulerAngles = m_Store.LatestRotation.Value;
                MVXRSDKLog.Info($"RegisterSceneRootNode: 节点 {node.name} 已按 Store 缓存回放偏移");
            }
        }

        public void UnRegisterSceneRootNode(Transform node)
        {
            if (node == null || !m_SceneRootNodes.Remove(node))
            {
                MVXRSDKLog.Warning("UnRegisterSceneRootNode: 未找到该节点");
                return;
            }
            MVXRSDKLog.Info($"UnRegisterSceneRootNode: 注销成功，剩余数量 {m_SceneRootNodes.Count}");
        }

        public void UnRegisterAllSceneRootNodes()
        {
            m_SceneRootNodes.Clear();
            MVXRSDKLog.Info("UnRegisterAllSceneRootNodes: 已清空所有场景根节点");
        }

        public IReadOnlyList<Transform> GetRegisteredRootNodes()
        {
            RemoveDestroyedNodes();
            return m_SceneRootNodes.AsReadOnly();
        }

        private void OnOffsetChanged(Vector3 offset, Vector3 rotation)
        {
            for (int i = m_SceneRootNodes.Count - 1; i >= 0; i--)
            {
                var n = m_SceneRootNodes[i];
                if (n == null)
                {
                    MVXRSDKLog.Warning($"OnOffsetChanged: 节点已被外部销毁，自动移除 (index:{i})");
                    m_SceneRootNodes.RemoveAt(i);
                    continue;
                }
                n.localPosition    = offset;
                n.localEulerAngles = rotation;
            }
        }

        private void RemoveDestroyedNodes()
        {
            for (int i = m_SceneRootNodes.Count - 1; i >= 0; i--)
                if (m_SceneRootNodes[i] == null) m_SceneRootNodes.RemoveAt(i);
        }
    }
}
