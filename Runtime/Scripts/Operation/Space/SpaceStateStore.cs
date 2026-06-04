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
        public Vector3? LatestOffset   { get; private set; }
        public Vector3? LatestRotation { get; private set; }

        public event Action<IReadOnlyList<GameSceneData>> OnObstaclesChanged;
        public event Action<Vector3, Vector3>             OnOffsetChanged;

        public void ApplyPush(GameScenePush push)
        {
            if (push == null)
            {
                MVXRSDKLog.Warning("SpaceStateStore.ApplyPush: push 为 null，忽略");
                return;
            }

            var obstacles = new List<GameSceneData>(push.SceneData);
            LatestObstacles = obstacles;
            SafeInvoke(OnObstaclesChanged, obstacles, nameof(OnObstaclesChanged));

            var offset   = new Vector3(push.Offset.X,   push.Offset.Y,   push.Offset.Z);
            var rotation = new Vector3(push.Rotation.X, push.Rotation.Y, push.Rotation.Z);
            LatestOffset   = offset;
            LatestRotation = rotation;
            SafeInvoke2(OnOffsetChanged, offset, rotation, nameof(OnOffsetChanged));
        }

        public void Clear()
        {
            LatestObstacles = null;
            LatestOffset    = null;
            LatestRotation  = null;
            // 同时清空订阅者：UnInit 时表现层已销毁，保留订阅会泄漏旧实例引用
            OnObstaclesChanged = null;
            OnOffsetChanged    = null;
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

        private static void SafeInvoke2(Action<Vector3, Vector3> evt, Vector3 a, Vector3 b, string name)
        {
            if (evt == null) return;
            foreach (var handler in evt.GetInvocationList())
            {
                try { ((Action<Vector3, Vector3>)handler)(a, b); }
                catch (Exception e) { MVXRSDKLog.Error($"SpaceStateStore.{name} 订阅者异常: {e}"); }
            }
        }
    }
}
