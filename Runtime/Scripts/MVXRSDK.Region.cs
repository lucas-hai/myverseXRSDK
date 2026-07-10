using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// MVXRSDK Facade 的 Region 区域对齐：服务端形态注入入口 + 计算结果查询。
    ///
    /// 服务端形态（宿主自持中控连接，如游戏服务端）接入方式：
    /// 1. `InitMVXRSDK(serverId, InitMode.Offline)`——SDK 不建立自己的网络连接；
    /// 2. 宿主从自己的消息链路收到登录响应 regionInfo 首帧 / RegionInfoPush 推送后调
    ///    <see cref="ApplyRegionInfo"/> 注入；
    /// 3. SDK 完成地块匹配 + 区域公式计算：驱动已注册根节点（可选），并触发
    ///    <see cref="OnRegionApplied"/>——宿主拿计算结果写进自己的同步通道（如 FishNet SyncVar）
    ///    分发给游戏客户端，可完全不注册根节点。
    /// 联网模式（Production/WsDirect）下 SDK 自行接收推送，无需调用注入接口。
    /// </summary>
    public static partial class MVXRSDK
    {
        /// <summary>
        /// 注入 Region 全量快照（RegionInfoPush，登录响应 regionInfo 字段同为此类型）。
        /// 全量快照幂等：重复注入以最后一次为准。push/regionInfo 为空的无效快照由 SDK 忽略并告警。
        /// SDK 未初始化时忽略（Offline 模式初始化即可，无需连网）。
        /// </summary>
        public static void ApplyRegionInfo(RegionInfoPush push)
        {
            if (!IsReady)
            {
                MVXRSDKLog.Warning($"ApplyRegionInfo: SDK 未就绪（State={State}），注入被忽略——请先 InitMVXRSDK（Offline 模式即可）");
                return;
            }
            SpaceManager.ApplyRegionSnapshot(push);
        }

        /// <summary>
        /// 查询最近一次 Region 位姿计算结果（即 <see cref="OnRegionApplied"/> 的最新参数值），
        /// 供晚订阅方补齐当前状态。无结果（未收到快照 / 地块未匹配 / SDK 未初始化）返回 false。
        /// </summary>
        public static bool TryGetRegionPose(out Vector3 position, out Vector3 eulerAngles)
        {
            return SpaceManager.TryGetRegionPose(out position, out eulerAngles);
        }
    }
}
