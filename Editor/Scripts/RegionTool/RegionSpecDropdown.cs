using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>
    /// 远端区域规格选择弹窗（Add Component 同款 AdvancedDropdown）：
    /// 自带搜索框 + 滚动 + 键盘导航——规格上百条时 GenericMenu 不可搜索/超屏，无法定位目标。
    /// 条目按 id 数值排序（先宽后长；字符串排序会把 10x12 排到 2x4 前面）；
    /// 当前环境已存在的 id 置灰（保留信息价值：能看出"已建过"而非"远端没有"）。
    /// </summary>
    internal sealed class RegionSpecDropdown : AdvancedDropdown
    {
        // 携带规格数据的条目（AdvancedDropdownItem 原生只有 name/id，选中回调需要拿回完整 spec）
        private sealed class SpecItem : AdvancedDropdownItem
        {
            public readonly RegionSpec Spec;
            public SpecItem(RegionSpec spec) : base(spec.id) { Spec = spec; }
        }

        private readonly List<RegionSpec> m_Specs;
        private readonly Func<string, bool> m_IsTaken;
        private readonly Action<RegionSpec> m_OnSelect;

        public RegionSpecDropdown(List<RegionSpec> specs, Func<string, bool> isTaken, Action<RegionSpec> onSelect)
            : base(new AdvancedDropdownState())
        {
            m_Specs = specs;
            m_IsTaken = isTaken;
            m_OnSelect = onSelect;
            minimumSize = new Vector2(240f, 320f);   // 长列表给足弹窗高度，减少滚动
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            // 数值排序：先宽后长（id 解析失败的排最后，按原文兜底）
            var sorted = new List<RegionSpec>(m_Specs);
            sorted.Sort(CompareSpec);

            int taken = 0;
            var root = new AdvancedDropdownItem("区域规格");
            foreach (var spec in sorted)
            {
                var item = new SpecItem(spec);
                if (m_IsTaken(spec.id))
                {
                    item.enabled = false;
                    taken++;
                }
                root.AddChild(item);
            }
            root.name = $"区域规格（共 {sorted.Count} 条，{taken} 条已创建）";
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is SpecItem specItem)
                m_OnSelect?.Invoke(specItem.Spec);
        }

        private static int CompareSpec(RegionSpec a, RegionSpec b)
        {
            bool aOk = RegionIdUtil.TryParseId(a?.id, out var aLen, out var aWidth);
            bool bOk = RegionIdUtil.TryParseId(b?.id, out var bLen, out var bWidth);
            if (aOk && bOk)
            {
                int byWidth = aWidth.CompareTo(bWidth);
                return byWidth != 0 ? byWidth : aLen.CompareTo(bLen);
            }
            if (aOk != bOk) return aOk ? -1 : 1;   // 可解析的排前面
            return string.CompareOrdinal(a?.id, b?.id);
        }
    }
}
