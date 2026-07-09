using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 本地区域地块编辑器的场景载体（仅编辑期使用）。
    /// 创建入口：Hierarchy 右键 → MyVerse XRSDK → 生成本地区域地块编辑器；tag=EditorOnly，打包自动剔除。
    /// 运行时逻辑不读本组件——运行时数据走 LocalRegionDataList 资产（Resources 固定路径）。
    /// 编辑状态字段序列化在组件上是为了支持 Undo 与域重载存活。
    /// </summary>
    public sealed class LocalRegionTileAuthoring : MonoBehaviour
    {
        [Tooltip("本地区域数据列表文件（运行时按 Resources/MVXRSDK/LocalRegionData 加载）")]
        public LocalRegionDataList dataList;

        // ------ 编辑工作副本（保存门禁通过后才写回 dataList）------
        [HideInInspector] public bool isEditing;
        [HideInInspector] public bool isNewEntry;              // true = 新建条目，尚未入列表
        [HideInInspector] public int editingIndex = -1;        // 编辑已有条目时的下标；新建为 -1
        [HideInInspector] public bool hasUnsavedChanges;
        [HideInInspector] public LocalRegionData workingCopy = new LocalRegionData();
    }
}
