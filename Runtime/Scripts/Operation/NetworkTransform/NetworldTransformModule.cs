using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 表现层：根据 Store 数据 + XR Offset Node 注册状态，维护远端玩家 GO 生命周期。
    /// 不变量：m_RoleMap.Count &gt; 0 ⟹ MVXRSDK.XROffsetNode != null
    /// - XR Node 未注册：Store 数据更新只缓存（在 Store 内），本模块不创建 GO
    /// - XR Node 注册：清空旧 GO（应对热替换），按 Store 快照回放
    /// - XR Node 注销：清空所有 GO，保留 Store 快照
    /// - 远 / 近：以 Self ↔ Role XZ 平面距离判定，远则回收 GO，近则创建/同步
    /// </summary>
    internal class NetworldTransformModule
    {
        private readonly NetworkTransformStateStore m_Store;
        private readonly Dictionary<string, GameObject> m_RoleMap = new Dictionary<string, GameObject>();

        public NetworldTransformModule(NetworkTransformStateStore store)
        {
            m_Store = store;
        }

        public void InitSDK()
        {
            m_Store.OnRoleUpserted += OnRoleUpserted;
            m_Store.OnRoleRemoved  += OnRoleRemoved;
            MVXRSDK.OnXROffsetNodeRegistered   += OnXROffsetNodeRegistered;
            MVXRSDK.OnXROffsetNodeUnregistered += OnXROffsetNodeUnregistered;
        }

        public void UnInitSDK()
        {
            m_Store.OnRoleUpserted -= OnRoleUpserted;
            m_Store.OnRoleRemoved  -= OnRoleRemoved;
            MVXRSDK.OnXROffsetNodeRegistered   -= OnXROffsetNodeRegistered;
            MVXRSDK.OnXROffsetNodeUnregistered -= OnXROffsetNodeUnregistered;
            ReleaseAllRoles();
        }

        // ====== 事件入口 ======

        private void OnRoleUpserted(RoleSnapshot snap)
        {
            var parent = MVXRSDK.XROffsetNode;
            if (parent == null)
            {
                // XR 节点未注册：不创建 GO，数据已在 Store；等待 OnXROffsetNodeRegistered 回放
                MVXRSDKLog.Info($"OnRoleUpserted: XR 节点未注册，跳过角色 {snap.Id} 的 GO 同步（数据已缓存）");
                return;
            }
            ApplyToScene(snap, parent);
        }

        private void OnRoleRemoved(string id)
        {
            if (m_RoleMap.TryGetValue(id, out var go))
            {
                ResSystem.PushGameObjectInPool(go);
                m_RoleMap.Remove(id);
            }
        }

        private void OnXROffsetNodeRegistered(Transform newNode)
        {
            // 热替换或首次注册：清空旧 GO（旧 parent 已失效），按最新快照重建
            ReleaseAllRoles();
            foreach (var kv in m_Store.Snapshots)
            {
                ApplyToScene(kv.Value, newNode);
            }
        }

        private void OnXROffsetNodeUnregistered()
        {
            ReleaseAllRoles();
        }

        // ====== 核心：单个 snapshot 落地到场景 ======

        private void ApplyToScene(RoleSnapshot snap, Transform parent)
        {
            // 显示距离：同房间虚影用外部可配距离（NetworkTransformManager.SameRoomDistance，默认 2m）；
            // 其他房间虚影固定 2m（MVXRSDKConfig.NORMAL_DISTANCE）。
            float displayDist = (snap.RoomId == RoomManager.RoomId)
                ? NetworkTransformManager.SameRoomDistance
                : MVXRSDKConfig.NORMAL_DISTANCE;

            // Self 参考为空时退化为"全部认为较近"——避免 NRE，但同时记一条日志便于排查
            var selfRef = MVXRSDK.SelfTransform;
            bool isNear = true;
            if (selfRef != null)
            {
                Vector2 posXZ = new Vector2(snap.Position.x, snap.Position.z);
                Vector2 refXZ = new Vector2(selfRef.localPosition.x, selfRef.localPosition.z);
                float sqrDist = (posXZ - refXZ).sqrMagnitude;
                isNear = sqrDist <= displayDist * displayDist;
            }
            else
            {
                MVXRSDKLog.Warning("NetworldTransformModule.ApplyToScene: SelfTransform 未就绪，跳过距离判定");
            }

            m_RoleMap.TryGetValue(snap.Id, out var roleGo);

            if (isNear)
            {
                if (roleGo == null)
                {
                    roleGo = AcquireRole(snap.ModeId, parent);
                    if (roleGo == null)
                    {
                        MVXRSDKLog.Warning("创建角色失败，资源未找到或加载失败: {0}", snap.Id);
                        return;
                    }
                    m_RoleMap[snap.Id] = roleGo;
                }
                if (!roleGo.activeSelf) roleGo.SetActive(true);
                SyncRoleTransform(roleGo, snap.Position, snap.Rotation, displayDist);
            }
            else
            {
                // 较远：若有角色则回收（保留快照）；无角色不创建
                if (roleGo != null)
                {
                    ResSystem.PushGameObjectInPool(roleGo);
                    m_RoleMap.Remove(snap.Id);
                }
            }
        }

        private static void SyncRoleTransform(GameObject roleGo, Vector3 position, Vector3 rotEulerAngles, float displayDistance)
        {
            var nwt = roleGo.GetComponent<NetworkTransform>();
            if (nwt == null) nwt = roleGo.AddComponent<NetworkTransform>();
            // 角色作为接收者：仅接受外部数据，不进行上报
            nwt.SetRole(NetworkTransform.MessageType.Receiver);
            nwt.SetDisplayDistance(displayDistance);  // 同房间/其他房间显示距离由上层决定
            nwt.SmoothMove(position, rotEulerAngles);
        }

        private static GameObject AcquireRole(string modeId, Transform parent)
        {
            // 此处 parent 已被调用方保证非空；不再额外做 XROffsetNode 警告
            return ResSystem.InstantiateGameObject(MVXRSDKConfig.CHARACTER_PREFAB_PATH, parent, MVXRSDKConfig.KEYNAME_CHARACTER);
        }

        private void ReleaseAllRoles()
        {
            if (m_RoleMap.Count == 0) return;
            // 收集后释放，避免遍历过程修改字典
            var roles = new List<GameObject>(m_RoleMap.Values);
            foreach (var go in roles)
            {
                if (go != null) ResSystem.PushGameObjectInPool(go);
            }
            m_RoleMap.Clear();
        }
    }
}
