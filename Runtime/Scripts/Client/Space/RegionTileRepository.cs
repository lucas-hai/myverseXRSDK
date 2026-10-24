using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyVerseXRSDK
{
    internal sealed class RegionTileRepository : IRegionTileSource
    {
        private readonly Dictionary<string, RegionTile> tiles = new Dictionary<string, RegionTile>(StringComparer.Ordinal);
        private bool loaded;
        private SdkResult loadResult;
        public int Version { get; private set; }

        public SdkResult Find(string id, out RegionTile tile)
        {
            tile = default;
            if (!loaded) Load();
            if (!loadResult.Success) return loadResult;
            return tiles.TryGetValue(id, out tile)
                ? SdkResult.Ok
                : SdkResult.Fail(SdkErrorCode.MissingRegionTile, $"未找到有效地块：{id}");
        }

        private void Load()
        {
            loaded = true;
            var asset = Resources.Load<LocalRegionDataList>(LocalRegionDataList.ResourcesLoadPath);
            if (asset == null)
            {
                loadResult = SdkResult.Fail(SdkErrorCode.MissingRegionTile, "未提供Resources/MVXRSDK/LocalRegionData资产");
                return;
            }
            var set = asset.GetSet(asset.activeEnvironment);
            if (set == null || !RegionDataSeal.Verify(set))
            {
                loadResult = SdkResult.Fail(SdkErrorCode.InvalidCalibration, "激活环境的地块组不存在或封签无效");
                return;
            }
            foreach (var entry in set.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id) ||
                    !SpatialValueValidation.Finite(entry.position) || !SpatialValueValidation.Finite(entry.rotation) ||
                    !SpatialValueValidation.Finite(entry.len) || !SpatialValueValidation.Finite(entry.width) ||
                    entry.len <= 0 || entry.width <= 0 || tiles.ContainsKey(entry.id))
                {
                    tiles.Clear();
                    loadResult = SdkResult.Fail(SdkErrorCode.InvalidCalibration, "地块组包含无效数据或重复id，整组拒用");
                    return;
                }
                tiles.Add(entry.id, new RegionTile(entry.id, entry.position, Quaternion.Euler(entry.rotation), entry.width, entry.len));
            }
            Version = 1;
            loadResult = SdkResult.Ok;
        }
    }
}
