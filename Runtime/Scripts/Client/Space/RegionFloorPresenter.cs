using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class RegionFloorPresenter : IDisposable
    {
        private readonly SpatialService space;
        private readonly SpatialOptions options;
        private GameObject floor;
        private readonly SdkLogger log;

        internal RegionFloorPresenter(SpatialService space, SpatialOptions options, SdkLogger log)
        {
            this.space = space;
            this.options = options;
            this.log = log;
            space.BindingChanging += Clear;
            space.PresentationChanged += Refresh;
        }

        private void Refresh()
        {
            var region = space.CurrentRegion;
            bool visible = options.ShowFloor && space.PresentationReady && region.HasValue && region.Value.ShowFloor;
            if (!visible)
            {
                if (floor != null) floor.SetActive(false);
                return;
            }
            if (floor == null)
            {
                floor = new GameObject("RegionFloor") { hideFlags = HideFlags.DontSave };
                floor.transform.SetParent(space.ProxyRoot, false);
                floor.AddComponent<RegionTileView>().Initialize(log);
            }
            var value = region.Value;
            // 父级已经承载B×C；此处只写物理区域本身，不能再应用地图对齐结果T。
            floor.transform.SetLocalPositionAndRotation(value.Center, value.Rotation.normalized);
            floor.transform.localScale = new Vector3(Mathf.Abs(value.Width), 1, Mathf.Abs(value.Length));
            floor.SetActive(true);
        }

        private void Clear()
        {
            if (floor != null) SpatialContext.Destroy(floor);
            floor = null;
        }

        public void Dispose()
        {
            space.BindingChanging -= Clear;
            space.PresentationChanged -= Refresh;
            Clear();
        }
    }
}
