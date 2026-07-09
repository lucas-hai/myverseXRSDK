using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>地块编辑器 Inspector：配置文件管理 + 条目列表；编辑态与保存门禁见后续任务扩展。</summary>
    [CustomEditor(typeof(LocalRegionTileAuthoring))]
    public sealed class LocalRegionTileAuthoringEditor : UnityEditor.Editor
    {
        private string m_Search = "";

        private LocalRegionTileAuthoring Target => (LocalRegionTileAuthoring)target;

        public override void OnInspectorGUI()
        {
            DrawDataListRow();
            if (Target.dataList == null)
            {
                EditorGUILayout.HelpBox("请指定或新建本地区域数据列表文件", MessageType.Info);
                return;
            }
            DrawAssetPathWarning();
            DrawToolbar();
            DrawEntryList();
            if (Target.isEditing)
                DrawEditPanel();
        }

        // ------ 编辑态：工作副本模型，保存门禁见 Task 8 ------

        private void StartEditExisting(int index)
        {
            if (!ConfirmDiscardUnsaved()) return;
            Undo.RecordObject(Target, "编辑地块条目");
            Target.isEditing = true;
            Target.isNewEntry = false;
            Target.editingIndex = index;
            Target.hasUnsavedChanges = false;
            Target.workingCopy = Target.dataList.entries[index].Clone();
            SceneView.RepaintAll();
            Repaint();
        }

        private void DrawEditPanel()
        {
            var wc = Target.workingCopy;
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(Target.isNewEntry ? $"正在编辑（新建）: {wc.id}" : $"正在编辑: {wc.id}",
                                       EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var len = EditorGUILayout.FloatField("长（米，本地Z轴）", wc.len);
            var width = EditorGUILayout.FloatField("宽（米，本地X轴）", wc.width);
            var pos = EditorGUILayout.Vector3Field("位置", wc.position);
            var rot = EditorGUILayout.Vector3Field("旋转（欧拉角）", wc.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Target, "编辑地块数据");
                wc.len = len;       // 长宽可编辑；id 不变，运行时匹配仍按 id
                wc.width = width;
                wc.position = pos;
                wc.rotation = rot;
                Target.hasUnsavedChanges = true;
                SceneView.RepaintAll();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存"))
                SaveWorkingCopy();
            if (GUILayout.Button("取消"))
                CancelEditing();
            EditorGUILayout.EndHorizontal();
        }

        // ------ 保存门禁：上传成功是落盘前置条件（失败不落盘，工作副本保留可重试）------

        private void SaveWorkingCopy()
        {
            var snapshot = Target.workingCopy.Clone(); // 上传与落盘用同一份快照，防止回调期间被继续编辑
            RegionToolServices.Uploader.Upload(snapshot,
                onDone: () =>
                {
                    Undo.RecordObject(Target.dataList, "保存地块条目");
                    if (Target.isNewEntry)
                        Target.dataList.entries.Add(snapshot);
                    else
                        Target.dataList.entries[Target.editingIndex] = snapshot;
                    EditorUtility.SetDirty(Target.dataList);
                    AssetDatabase.SaveAssets();

                    Undo.RecordObject(Target, "结束编辑地块");
                    Target.isEditing = false;
                    Target.isNewEntry = false;
                    Target.editingIndex = -1;
                    Target.hasUnsavedChanges = false;
                    SceneView.RepaintAll();
                    Repaint();
                },
                onError: error => EditorUtility.DisplayDialog("上传失败",
                    $"{error}\n\n数据未保存，可修改后重试。", "确定"));
        }

        // 有未保存修改时返回 false 表示用户选择留在编辑态
        private bool ConfirmDiscardUnsaved()
        {
            if (!Target.isEditing || !Target.hasUnsavedChanges) return true;
            return EditorUtility.DisplayDialog("未保存的修改",
                $"当前条目 {Target.workingCopy.id} 有未保存的修改，确定放弃吗？", "放弃修改", "继续编辑");
        }

        private void CancelEditing()
        {
            if (!ConfirmDiscardUnsaved()) return;
            Undo.RecordObject(Target, "取消编辑地块");
            Target.isEditing = false;
            Target.isNewEntry = false;
            Target.editingIndex = -1;
            Target.hasUnsavedChanges = false;
            SceneView.RepaintAll();
            Repaint();
        }

        // ------ SceneView：底框绘制 + 位置/旋转 Handle ------

        private void OnSceneGUI()
        {
            if (!Target.isEditing || Target.workingCopy == null) return;
            var wc = Target.workingCopy;
            var rotation = Quaternion.Euler(wc.rotation);

            // 长 len 沿本地 Z，宽 width 沿本地 X，底框画在 XZ 平面
            float halfX = wc.width * 0.5f;
            float halfZ = wc.len * 0.5f;
            var corners = new Vector3[4]
            {
                wc.position + rotation * new Vector3(-halfX, 0f,  halfZ),
                wc.position + rotation * new Vector3( halfX, 0f,  halfZ),
                wc.position + rotation * new Vector3( halfX, 0f, -halfZ),
                wc.position + rotation * new Vector3(-halfX, 0f, -halfZ),
            };
            Handles.DrawSolidRectangleWithOutline(corners,
                new Color(0f, 0.6f, 1f, 0.1f), new Color(0f, 0.6f, 1f, 0.9f));

            EditorGUI.BeginChangeCheck();
            var newPos = Handles.PositionHandle(wc.position, rotation);
            var newRot = Handles.RotationHandle(rotation, wc.position);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Target, "拖拽地块位姿");
                wc.position = newPos;
                wc.rotation = newRot.eulerAngles;
                Target.hasUnsavedChanges = true;
                Repaint();
            }
        }

        // ------ 配置文件槽位 + 新建 ------

        private void DrawDataListRow()
        {
            EditorGUILayout.BeginHorizontal();
            var newList = (LocalRegionDataList)EditorGUILayout.ObjectField(
                "当前配置文件", Target.dataList, typeof(LocalRegionDataList), false);
            if (newList != Target.dataList)
            {
                Undo.RecordObject(Target, "切换配置文件");
                Target.dataList = newList;
            }
            if (Target.dataList == null && GUILayout.Button("新建", GUILayout.Width(44f)))
                CreateDataListAsset();
            EditorGUILayout.EndHorizontal();
        }

        // 默认创建到运行时契约路径 Assets/Resources/MVXRSDK/LocalRegionData.asset
        private void CreateDataListAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder("Assets/Resources/MVXRSDK"))
                AssetDatabase.CreateFolder("Assets/Resources", "MVXRSDK");

            var asset = ScriptableObject.CreateInstance<LocalRegionDataList>();
            AssetDatabase.CreateAsset(asset, "Assets/Resources/MVXRSDK/LocalRegionData.asset");
            AssetDatabase.SaveAssets();
            Undo.RecordObject(Target, "新建配置文件");
            Target.dataList = asset;
            Debug.Log("[MVXRSDK] 已创建本地区域数据列表：Assets/Resources/MVXRSDK/LocalRegionData.asset");
        }

        // 运行时按 Resources/MVXRSDK/LocalRegionData 固定路径加载，路径不符给警告
        private void DrawAssetPathWarning()
        {
            var path = AssetDatabase.GetAssetPath(Target.dataList);
            if (!path.EndsWith("/Resources/MVXRSDK/LocalRegionData.asset"))
            {
                EditorGUILayout.HelpBox(
                    $"资产路径不符合运行时加载契约（任一 Resources 根下的 MVXRSDK/LocalRegionData.asset），运行时将加载不到：\n{path}",
                    MessageType.Warning);
            }
        }

        // ------ 工具行：搜索 / 创建 / 刷新 ------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            m_Search = EditorGUILayout.TextField("搜索", m_Search);
            if (GUILayout.Button("创建新条目", GUILayout.Width(84f)))
                OnCreateEntryClicked();
            if (GUILayout.Button("刷新", GUILayout.Width(44f)))
                Repaint();
            EditorGUILayout.EndHorizontal();
        }

        // ------ 创建条目：验证拦截 → 每次点击都重新拉规格 → 置灰选择 ------

        private void OnCreateEntryClicked()
        {
            if (!ConfirmDiscardUnsaved()) return;
            if (!SdkAuthStore.IsVerified)
            {
                if (EditorUtility.DisplayDialog("需要 SDK 验证",
                        "拉取区域规格列表需要先完成 SDK 验证。", "打开验证面板", "取消"))
                    SdkAuthWindow.Open();
                return;
            }
            RegionToolServices.SpecSource.Fetch(
                ShowSpecMenu,
                error => EditorUtility.DisplayDialog("拉取区域规格失败",
                    $"{error}\n\n可再次点击\"创建新条目\"重试。", "确定"));
        }

        private void ShowSpecMenu(System.Collections.Generic.List<RegionSpec> specs)
        {
            var menu = new GenericMenu();
            if (specs == null || specs.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（远端无可用规格）"));
            }
            else
            {
                foreach (var spec in specs)
                {
                    // 已持久化条目 + 当前未保存的新建工作副本 都算“已存在”→ 置灰
                    bool taken = Target.dataList.ContainsId(spec.id) ||
                                 (Target.isEditing && Target.isNewEntry && Target.workingCopy.id == spec.id);
                    if (taken)
                    {
                        menu.AddDisabledItem(new GUIContent(spec.id));
                    }
                    else
                    {
                        var captured = spec; // 闭包捕获当前项
                        menu.AddItem(new GUIContent(captured.id), false, () => BeginCreate(captured));
                    }
                }
            }
            menu.ShowAsContext();
        }

        private void BeginCreate(RegionSpec spec)
        {
            // 长宽默认按 id 拆取（"12x6"→12/6）；拆不出再用规格自带值兜底。后续可在编辑面板改动，id 不变
            if (!RegionIdUtil.TryParseId(spec.id, out var len, out var width))
            {
                len = spec.len;
                width = spec.width;
            }

            Undo.RecordObject(Target, "创建地块条目");
            Target.isEditing = true;
            Target.isNewEntry = true;
            Target.editingIndex = -1;
            Target.hasUnsavedChanges = true;
            Target.workingCopy = new LocalRegionData
            {
                id = spec.id,
                len = len,
                width = width,
                position = Vector3.zero,
                rotation = Vector3.zero,
            };
            SceneView.RepaintAll();
            Repaint();
        }

        // ------ 条目列表（行点击进入编辑态在 Task 7 接管）------

        private void DrawEntryList()
        {
            var entries = Target.dataList.entries;
            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无条目", MessageType.None);
                return;
            }
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                if (!string.IsNullOrEmpty(m_Search) &&
                    (entry.id == null || !entry.id.Contains(m_Search)))
                    continue;

                // 选中态：正在编辑的行高亮（背景 + ▶ 前缀 + 加粗）
                bool selected = Target.isEditing && !Target.isNewEntry && Target.editingIndex == i;
                var prevBackground = GUI.backgroundColor;
                if (selected)
                    GUI.backgroundColor = new Color(0.35f, 0.65f, 1f);
                EditorGUILayout.BeginHorizontal("box");
                GUI.backgroundColor = prevBackground;
                if (GUILayout.Button($"{(selected ? "▶ " : "")}ID: {entry.id} ｜ 长{entry.len} 宽{entry.width}",
                                     selected ? EditorStyles.boldLabel : EditorStyles.label))
                    StartEditExisting(i);
                if (GUILayout.Button("删除", GUILayout.Width(44f)))
                {
                    if (EditorUtility.DisplayDialog("删除条目", $"确认删除 {entry.id}？（不同步远端）", "删除", "取消"))
                    {
                        Undo.RecordObject(Target.dataList, "删除地块条目");
                        entries.RemoveAt(i);
                        EditorUtility.SetDirty(Target.dataList);
                        AssetDatabase.SaveAssets();
                        EditorGUILayout.EndHorizontal();
                        GUIUtility.ExitGUI(); // 列表已变更，中断本帧绘制避免下标错乱
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
