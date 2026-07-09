using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>表现层：把 Region 快照按区域公式应用到所有已注册 Scene Root Nodes。</summary>
    /// <remarks>
    /// 场景根节点的唯一驱动源为 Region 体系（OnRegionChanged）。旧 GameScenePush 的 Offset/Rotation
    /// 根节点偏移功能已废弃——后端为老版本客户端保留字段，新版客户端不消费（注册接口不变）。
    /// Scene Root Node 不依赖 XR Offset Node，两类节点独立。
    /// 注册时机晚于推送：注册成功后立即用最近一次 Region 计算结果对该新节点回放。
    /// </remarks>
    internal class SpatialAlternationModule
    {
        private readonly SpaceStateStore m_Store;
        private readonly List<Transform> m_SceneRootNodes = new();

        // ------ Region 匹配与计算缓存 ------
        private string m_LastRuntimeId;              // 上次匹配用的临时 id，变化才重查地片
        private LocalRegionData m_MatchedTile;       // 当前匹配的地片条目
        private LocalRegionDataList m_DataList;      // 地片列表缓存（Resources 只加载一次）
        private bool m_DataListLoadAttempted;
        private Vector3? m_LastComputedPos;          // 最近一次计算结果（晚注册回放用）
        private Vector3 m_LastComputedRot;

        /// <summary>测试注入点：覆盖地片列表加载方式（默认 Resources.Load 固定契约路径）。</summary>
        internal Func<LocalRegionDataList> LoadDataListOverride;

        public SpatialAlternationModule(SpaceStateStore store)
        {
            m_Store = store;
        }

        public void InitSDK()
        {
            m_Store.OnRegionChanged += OnRegionChanged;
        }

        public void UnInitSDK()
        {
            m_Store.OnRegionChanged -= OnRegionChanged;
            m_LastRuntimeId = null;
            m_MatchedTile = null;
            m_DataList = null;
            m_DataListLoadAttempted = false;
            m_LastComputedPos = null;
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

            // 回放：已有 Region 计算结果时立即对新节点应用一次（不影响其它已注册节点）
            if (m_LastComputedPos.HasValue)
            {
                MVXRSDKLog.Info($"场景根节点 [{node.name}] 应用缓存 Region 位姿: " +
                                $"pos {node.localPosition.ToString("F3")} → {m_LastComputedPos.Value.ToString("F3")}, " +
                                $"rot {node.localEulerAngles.ToString("F3")} → {m_LastComputedRot.ToString("F3")}");
                node.localPosition    = m_LastComputedPos.Value;
                node.localEulerAngles = m_LastComputedRot;
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

        // ------ Region 快照 → 地片匹配 → 区域公式 ------

        private void OnRegionChanged(RegionSnapshot snapshot)
        {
            string runtimeId = RegionIdUtil.MakeRuntimeId(snapshot.Len, snapshot.Width);
            // 仅登录首帧或长宽（临时 id）变化时重查地片；纯 gameOffset 变更复用缓存
            if (m_MatchedTile == null || !string.Equals(runtimeId, m_LastRuntimeId, StringComparison.Ordinal))
            {
                m_LastRuntimeId = runtimeId;
                m_MatchedTile = FindTile(runtimeId);
            }
            if (m_MatchedTile == null)
            {
                m_LastComputedPos = null;
                var list = GetDataList();
                MVXRSDKLog.Warning($"Region 快照无匹配地片：远端长宽 ({snapshot.Len}, {snapshot.Width}) → 临时 id [{runtimeId}]，" +
                                   $"本地条目数 {(list == null ? 0 : list.entries.Count)}，场景根节点保持原位");
                return;
            }

            var (position, eulerAngles) = RegionAlignmentCalculator.Compute(snapshot, m_MatchedTile);
            m_LastComputedPos = position;
            m_LastComputedRot = eulerAngles;
            ApplyToAll(position, eulerAngles, $"Region[{runtimeId}]");
        }

        private LocalRegionDataList GetDataList()
        {
            if (!m_DataListLoadAttempted)
            {
                m_DataListLoadAttempted = true;
                m_DataList = LoadDataListOverride != null
                    ? LoadDataListOverride()
                    : Resources.Load<LocalRegionDataList>(LocalRegionDataList.ResourcesLoadPath);
                if (m_DataList == null)
                    MVXRSDKLog.Warning($"本地区域数据列表加载失败：Resources/{LocalRegionDataList.ResourcesLoadPath}" +
                                       "（工程未提供或路径不符），Region 对齐不生效");
            }
            return m_DataList;
        }

        private LocalRegionData FindTile(string runtimeId)
        {
            var list = GetDataList();
            return list == null ? null : list.FindById(runtimeId);
        }

        private void ApplyToAll(Vector3 position, Vector3 eulerAngles, string sourceTag)
        {
            for (int i = m_SceneRootNodes.Count - 1; i >= 0; i--)
            {
                var n = m_SceneRootNodes[i];
                if (n == null)
                {
                    MVXRSDKLog.Warning($"ApplyToAll: 节点已被外部销毁，自动移除 (index:{i})");
                    m_SceneRootNodes.RemoveAt(i);
                    continue;
                }
                // 打印该场景根节点位置更新前 → 更新后的坐标
                MVXRSDKLog.Info($"场景根节点 [{n.name}] 位置更新({sourceTag}): " +
                                $"pos {n.localPosition.ToString("F3")} → {position.ToString("F3")}, " +
                                $"rot {n.localEulerAngles.ToString("F3")} → {eulerAngles.ToString("F3")}");
                n.localPosition    = position;
                n.localEulerAngles = eulerAngles;
            }
        }

        private void RemoveDestroyedNodes()
        {
            for (int i = m_SceneRootNodes.Count - 1; i >= 0; i--)
                if (m_SceneRootNodes[i] == null) m_SceneRootNodes.RemoveAt(i);
        }
    }
}
