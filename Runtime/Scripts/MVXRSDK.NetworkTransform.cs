namespace MyVerseXRSDK
{
    /// <summary>
    /// MVXRSDK Facade 的网络位置同步（远端玩家虚影）对外配置。
    /// 拆分自 MVXRSDK 主文件，聚合 NetworkTransform 域的对外开关。
    /// </summary>
    public static partial class MVXRSDK
    {
        /// <summary>是否同步"同房间（本房间）其他玩家虚影"。默认 false。</summary>
        public static bool IsSyncSameRoomAvatar => NetworkTransformManager.IsSyncSameRoomAvatar;

        /// <summary>同房间虚影的显示距离（米）。默认 2m，由 SetSyncSameRoomAvatar 的距离参数设置。</summary>
        public static float SameRoomAvatarDistance => NetworkTransformManager.SameRoomDistance;

        /// <summary>
        /// 设置是否同步"同房间（本房间）其他玩家虚影"，并指定其显示距离。默认 false（不同步）。
        ///
        /// 背景：SDK 收到服务端位置推送时，默认只为"非本房间"成员创建虚影代理，
        /// 本房间成员的位置推送被跳过。业务若需要在场景内看到同房间其他玩家的虚影，调用本方法置 true 开启。
        ///
        /// 显示距离规则：
        /// - 本机（自己）的虚影【一律不显示】，无论同房间还是其他房间；
        /// - 其他房间虚影显示距离【固定 2m】，不受参数影响；
        /// - 同房间虚影显示距离取 <paramref name="displayDistanceMeters"/>（米，默认 2m，&lt;=0 回退 2m），仅在开启时生效。
        ///
        /// 任何时机皆可调用（Init 前/中/后）：
        /// - 开启：后续收到本房间位置推送即创建虚影（成员静止不发推送时，等其移动后出现）。
        /// - 关闭：立即回收已创建的本房间虚影（不影响非本房间的虚影）。
        ///
        /// 注意：虚影外观复用 SDK 内置角色 prefab（Characters/Prefabs/Role），
        /// 且与其他远端虚影一样依赖已注册 XR 偏移节点（RegisterXROffsetNode）才会落地到场景。
        /// </summary>
        /// <param name="enable">是否开启同房间虚影同步。</param>
        /// <param name="displayDistanceMeters">同房间虚影显示距离（米），默认 2m；仅在 enable=true 时生效。</param>
        public static void SetSyncSameRoomAvatar(bool enable, float displayDistanceMeters = 2f)
        {
            NetworkTransformManager.SetSyncSameRoomAvatar(enable, displayDistanceMeters);
        }
    }
}
