using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 空间场景数据快照存储。持有最近一次 GameScenePush 的数据，
    /// 与 Unity GameObject/Transform 解耦，可在表现层未就绪时独立工作。
    /// 表现层订阅事件 + 在初始化/节点注册时主动读取 Latest* 做回放。
    /// </summary>
    internal class SpaceStateStore
    {
        public IReadOnlyList<GameSceneData> LatestObstacles { get; private set; }
        public RegionSnapshot? LatestRegion { get; private set; }

        public event Action<IReadOnlyList<GameSceneData>> OnObstaclesChanged;
        public event Action<RegionSnapshot>               OnRegionChanged;

        public void ApplyPush(GameScenePush push)
        {
            if (push == null)
            {
                MVXRSDKLog.Warning("SpaceStateStore.ApplyPush: push 为 null，忽略");
                return;
            }

            // 仅消费障碍物列表；Offset/Rotation 的旧根节点偏移功能已废弃——
            // 后端为老版本客户端保留字段，新版客户端不解析（根节点唯一驱动源为 Region 体系）
            var obstacles = new List<GameSceneData>(push.SceneData);
            LatestObstacles = obstacles;
            SafeInvoke(OnObstaclesChanged, obstacles, nameof(OnObstaclesChanged));
        }

        /// <summary>
        /// 应用 Region 全量快照（登录响应 regionInfo 字段 / RegionInfoPush 推送共用入口）。
        /// push 或 regionInfo 为 null 视为无效忽略（未接入 Region 体系的房间不下发有效数据）；
        /// gameInfo 及各向量字段缺省归零（proto3 全量快照约定，不做增量合并）。
        /// </summary>
        public void ApplyRegionPush(RegionInfoPush push)
        {
            if (push == null || push.RegionInfo == null)
            {
                MVXRSDKLog.Warning("SpaceStateStore.ApplyRegionPush: push 或 regionInfo 为 null，忽略");
                return;
            }

            var region = push.RegionInfo;
            var center         = region.Center         == null ? Vector3.zero : new Vector3(region.Center.X, region.Center.Y, region.Center.Z);
            var offset         = region.Offset         == null ? Vector3.zero : new Vector3(region.Offset.X, region.Offset.Y, region.Offset.Z);
            var offsetRotation = region.OffsetRotation == null ? Vector3.zero : new Vector3(region.OffsetRotation.X, region.OffsetRotation.Y, region.OffsetRotation.Z);
            var rotation       = region.Rotation       == null ? Vector3.zero : new Vector3(region.Rotation.X, region.Rotation.Y, region.Rotation.Z);

            var gameOffset         = Vector3.zero;
            var gameOffsetRotation = Vector3.zero;
            var game = push.GameInfo;
            if (game != null)
            {
                if (game.GameOffset != null)
                    gameOffset = new Vector3(game.GameOffset.X, game.GameOffset.Y, game.GameOffset.Z);
                if (game.GameOffsetRotation != null)
                    gameOffsetRotation = new Vector3(game.GameOffsetRotation.X, game.GameOffsetRotation.Y, game.GameOffsetRotation.Z);
            }

            var snapshot = new RegionSnapshot(region.Len, region.Width, center, offset, offsetRotation,
                                              rotation, gameOffset, gameOffsetRotation);
            LatestRegion = snapshot;
            SafeInvokeRegion(OnRegionChanged, snapshot, nameof(OnRegionChanged));
        }

        public void Clear()
        {
            LatestObstacles = null;
            LatestRegion    = null;
            // 同时清空订阅者：UnInit 时表现层已销毁，保留订阅会泄漏旧实例引用
            OnObstaclesChanged = null;
            OnRegionChanged    = null;
        }

        private static void SafeInvoke(Action<IReadOnlyList<GameSceneData>> evt, IReadOnlyList<GameSceneData> arg, string name)
        {
            if (evt == null) return;
            foreach (var handler in evt.GetInvocationList())
            {
                try { ((Action<IReadOnlyList<GameSceneData>>)handler)(arg); }
                catch (Exception e) { MVXRSDKLog.Error($"SpaceStateStore.{name} 订阅者异常: {e}"); }
            }
        }

        private static void SafeInvokeRegion(Action<RegionSnapshot> evt, RegionSnapshot arg, string name)
        {
            if (evt == null) return;
            foreach (var handler in evt.GetInvocationList())
            {
                try { ((Action<RegionSnapshot>)handler)(arg); }
                catch (Exception e) { MVXRSDKLog.Error($"SpaceStateStore.{name} 订阅者异常: {e}"); }
            }
        }
    }
}
