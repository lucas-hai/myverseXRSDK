using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>Hierarchy 右键创建地块编辑器对象。</summary>
    internal static class RegionTileMenu
    {
        [MenuItem("GameObject/MyVerse XRSDK/生成本地区域地块编辑器", false, 10)]
        private static void CreateEditorObject(MenuCommand command)
        {
            var go = new GameObject("本地区域地块编辑器");
            go.tag = "EditorOnly"; // 打包自动剔除，地块编辑器不进真机
            go.AddComponent<LocalRegionTileAuthoring>();
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "生成本地区域地块编辑器");
            Selection.activeGameObject = go;
        }
    }
}
