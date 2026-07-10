using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>Hierarchy 右键创建地块编辑器对象。</summary>
    internal static class RegionTileMenu
    {
        // Hierarchy 右键菜单必须挂 GameObject/ 前缀（Unity 机制），子路径统一 MyVerse
        [MenuItem("GameObject/MyVerse/生成本地区域地块编辑器", false, 10)]
        private static void CreateEditorObject(MenuCommand command)
        {
            var go = new GameObject("本地区域地块编辑器");
            go.tag = "EditorOnly"; // 打包自动剔除，地块编辑器不进真机
            var authoring = go.AddComponent<LocalRegionTileAuthoring>();
            // 工程已有区域配置文件时自动挂到"当前配置文件"，省去手动指定
            authoring.dataList = FindExistingDataList();
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "生成本地区域地块编辑器");
            Selection.activeGameObject = go;
        }

        // 全工程查找已有 LocalRegionDataList 资产；多个时优先运行时契约路径（Resources/MVXRSDK/LocalRegionData）
        private static LocalRegionDataList FindExistingDataList()
        {
            var guids = AssetDatabase.FindAssets("t:LocalRegionDataList");
            if (guids.Length == 0) return null;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/Resources/MVXRSDK/LocalRegionData.asset"))
                    return AssetDatabase.LoadAssetAtPath<LocalRegionDataList>(path);
            }
            return AssetDatabase.LoadAssetAtPath<LocalRegionDataList>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
