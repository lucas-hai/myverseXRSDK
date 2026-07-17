using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 本地区域地块编辑器的场景载体（仅编辑期使用）。
    /// 创建入口：Hierarchy 右键 → MyVerse → 生成本地区域地块编辑器；tag=EditorOnly，打包自动剔除。
    /// 运行时逻辑不读本组件——运行时数据走 LocalRegionDataList 资产（Resources 固定路径）。
    /// 编辑状态字段序列化在组件上是为了支持 Undo 与域重载存活。
    /// </summary>
    public sealed class LocalRegionTileAuthoring : MonoBehaviour
    {
        [Tooltip("本地区域数据列表文件（运行时按 Resources/MVXRSDK/LocalRegionData 加载）")]
        public LocalRegionDataList dataList;

        [Tooltip("Scene 视图常显列表中选中（编辑中）的地块底框与 id（无需选中本编辑器对象）")]
        public bool alwaysShowTiles = true;

        // ------ 编辑工作副本（保存门禁通过后才写回 dataList）------
        [HideInInspector] public bool isEditing;
        [HideInInspector] public bool isNewEntry;              // true = 新建条目，尚未入列表
        [HideInInspector] public int editingIndex = -1;        // 编辑已有条目时的下标；新建为 -1
        [HideInInspector] public bool hasUnsavedChanges;
        [HideInInspector] public LocalRegionData workingCopy = new LocalRegionData();
        [HideInInspector] public SdkEnvironment editingEnv;    // 编辑发起时的环境；切环境后编辑态失效防错写

#if UNITY_EDITOR
        // 常显选中地块：Gizmo 不依赖选中态（OnSceneGUI 只在选中编辑器对象时跑）。
        // 只画列表中选中（编辑中）的那块——以工作副本为准（含新建草稿），
        // 未选中编辑器对象时编辑中的地块也不会从 Scene 里消失。
        private void OnDrawGizmos()
        {
            if (!alwaysShowTiles || !isEditing || workingCopy == null) return;
            DrawTileGizmo(workingCopy, new Color(1f, 0.7f, 0.2f, 0.9f));
        }

        private static void DrawTileGizmo(LocalRegionData tile, Color color)
        {
            var rotation = Quaternion.Euler(tile.rotation);
            // 与编辑器绘制约定一致：长 len 沿本地 Z，宽 width 沿本地 X，底框在 XZ 平面
            float halfX = tile.width * 0.5f;
            float halfZ = tile.len * 0.5f;
            var p0 = tile.position + rotation * new Vector3(-halfX, 0f, halfZ);
            var p1 = tile.position + rotation * new Vector3(halfX, 0f, halfZ);
            var p2 = tile.position + rotation * new Vector3(halfX, 0f, -halfZ);
            var p3 = tile.position + rotation * new Vector3(-halfX, 0f, -halfZ);
            Gizmos.color = color;
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);
            UnityEditor.Handles.Label(tile.position, tile.id);
        }
#endif
    }
}
