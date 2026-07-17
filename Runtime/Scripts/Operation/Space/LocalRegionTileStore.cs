using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 运行时地块数据统一取数口（SpatialAlternationModule / RegionTileModule 共用）：
    /// Resources 加载 → 取激活环境组 → 防篡改验签（Offline 免验）→ 按 id 查找。
    /// 加载与验签结果缓存（资产与激活环境运行期不变），UnInit 时 Reset。
    /// 验签失败 fail-loud：整组拒用 + Error 日志（对齐/地块显示均不生效），不静默降级。
    /// </summary>
    internal static class LocalRegionTileStore
    {
        /// <summary>测试注入点：覆盖数据列表加载方式（默认 Resources.Load 固定契约路径）。</summary>
        internal static Func<LocalRegionDataList> LoadOverride;

        private static bool s_Attempted;                 // 加载+验签只做一次（结果含"失败"也缓存）
        private static EnvironmentTileSet s_ValidSet;    // 验签通过的激活环境组；失败/缺失为 null

        /// <summary>激活环境组内按 id 查找（匹配规则统一走 EnvironmentTileSet.FindById）；组无效（未提供/验签失败/无该组）返回 null。</summary>
        internal static LocalRegionData FindTile(string runtimeId)
        {
            var set = GetValidatedSet();
            return set == null ? null : set.FindById(runtimeId);
        }

        /// <summary>激活环境组条目数（日志用）；组无效返回 0。</summary>
        internal static int ValidEntryCount
        {
            get
            {
                var set = GetValidatedSet();
                return set == null ? 0 : set.entries.Count;
            }
        }

        /// <summary>UnInit / 测试清理：清缓存与注入点，允许下次重新加载验签。</summary>
        internal static void Reset()
        {
            s_Attempted = false;
            s_ValidSet = null;
            LoadOverride = null;
        }

        private static EnvironmentTileSet GetValidatedSet()
        {
            if (s_Attempted) return s_ValidSet;
            s_Attempted = true;
            s_ValidSet = null;

            var list = LoadOverride != null
                ? LoadOverride()
                : Resources.Load<LocalRegionDataList>(LocalRegionDataList.ResourcesLoadPath);
            if (list == null)
            {
                MVXRSDKLog.Warning($"LocalRegionTileStore: 本地区域数据列表加载失败：Resources/{LocalRegionDataList.ResourcesLoadPath}" +
                                   "（工程未提供或路径不符），Region 对齐/地块显示不生效");
                return null;
            }

            var env = list.activeEnvironment;
            var set = list.GetSet(env);
            if (set == null || set.entries.Count == 0)
            {
                MVXRSDKLog.Warning($"LocalRegionTileStore: 激活环境 [{env}] 无地块条目" +
                                   "（未配置该环境地块，或旧版数据未迁移——请在地块编辑器打开一次完成迁移并重新封签）");
                return null;
            }

            if (!RegionDataSeal.Verify(set))
            {
                MVXRSDKLog.Error($"LocalRegionTileStore: 激活环境 [{env}] 地块数据签名校验失败——" +
                                 "本地数据可能被绕过地块编辑器修改（或迁移后未重新封签），整组拒用。" +
                                 "请用地块编辑器重新保存，或在 SDK 验证面板执行\"重新封签\"");
                return null;
            }

            s_ValidSet = set;
            return s_ValidSet;
        }
    }
}
