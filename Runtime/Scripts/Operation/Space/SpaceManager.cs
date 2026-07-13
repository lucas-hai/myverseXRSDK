using Google.Protobuf;
using UnityEngine;
using System.Collections.Generic;

namespace MyVerseXRSDK
{
    /// <summary>接线层：装配 Store + 两个表现层 Module；监听 WS/LOGIN 把数据喂给 Store。</summary>
    internal static class SpaceManager
    {
        private static SpaceStateStore           m_Store;
        private static SpaceObstaclesModule      m_SpaceObstacles;
        private static SpatialAlternationModule  m_SpatialAlternation;
        private static bool                      s_Initialized;

        public static void Start()
        {
            m_Store              = new SpaceStateStore();
            m_SpatialAlternation = new SpatialAlternationModule(m_Store);
            m_SpaceObstacles     = new SpaceObstaclesModule(m_Store);
        }

        public static void InitSDK()
        {
            if (s_Initialized) return;
            AddEvent();
            m_SpaceObstacles.InitSDK();
            m_SpatialAlternation.InitSDK();
            s_Initialized = true;
        }

        public static void UnInitSDK()
        {
            if (!s_Initialized) return;
            RemoveEvent();
            m_SpaceObstacles.UnInitSDK();
            m_SpatialAlternation.UnInitSDK();
            m_Store.Clear();
            s_Initialized = false;
        }

        // ====== Scene Root Node 转发（保持对外 API 不变）======

        public static void RegisterSceneRootNode(Transform node)        => m_SpatialAlternation.RegisterSceneRootNode(node);
        public static void UnRegisterSceneRootNode(Transform node)      => m_SpatialAlternation.UnRegisterSceneRootNode(node);
        public static void UnRegisterAllSceneRootNodes()                => m_SpatialAlternation.UnRegisterAllSceneRootNodes();
        internal static IReadOnlyList<Transform> GetRegisteredRootNodes() => m_SpatialAlternation.GetRegisteredRootNodes();

        // ====== WS / Login 接线 ======

        private static void AddEvent()
        {
            SocketSystem.RegisterMessage(MessageType.SC_GAME_SCENE_PUSH, OnScenePush);
            SocketSystem.RegisterMessage(MessageType.SC_REGION_INFO_PUSH, OnRegionPush);
            EventSystem.AddEventListener(MVXRSDKEventType.LOGIN_SUCCESS, OnLoginSuccess);
        }

        private static void RemoveEvent()
        {
            SocketSystem.CancelMessage(MessageType.SC_GAME_SCENE_PUSH);
            SocketSystem.CancelMessage(MessageType.SC_REGION_INFO_PUSH);
            EventSystem.RemoveEventListener(MVXRSDKEventType.LOGIN_SUCCESS, OnLoginSuccess);
        }

        /// <summary>
        /// 应用 Region 全量快照。登录链路（RoomManager 转发 Login.Response.regionInfo）与
        /// WS 推送（OnRegionPush）共用入口；push/regionInfo 无效时由 Store 忽略并告警。
        /// </summary>
        internal static void ApplyRegionSnapshot(RegionInfoPush push)
        {
            if (!s_Initialized) return;
            m_Store.ApplyRegionPush(push);
        }

        /// <summary>最近一次 Region 位姿计算结果（MVXRSDK.TryGetRegionPose 的实现）。</summary>
        internal static bool TryGetRegionPose(out Vector3 position, out Vector3 eulerAngles)
        {
            if (!s_Initialized || m_SpatialAlternation == null)
            {
                position = default;
                eulerAngles = default;
                return false;
            }
            return m_SpatialAlternation.TryGetLastComputedPose(out position, out eulerAngles);
        }


        private static void OnLoginSuccess()
        {
            if (!s_Initialized) return;
            var req = new QueryGameSceneInfo.Types.Request();
            SocketSystem.SendMessage(MessageType.CS_QUERY_GAME_SCENE_INFO, req.ToByteString(), OnQueryResp);

            void OnQueryResp(int errorCode, byte[] buffer)
            {
                if (errorCode != 0) { MVXRSDKLog.Error($"QueryGameSceneInfo errorCode:{errorCode}"); return; }
                if (!SocketSystem.TryParse<QueryGameSceneInfo.Types.Response>(buffer, out var resp, "Space.QueryGameSceneResp")) return;
                m_Store.ApplyPush(resp.GameSceneInfo);
            }
        }

        private static void OnScenePush(int errorCode, byte[] buffer)
        {
            if (!s_Initialized) return;
            if (errorCode != 0) { MVXRSDKLog.Error($"GameScenePush errorCode:{errorCode}"); return; }
            if (!SocketSystem.TryParse<GameScenePush>(buffer, out var push, "Space.GameScenePush")) return;
            m_Store.ApplyPush(push);
        }

        private static void OnRegionPush(int errorCode, byte[] buffer)
        {
            if (!s_Initialized) return;
            if (errorCode != 0) { MVXRSDKLog.Error($"RegionInfoPush errorCode:{errorCode}"); return; }
            if (!SocketSystem.TryParse<RegionInfoPush>(buffer, out var push, "Space.RegionInfoPush")) return;
            m_Store.ApplyRegionPush(push);
        }
    }
}
