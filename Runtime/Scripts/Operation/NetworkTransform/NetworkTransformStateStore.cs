using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>远端玩家位姿快照。Store 保存的最小数据单元，与 Unity GO 解耦。</summary>
    internal class RoleSnapshot
    {
        public string  Id;
        public string  ModeId;
        public Vector3 Position;
        public Vector3 Rotation;
    }

    /// <summary>
    /// 远端玩家位姿快照存储。持有最新一次收到的每个 deviceId 的位姿数据，
    /// 与 Unity GameObject/Transform 解耦，表现层未就绪时（如 XR Node 未注册）独立工作。
    /// 表现层订阅事件 + 在节点注册时主动读取 Snapshots 做回放。
    /// </summary>
    internal class NetworkTransformStateStore
    {
        private readonly Dictionary<string, RoleSnapshot> m_Snapshots = new();

        /// <summary>当前所有角色快照（只读）。表现层在 XR Node 注册时遍历此集合回放。</summary>
        public IReadOnlyDictionary<string, RoleSnapshot> Snapshots => m_Snapshots;

        /// <summary>新增或更新角色快照触发。</summary>
        public event Action<RoleSnapshot> OnRoleUpserted;

        /// <summary>角色快照被移除时触发（退房间 / 主动移除）。</summary>
        public event Action<string> OnRoleRemoved;

        public void ApplyRole(string id, string modeId, Vector3 position, Vector3 rotation)
        {
            if (string.IsNullOrEmpty(id))
            {
                MVXRSDKLog.Warning("NetworkTransformStateStore.ApplyRole: id 为空，忽略");
                return;
            }

            if (!m_Snapshots.TryGetValue(id, out var snap))
            {
                snap = new RoleSnapshot();
                m_Snapshots[id] = snap;
            }
            snap.Id       = id;
            snap.ModeId   = modeId;
            snap.Position = position;
            snap.Rotation = rotation;

            SafeInvoke(OnRoleUpserted, snap, nameof(OnRoleUpserted));
        }

        public bool RemoveRole(string id)
        {
            if (!m_Snapshots.Remove(id)) return false;
            SafeInvokeId(OnRoleRemoved, id, nameof(OnRoleRemoved));
            return true;
        }

        /// <summary>清空全部快照并广播移除事件。用于房间解散等场景，让表现层同步释放 GO。</summary>
        public void ClearAndBroadcast()
        {
            if (m_Snapshots.Count == 0) return;
            // 复制 keys，避免广播中订阅者再次操作字典
            var ids = new List<string>(m_Snapshots.Keys);
            m_Snapshots.Clear();
            foreach (var id in ids)
            {
                SafeInvokeId(OnRoleRemoved, id, nameof(OnRoleRemoved));
            }
        }

        /// <summary>UnInit 专用：清空快照 + 清空订阅者。避免保留旧 Module 实例引用。</summary>
        public void Dispose()
        {
            m_Snapshots.Clear();
            OnRoleUpserted = null;
            OnRoleRemoved  = null;
        }

        private static void SafeInvoke(Action<RoleSnapshot> evt, RoleSnapshot arg, string name)
        {
            if (evt == null) return;
            foreach (var handler in evt.GetInvocationList())
            {
                try { ((Action<RoleSnapshot>)handler)(arg); }
                catch (Exception e) { MVXRSDKLog.Error($"NetworkTransformStateStore.{name} 订阅者异常: {e}"); }
            }
        }

        private static void SafeInvokeId(Action<string> evt, string arg, string name)
        {
            if (evt == null) return;
            foreach (var handler in evt.GetInvocationList())
            {
                try { ((Action<string>)handler)(arg); }
                catch (Exception e) { MVXRSDKLog.Error($"NetworkTransformStateStore.{name} 订阅者异常: {e}"); }
            }
        }
    }
}
