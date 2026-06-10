using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// MVXRSDK Facade 的节点注册：XR Offset / Self（玩家相机）/ Scene Root 三类。
    /// 拆分自原 MVXRSDK.cs（v2 整理），无行为变化。
    ///
    /// v2 设计原则：任何时机皆可调用（Init 前/中/后）；SDK 内部读取这些节点的代码
    /// 都做了 null 检查，未注册 → 对应功能静默不启用，不报错。
    /// </summary>
    public static partial class MVXRSDK
    {
        // ============================== 字段 ==============================

        private static Transform m_SelfTransform;
        internal static Transform SelfTransform
        {
            // 节点被外部销毁后，把"悬挂引用"归一为真 null，避免下游误用已销毁对象
            get { if (m_SelfTransform == null) m_SelfTransform = null; return m_SelfTransform; }
        }

        private static Transform m_XROffsetNode;
        internal static Transform XROffsetNode
        {
            get { if (m_XROffsetNode == null) m_XROffsetNode = null; return m_XROffsetNode; }
        }

        // ============================== 内部事件（仅 SDK 内部模块订阅）==============================

        /// <summary>
        /// XR 偏移节点从 null → 非null，或被热替换为新节点时触发。
        /// 仅 SDK 内部模块订阅；业务方请用 RegisterXROffsetNode。
        /// 参数：新生效的节点。
        /// </summary>
        internal static event Action<Transform> OnXROffsetNodeRegistered;

        /// <summary>XR 偏移节点从非null → null 时触发。仅 SDK 内部模块订阅。</summary>
        internal static event Action OnXROffsetNodeUnregistered;

        /// <summary>
        /// 自身节点从 null → 非null，或被热替换为新节点时触发。
        /// 仅 SDK 内部模块订阅；业务方请用 RegisterSelfNode。参数：新生效的节点。
        /// </summary>
        internal static event Action<Transform> OnSelfNodeRegistered;

        /// <summary>自身节点从非null → null 时触发。仅 SDK 内部模块订阅。</summary>
        internal static event Action OnSelfNodeUnregistered;

        // ============================== Scene Root Node ==============================

        public static void RegisterRootNode(Transform root)
        {
            SpaceManager.RegisterSceneRootNode(root);
        }

        public static void UnRegisterRootNode(Transform root)
        {
            SpaceManager.UnRegisterSceneRootNode(root);
        }

        public static void UnRegisterAllRootNodes()
        {
            SpaceManager.UnRegisterAllSceneRootNodes();
        }

        // ============================== XR Offset Node ==============================

        /// <summary>
        /// 注册（或热替换）XR 偏移节点（场景空间对齐根节点）。
        ///
        /// 任何时机都可调用：
        /// - Init 前注册：业务最常见用法
        /// - Init 后注册：场景切换、延迟构造的 XR Origin 等场景；SDK 在每次收到服务端
        ///   推送时实时读取，立即生效
        /// - 重复注册不同节点：视为热替换（如更换场景根）；同一节点重复注册无副作用
        ///
        /// 设计原则：SDK 内部读取本节点的所有代码（SpaceObstacles / NetworkTransform 等）
        /// 都做了 null 检查；未注册 → 对应功能静默不启用，不报错。
        /// </summary>
        public static void RegisterXROffsetNode(Transform node)
        {
            if (node == null)
            {
                MVXRSDKLog.Error("RegisterXROffsetNode: node 为空，注册失败");
                return;
            }
            if (ReferenceEquals(m_XROffsetNode, node))
            {
                MVXRSDKLog.Debug("RegisterXROffsetNode: 同一节点重复注册，忽略");
                return;
            }
            if (m_XROffsetNode != null)
            {
                MVXRSDKLog.Info($"RegisterXROffsetNode: 热替换 XR 偏移节点 {m_XROffsetNode.name} → {node.name}");
            }
            else
            {
                MVXRSDKLog.Info($"RegisterXROffsetNode: 注册 XR 偏移节点 {node.name} 成功");
            }
            m_XROffsetNode = node;

            try { OnXROffsetNodeRegistered?.Invoke(node); }
            catch (Exception e) { MVXRSDKLog.Error($"OnXROffsetNodeRegistered 订阅者异常: {e}"); }
        }

        /// <summary>
        /// 注销 XR 偏移节点。未注册时幂等忽略（仅警告日志）。
        /// 注销会触发障碍物 / 其他玩家虚影模块回收 XR 子树下的 GO；
        /// 若自身节点（玩家相机）挂在 XR 子树下，则一并注销自身节点，清掉内部悬挂引用。
        /// 典型用于"切场景时销毁整个 XR Origin"：先调本方法回收 SDK 状态，再销毁 XR GameObject。
        /// </summary>
        public static void UnRegisterXROffsetNode()
        {
            // 用 ReferenceEquals 而非 ==null：节点"已被外部销毁但 C# 引用还在"时仍要走完注销，
            // 让障碍物/角色模块清理挂在 XR 子树下的悬挂 GO。仅"从未注册(真 null)"才幂等忽略。
            if (ReferenceEquals(m_XROffsetNode, null))
            {
                MVXRSDKLog.Warning("UnRegisterXROffsetNode:未注册XR偏移节点,无法注销");
                return;
            }

            bool xrAlive = m_XROffsetNode != null; // Unity 重载：false 表示已被销毁

            // 自身节点（玩家相机）若挂在 XR 子树下会随 XR 一同销毁，或已随之销毁——
            // 两种情况都连带注销，清掉内部悬挂引用，避免后续读到已销毁的 SelfTransform。
            // Self 独立挂载（不在 XR 下）则保留，维持 XROffset / Self 节点独立性。
            bool selfReferenced = !ReferenceEquals(m_SelfTransform, null);
            bool selfDestroyed  = selfReferenced && m_SelfTransform == null;
            if (selfReferenced && (selfDestroyed || (xrAlive && m_SelfTransform.IsChildOf(m_XROffsetNode))))
            {
                MVXRSDKLog.Info("UnRegisterXROffsetNode: 自身节点位于 XR 子树下或已随之销毁，连带注销");
                UnRegisterSelfNode();
            }

            MVXRSDKLog.Info("UnRegisterXROffsetNode:注销XR偏移节点成功");
            m_XROffsetNode = null;

            // 障碍物 / 其他玩家虚影模块订阅此事件，各自回收 XR 子树下的 GO
            try { OnXROffsetNodeUnregistered?.Invoke(); }
            catch (Exception e) { MVXRSDKLog.Error($"OnXROffsetNodeUnregistered 订阅者异常: {e}"); }
        }

        // ============================== Self Node（玩家相机）==============================

        /// <summary>
        /// 注册（或热替换）自身节点（通常为玩家相机 Transform）。
        ///
        /// 任何时机都可调用（Init 前/中/后），SDK 实时读取最新值：
        /// - 重复注册不同节点 → 热替换（停止旧节点上的 Reporter，挂载新节点）
        /// - 同一节点重复注册 → 幂等忽略
        /// - 未注册时 SDK 内部读取该节点的代码（NetworkFailureHUD / SpaceObstacles / NetworldTransformModule）
        ///   都做了 null 检查 → 对应功能静默不启用，不报错
        ///
        /// 设计原则：v2 不再依赖 `Camera.main` 自动抓取，避免测试场景 / 多相机 / 延迟构造场景下抓不到主相机的意外。
        /// 业务侧自己持有相机引用，主动注册更可靠。
        /// </summary>
        public static void RegisterSelfNode(Transform node)
        {
            if (node == null)
            {
                MVXRSDKLog.Error("RegisterSelfNode: node 为空，注册失败");
                return;
            }
            if (ReferenceEquals(m_SelfTransform, node))
            {
                MVXRSDKLog.Debug("RegisterSelfNode: 同一节点重复注册，忽略");
                return;
            }
            if (m_SelfTransform != null)
            {
                // 热替换：停止旧节点上的 Reporter 上报
                StopReporterOn(m_SelfTransform);
                MVXRSDKLog.Info($"RegisterSelfNode: 热替换自身节点 {m_SelfTransform.name} → {node.name}");
            }
            else
            {
                MVXRSDKLog.Info($"RegisterSelfNode: 注册自身节点 {node.name} 成功");
            }
            m_SelfTransform = node;
            StartReporterOn(node);

            try { OnSelfNodeRegistered?.Invoke(node); }
            catch (Exception e) { MVXRSDKLog.Error($"OnSelfNodeRegistered 订阅者异常: {e}"); }
        }

        /// <summary>
        /// 注销自身节点。未注册时调用幂等忽略（仅警告日志）。
        /// 注销后停止本机位姿上报，已注册节点上的 NetworkTransform 组件保留（messageType 置 None）。
        /// </summary>
        public static void UnRegisterSelfNode()
        {
            // ReferenceEquals：已被外部销毁但引用还在时也要清理（StopReporterOn 内部已判空安全）
            if (ReferenceEquals(m_SelfTransform, null))
            {
                MVXRSDKLog.Warning("UnRegisterSelfNode: 未注册自身节点，无法注销");
                return;
            }
            StopReporterOn(m_SelfTransform);
            MVXRSDKLog.Info("UnRegisterSelfNode: 注销自身节点成功");
            m_SelfTransform = null;

            try { OnSelfNodeUnregistered?.Invoke(); }
            catch (Exception e) { MVXRSDKLog.Error($"OnSelfNodeUnregistered 订阅者异常: {e}"); }
        }

        // 给指定节点挂载（或复用）NetworkTransform 并切到 Reporter 角色
        private static void StartReporterOn(Transform node)
        {
            var nt = node.GetComponent<NetworkTransform>();
            if (nt == null) nt = node.gameObject.AddComponent<NetworkTransform>();
            nt.SetRole(NetworkTransform.MessageType.Reporter);
        }

        // 停掉指定节点上的 Reporter（保留组件，仅切到 None 让 OnUpdate 空转）
        private static void StopReporterOn(Transform node)
        {
            if (node == null) return;
            var nt = node.GetComponent<NetworkTransform>();
            if (nt != null) nt.SetRole(NetworkTransform.MessageType.None);
        }
    }
}
