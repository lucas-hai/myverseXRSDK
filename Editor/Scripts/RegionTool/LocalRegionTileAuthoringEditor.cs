using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>地块编辑器 Inspector：配置文件管理 + 条目列表；编辑态与保存门禁见后续任务扩展。</summary>
    [CustomEditor(typeof(LocalRegionTileAuthoring))]
    public sealed class LocalRegionTileAuthoringEditor : UnityEditor.Editor
    {
        private const int EntriesPerPage = 5;   // 条目滚动列表一页显示的条数，超出滚动

        private string m_Search = "";
        private bool m_FetchingSpecs;                                          // 规格列表拉取在途（按钮置灰防重入）
        private System.Collections.Generic.List<RegionSpec> m_PendingSpecs;    // 异步拉回、待在 OnGUI 内弹出的规格
        private bool m_Uploading;                                              // 地块上传在途（冻结全部交互）
        private Vector2 m_EntryListScroll;

        private LocalRegionTileAuthoring Target => (LocalRegionTileAuthoring)target;

        public override void OnInspectorGUI()
        {
            // 上传在途：冻结全部交互（防重复保存/切换条目/删除导致回调写错行），完成后解冻
            if (m_Uploading)
                EditorGUILayout.HelpBox("正在上传地块数据…", MessageType.Info);
            using (new EditorGUI.DisabledScope(m_Uploading))
            {
                DrawDataListRow();
                DrawAlwaysShowToggle();
                if (Target.dataList == null)
                {
                    EditorGUILayout.HelpBox("请指定或新建本地区域数据列表文件", MessageType.Info);
                    return;
                }
                DrawAssetPathWarning();
                DrawToolbar();
                // 异步拉回的规格列表转到 OnGUI 内弹菜单：GenericMenu.ShowAsContext 依赖
                // Event.current，在 HTTP 回调（EditorApplication.update）里直接调会静默失效
                if (m_PendingSpecs != null && Event.current.type == EventType.Layout)
                {
                    var specs = m_PendingSpecs;
                    m_PendingSpecs = null;
                    ShowSpecMenu(specs);
                }
                DrawEntryList();
                // 防御：编辑中的已有条目下标失效（删除/Undo/外部改动）→ 退出编辑态，不再显示编辑区
                if (Target.isEditing && !Target.isNewEntry &&
                    (Target.editingIndex < 0 || Target.editingIndex >= Target.dataList.entries.Count))
                {
                    Target.isEditing = false;
                    Target.editingIndex = -1;
                    Target.hasUnsavedChanges = false;
                    SceneView.RepaintAll();
                }
                if (Target.isEditing)
                    DrawEditPanel();
            }
        }

        // 删除条目后同步编辑态：删的是正在编辑的条目 → 退出编辑；删的在其前面 → 下标前移对齐
        private void OnEntryDeleted(int deletedIndex)
        {
            if (!Target.isEditing || Target.isNewEntry) return;
            Undo.RecordObject(Target, "删除地块条目");
            if (Target.editingIndex == deletedIndex)
            {
                Target.isEditing = false;
                Target.editingIndex = -1;
                Target.hasUnsavedChanges = false;
                SceneView.RepaintAll();
            }
            else if (Target.editingIndex > deletedIndex)
            {
                Target.editingIndex--;
            }
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
            // 编辑区域：与列表区域分离的独立区块（分隔线 + 盒式背景 + 标题）
            EditorGUILayout.Space(8f);
            var divider = EditorGUILayout.GetControlRect(false, 2f);
            EditorGUI.DrawRect(divider, new Color(0.5f, 0.5f, 0.5f, 1f));
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.LabelField(Target.isNewEntry ? $"编辑区域（新建条目）: {wc.id}" : $"编辑区域: {wc.id}",
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
            EditorGUILayout.EndVertical();
        }

        // ------ 保存门禁：上传成功是落盘前置条件（失败不落盘，工作副本保留可重试）------

        private void SaveWorkingCopy()
        {
            var snapshot = Target.workingCopy.Clone(); // 上传与落盘用同一份快照，防止回调期间被继续编辑
            // 捕获写入上下文：上传期间 UI 已冻结，但 Inspector 可能被关闭/切换选中对象，
            // 回调不能再依赖 Target 的编辑态
            var dataList = Target.dataList;
            bool isNewEntry = Target.isNewEntry;
            int editingIndex = Target.editingIndex;

            m_Uploading = true;
            Repaint();
            RegionToolServices.Uploader.Upload(snapshot,
                onDone: () =>
                {
                    // 门禁兑现：上传已成功，即使 Inspector 已关闭也要落盘（用捕获的写入目标）
                    bool saved = false;
                    if (dataList != null)
                    {
                        Undo.RecordObject(dataList, "保存地块条目");
                        if (isNewEntry)
                        {
                            dataList.entries.Add(snapshot);
                            saved = true;
                        }
                        else if (editingIndex >= 0 && editingIndex < dataList.entries.Count)
                        {
                            dataList.entries[editingIndex] = snapshot;
                            saved = true;
                        }
                        if (saved)
                        {
                            EditorUtility.SetDirty(dataList);
                            AssetDatabase.SaveAssets();
                        }
                    }
                    if (!saved)
                    {
                        Debug.LogWarning($"[MVXRSDK] 地块 {snapshot.id} 已上传成功，但本地写入目标已失效（配置文件被删/条目下标失效），未落盘");
                    }

                    if (this != null)   // Inspector 仍存活：结束编辑态并解冻
                    {
                        m_Uploading = false;
                        Undo.RecordObject(Target, "结束编辑地块");
                        Target.isEditing = false;
                        Target.isNewEntry = false;
                        Target.editingIndex = -1;
                        Target.hasUnsavedChanges = false;
                        SceneView.RepaintAll();
                        Repaint();
                    }
                    if (saved)
                        EditorUtility.DisplayDialog("保存成功", $"地块 {snapshot.id} 已上传远端并保存到本地。", "确定");
                },
                onError: error =>
                {
                    if (this != null)
                    {
                        m_Uploading = false;
                        Repaint();
                    }
                    EditorUtility.DisplayDialog("上传失败",
                        $"{error}\n\n数据未保存，可修改后重试。", "确定");
                });
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
            if (m_Uploading) return;   // 上传在途冻结拖拽：上传/落盘用的是点击保存时的快照，途中改动会被丢弃
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

        // 常显开关：写在组件序列化字段上，Gizmo 依据它在未选中时也绘制列表中选中的地块
        private void DrawAlwaysShowToggle()
        {
            EditorGUI.BeginChangeCheck();
            bool show = EditorGUILayout.ToggleLeft("常显选中地块（未选中本对象时也显示列表中选中/编辑中的地块）",
                                                   Target.alwaysShowTiles);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(Target, "切换地块常显");
                Target.alwaysShowTiles = show;
                EditorUtility.SetDirty(Target);
                SceneView.RepaintAll();
            }
        }

        // ------ 工具行：搜索 / 创建 / 刷新 ------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            m_Search = EditorGUILayout.TextField("搜索", m_Search);
            using (new EditorGUI.DisabledScope(m_FetchingSpecs))
            {
                if (GUILayout.Button(m_FetchingSpecs ? "拉取中…" : "创建新条目", GUILayout.Width(84f)))
                    OnCreateEntryClicked();
            }
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
            m_FetchingSpecs = true;
            RegionToolServices.SpecSource.Fetch(
                specs =>
                {
                    if (this == null) return;   // Inspector 已关闭/切换选中对象（Unity 假 null）
                    m_FetchingSpecs = false;
                    // 空列表不弹菜单（置灰菜单项易被忽略），改用明确的提示弹窗
                    if (specs == null || specs.Count == 0)
                    {
                        Repaint();
                        EditorUtility.DisplayDialog("无可用规格",
                            "远端区域规格列表为空，无法创建条目。\n请确认开发者平台已为该 appId 配置区域规格。", "确定");
                        return;
                    }
                    m_PendingSpecs = specs;     // 菜单弹出转到下一次 OnInspectorGUI（需 GUI 事件上下文）
                    Repaint();
                },
                error =>
                {
                    if (this != null)
                    {
                        m_FetchingSpecs = false;
                        Repaint();
                    }
                    EditorUtility.DisplayDialog("拉取区域规格失败",
                        $"{error}\n\n可再次点击\"创建新条目\"重试。", "确定");
                });
        }

        private void ShowSpecMenu(System.Collections.Generic.List<RegionSpec> specs)
        {
            var menu = new GenericMenu();
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
            menu.ShowAsContext();
        }

        private void BeginCreate(RegionSpec spec)
        {
            // 绘制默认值取规格自带的远端尺寸（width/height，可能带小数）；
            // 缺省（≤0）时回退按 id 拆取（"6x12"→len 12/width 6）。后续可在编辑面板改动，id 不变
            var len = spec.len;
            var width = spec.width;
            if ((len <= 0f || width <= 0f) && RegionIdUtil.TryParseId(spec.id, out var parsedLen, out var parsedWidth))
            {
                len = parsedLen;
                width = parsedWidth;
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

        private bool MatchesSearch(LocalRegionData entry)
        {
            if (entry == null) return false;
            if (string.IsNullOrEmpty(m_Search)) return true;
            return entry.id != null && entry.id.Contains(m_Search);
        }

        private void DrawEntryList()
        {
            var entries = Target.dataList.entries;
            // 新建中的工作副本落盘前不在 entries 里，以"未保存"草稿行显示，避免被误读成"创建了但列表不显示"
            bool hasDraftRow = Target.isEditing && Target.isNewEntry && Target.workingCopy != null;

            // 列表区域：独立区块（标题 + 盒式背景），滚动区固定五行高，不随条数收缩
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical("HelpBox");
            EditorGUILayout.LabelField($"地块条目列表（{entries.Count} 条{(hasDraftRow ? "，+1 未保存" : "")}）",
                                       EditorStyles.boldLabel);

            float rowHeight = EditorGUIUtility.singleLineHeight + 10f;   // box 行含内外边距的近似高度
            float viewHeight = EntriesPerPage * rowHeight + 8f;
            m_EntryListScroll = EditorGUILayout.BeginScrollView(m_EntryListScroll, GUILayout.Height(viewHeight));
            int drawnRows = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!MatchesSearch(entry)) continue;

                // 行醒目化：正在编辑的行蓝色高亮（▶ 前缀 + 加粗），其余行深浅交替（斑马纹）
                bool selected = Target.isEditing && !Target.isNewEntry && Target.editingIndex == i;
                var prevBackground = GUI.backgroundColor;
                GUI.backgroundColor = selected ? new Color(0.35f, 0.65f, 1f)
                    : drawnRows % 2 == 0 ? Color.white : new Color(0.65f, 0.65f, 0.65f);
                EditorGUILayout.BeginHorizontal("box");
                GUI.backgroundColor = prevBackground;
                drawnRows++;
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
                        OnEntryDeleted(i);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndScrollView();
                        EditorGUILayout.EndVertical();
                        GUIUtility.ExitGUI(); // 列表已变更，中断本帧绘制避免下标错乱
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            // 新建草稿行：橙黄底 + "未保存"标记（与蓝色编辑选中区分），保存成功后转为正式行
            if (hasDraftRow)
            {
                var wc = Target.workingCopy;
                var prevBackground = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.85f, 0.4f);
                EditorGUILayout.BeginHorizontal("box");
                GUI.backgroundColor = prevBackground;
                GUILayout.Label($"▶ ID: {wc.id} ｜ 长{wc.len} 宽{wc.width}（新建，未保存）", EditorStyles.boldLabel);
                EditorGUILayout.EndHorizontal();
            }
            // 空态提示画在固定高度的滚动区内，面板尺寸保持稳定
            if (entries.Count == 0 && !hasDraftRow)
                EditorGUILayout.HelpBox("暂无条目", MessageType.None);
            else if (drawnRows == 0 && !hasDraftRow)
                EditorGUILayout.HelpBox("无匹配搜索的条目", MessageType.None);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }
    }
}
