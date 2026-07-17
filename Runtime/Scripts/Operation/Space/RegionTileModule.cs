using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 表现层：按 Region 快照的 ShowFloor 开关 + 匹配地块，以独立常驻对象显示/隐藏自发光地块可视物。
    /// </summary>
    /// <remarks>
    /// 可视物是独立场景根对象（不挂 XR/根节点）+ DontDestroyOnLoad：区域是物理固定的，
    /// 地块跨场景常驻、不随场景卸载删除。位姿取区域对齐计算结果（与场景根节点同一世界位姿），不依赖 XR 偏移节点。
    /// 地块匹配复用运行时临时 id（MakeRuntimeId），仅 ShowFloor=true 且命中本地地块条目时才显示；
    /// ShowFloor=false / 无匹配均隐藏（SetActive 复用，不重复建销毁）。仅 UnInit 时销毁。
    /// </remarks>
    internal class RegionTileModule
    {
        private readonly SpaceStateStore m_Store;

        private RegionSnapshot? m_LastSnapshot;
        private RegionTileView m_View;

        public RegionTileModule(SpaceStateStore store)
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
            DestroyView();
            m_LastSnapshot = null;
        }

        private void OnRegionChanged(RegionSnapshot snapshot)
        {
            m_LastSnapshot = snapshot;
            Refresh();
        }

        private void Refresh()
        {
            if (!m_LastSnapshot.HasValue)
            {
                MVXRSDKLog.Info("RegionTile.Refresh: 尚无 Region 快照，地块暂不显示");
                return;
            }

            var snapshot = m_LastSnapshot.Value;
            if (!snapshot.ShowFloor)
            {
                MVXRSDKLog.Info("RegionTile.Refresh: ShowFloor=false，隐藏地块");
                if (m_View != null) m_View.gameObject.SetActive(false);
                return;
            }

            var id = RegionIdUtil.MakeRuntimeId(snapshot.Len, snapshot.Width);
            var tile = LocalRegionTileStore.FindTile(id);
            if (tile == null)
            {
                MVXRSDKLog.Warning($"RegionTile.Refresh: ShowFloor=true 但无匹配地块 id=[{id}]" +
                                   $"（长{snapshot.Len} 宽{snapshot.Width}），激活环境有效条目数 {LocalRegionTileStore.ValidEntryCount}，不显示");
                if (m_View != null) m_View.gameObject.SetActive(false);
                return;
            }

            EnsureView();
            if (m_View == null)
            {
                MVXRSDKLog.Error("RegionTile.Refresh: 可视物创建失败（EnsureView 返回空）");
                return;
            }
            // 位置与旋转采用区域对齐计算结果（与场景根节点同一世界位姿），尺寸仍取地块长宽
            var (position, eulerAngles) = RegionAlignmentCalculator.Compute(snapshot, tile);
            m_View.Apply(position, eulerAngles, tile.width, tile.len);
            m_View.gameObject.SetActive(true);
            MVXRSDKLog.Info($"RegionTile.Refresh: 显示地块 [{id}]（跨场景常驻），" +
                            $"世界位姿(取自区域计算)=pos {position.ToString("F2")} rot {eulerAngles.ToString("F2")}，" +
                            $"尺寸(宽×长)=({tile.width}×{tile.len})");
        }

        // 可视物：独立场景根对象（不挂任何业务节点）+ 跳场景不删除，跨场景常驻标记物理区域
        private void EnsureView()
        {
            if (m_View != null) return;
            var go = new GameObject("MVXR_RegionTileView");
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
            m_View = go.AddComponent<RegionTileView>();
        }

        private void DestroyView()
        {
            if (m_View != null)
            {
                var go = m_View.gameObject;
                // 运行时用 Destroy（延迟到帧末）；编辑器下（含 EditMode）Destroy 会报错，用 DestroyImmediate
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
            }
            m_View = null;
        }

    }
}
