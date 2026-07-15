using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 表现层：按 Region 快照的 ShowFloor 开关 + 匹配地块，在 XR 偏移节点下显示/隐藏自发光地块可视物。
    /// </summary>
    /// <remarks>
    /// 挂载父节点为 XR 偏移节点（与障碍物一致）：未注册时数据缓存、等注册回放；热替换移父复用、注销销毁。
    /// 地块匹配复用运行时临时 id（MakeRuntimeId），仅 ShowFloor=true 且命中本地地块条目时才显示；
    /// ShowFloor=false / 无匹配 / 无 XR 节点均隐藏。显示是客户端渲染语义——服务端形态无 XR 节点自然不显示。
    /// view 用 SetActive 复用（反复切换不重复建销毁），仅 UnInit / XR 注销时销毁。
    /// </remarks>
    internal class RegionTileModule
    {
        private readonly SpaceStateStore m_Store;

        private LocalRegionDataList m_DataList;
        private bool m_DataListLoadAttempted;
        private RegionSnapshot? m_LastSnapshot;   // 晚注册（XR 节点后到）回放用
        private RegionTileView m_View;

        /// <summary>测试注入点：覆盖地块列表加载方式（默认 Resources.Load 固定契约路径）。</summary>
        internal Func<LocalRegionDataList> LoadDataListOverride;

        public RegionTileModule(SpaceStateStore store)
        {
            m_Store = store;
        }

        public void InitSDK()
        {
            m_Store.OnRegionChanged            += OnRegionChanged;
            MVXRSDK.OnXROffsetNodeRegistered   += OnXROffsetNodeRegistered;
            MVXRSDK.OnXROffsetNodeUnregistered += OnXROffsetNodeUnregistered;
        }

        public void UnInitSDK()
        {
            m_Store.OnRegionChanged            -= OnRegionChanged;
            MVXRSDK.OnXROffsetNodeRegistered   -= OnXROffsetNodeRegistered;
            MVXRSDK.OnXROffsetNodeUnregistered -= OnXROffsetNodeUnregistered;
            DestroyView();
            m_LastSnapshot = null;
            m_DataList = null;
            m_DataListLoadAttempted = false;
        }

        private void OnRegionChanged(RegionSnapshot snapshot)
        {
            m_LastSnapshot = snapshot;
            Refresh();
        }

        private void OnXROffsetNodeRegistered(Transform node)
        {
            // 热替换：view 若存活则移到新父节点复用（不销毁重建——避免帧内并存双份、绕开编辑器 Destroy 限制）；
            // view 若已随旧 XR 子树被外部销毁，则由下方 Refresh→EnsureView 按当前快照重建到新节点
            if (m_View != null && node != null)
                m_View.transform.SetParent(node, false);
            Refresh();
        }

        private void OnXROffsetNodeUnregistered()
        {
            DestroyView();
        }

        private void Refresh()
        {
            var parent = MVXRSDK.XROffsetNode;
            if (parent == null)
            {
                MVXRSDKLog.Info("RegionTile.Refresh: XR 偏移节点未注册，地块暂不显示（等注册回放）");
                return;
            }
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
            var tile = FindTile(id);
            if (tile == null)
            {
                var list = GetDataList();
                MVXRSDKLog.Warning($"RegionTile.Refresh: ShowFloor=true 但无匹配地块 id=[{id}]" +
                                   $"（长{snapshot.Len} 宽{snapshot.Width}），本地条目数 {(list == null ? 0 : list.entries.Count)}，不显示");
                if (m_View != null) m_View.gameObject.SetActive(false);
                return;
            }

            EnsureView(parent);
            if (m_View == null)
            {
                MVXRSDKLog.Error("RegionTile.Refresh: 可视物创建失败（EnsureView 返回空）");
                return;
            }
            m_View.Apply(tile);
            m_View.gameObject.SetActive(true);
            MVXRSDKLog.Info($"RegionTile.Refresh: 显示地块 [{id}] 挂于 [{parent.name}]，" +
                            $"本地pos={tile.position.ToString("F2")} 旋转={tile.rotation.ToString("F2")} " +
                            $"尺寸(宽×长)=({tile.width}×{tile.len})，世界pos={m_View.transform.position.ToString("F2")}");
        }

        private void EnsureView(Transform parent)
        {
            if (m_View != null) return;
            var go = new GameObject("MVXR_RegionTileView");
            go.transform.SetParent(parent, false);
            m_View = go.AddComponent<RegionTileView>();
        }

        private void DestroyView()
        {
            if (m_View != null)
            {
                var go = m_View.gameObject;
                // 运行时用 Destroy（延迟到帧末）；编辑器下（含 EditMode 测试）Destroy 会报错，用 DestroyImmediate
                if (Application.isPlaying) UnityEngine.Object.Destroy(go);
                else UnityEngine.Object.DestroyImmediate(go);
            }
            m_View = null;
        }

        private LocalRegionDataList GetDataList()
        {
            if (!m_DataListLoadAttempted)
            {
                m_DataListLoadAttempted = true;
                m_DataList = LoadDataListOverride != null
                    ? LoadDataListOverride()
                    : Resources.Load<LocalRegionDataList>(LocalRegionDataList.ResourcesLoadPath);
                if (m_DataList == null)
                    MVXRSDKLog.Warning($"RegionTileModule: 本地区域数据列表加载失败：Resources/{LocalRegionDataList.ResourcesLoadPath}，地块可视物不显示");
            }
            return m_DataList;
        }

        private LocalRegionData FindTile(string runtimeId)
        {
            var list = GetDataList();
            return list == null ? null : list.FindById(runtimeId);
        }
    }
}
