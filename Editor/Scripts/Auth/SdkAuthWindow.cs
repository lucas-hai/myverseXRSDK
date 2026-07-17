using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    // 别名必须在 namespace 内：InternalsVisibleTo 使 Socket 的 internal MyVerseXRSDK.MessageType 可见，
    // 父命名空间成员优先于文件顶层 using——只有本层 alias 才能压过它，把 MessageType 解析回 HelpBox 枚举
    using MessageType = UnityEditor.MessageType;

    /// <summary>
    /// SDK 设置面板：环境选择（正式/测试/离线，默认正式）+ 各环境独立凭据验证 + 地块重新封签。
    /// 环境地址内置只读（防手滑连错环境）；切换环境同步写入地块数据资产（运行时按它取对应环境组），
    /// 面板打开期间还会持续对账两资产的环境标记（兜住直接在 Inspector 手改设置资产的场景）。
    /// 离线环境无验证概念：远端功能不可用，地块仅本地保存。
    /// 资产引用与封签校验结果做窗口级缓存（OnGUI 每事件触发，直查 AssetDatabase/算 HMAC 太浪费）。
    /// </summary>
    public sealed class SdkAuthWindow : EditorWindow
    {
        private static readonly SdkEnvironment[] k_EnvOrder =
            { SdkEnvironment.Production, SdkEnvironment.Test, SdkEnvironment.Offline };
        // 显示名统一走 SdkAuthStore.EnvDisplayName，避免两处文案漂移
        private static readonly string[] k_EnvNames = BuildEnvNames();

        private static string[] BuildEnvNames()
        {
            var names = new string[k_EnvOrder.Length];
            for (int i = 0; i < names.Length; i++) names[i] = SdkAuthStore.EnvDisplayName(k_EnvOrder[i]);
            return names;
        }

        private MVXRSDKSettingsAsset m_Settings;   // 窗口级缓存（OnEnable/OnFocus 刷新）
        private LocalRegionDataList m_DataList;
        private bool m_SealStateDirty = true;      // 封签校验结果缓存失效标记（HMAC 别每帧算）
        private bool m_SealValid;

        private string m_AppId;          // 当前环境凭据的编辑缓冲（切环境时重载）
        private string m_AccessToken;
        private string m_ErrorMessage;
        private bool m_Verifying;
        private bool m_Resealing;

        // 本工程所有 SDK 的编辑器工具统一挂顶级菜单 MyVerse（与 MyVerse/Mocap 系对齐）
        [MenuItem("MyVerse/XRSDK/SDK 验证")]
        public static void Open()
        {
            var window = GetWindow<SdkAuthWindow>(utility: true, title: "MyVerse XRSDK 设置");
            window.minSize = new Vector2(440f, 260f);
            window.RefreshCachedRefs();
            window.LoadFromSettings();
            window.Show();
        }

        private void OnEnable()
        {
            RefreshCachedRefs();
            LoadFromSettings();
        }

        private void OnFocus()
        {
            // 资产可能在窗口失焦期间被外部改动（验证面板常驻开着的场景）
            RefreshCachedRefs();
        }

        // 缓存资产引用 + 触发旧版数据迁移（幂等）。设置资产不存在则创建（面板本职就是写它）
        private void RefreshCachedRefs()
        {
            m_Settings = SdkAuthStore.GetOrCreateSettings();
            m_DataList = RegionDataMigration.FindDataList();
            RegionDataMigration.MigrateIfNeeded(m_DataList);
            m_SealStateDirty = true;
        }

        // 回填当前激活环境的凭据到输入缓冲（打开面板/切换环境时）
        private void LoadFromSettings()
        {
            m_AppId = null;
            m_AccessToken = null;
            m_ErrorMessage = null;
            var cred = m_Settings != null ? m_Settings.GetCredential(m_Settings.activeEnvironment) : null;
            if (cred == null) return;
            m_AppId = cred.appId;
            m_AccessToken = cred.accessToken;
        }

        private void OnGUI()
        {
            if (m_Settings == null) RefreshCachedRefs();   // 资产被删后自愈
            var settings = m_Settings;
            var env = settings.activeEnvironment;

            EditorGUILayout.Space(4f);

            // ------ 环境选择（切换同步 settings + 地块资产）------
            int envIndex = System.Array.IndexOf(k_EnvOrder, env);
            if (envIndex < 0) envIndex = 0;
            int newIndex = EditorGUILayout.Popup("环境", envIndex, k_EnvNames);
            if (newIndex != envIndex)
            {
                // 输入缓冲有未验证的改动时切环境会丢（缓冲按环境重载）——先确认，取消则下帧画回原环境
                if (HasUnsavedCredentialInput(settings, env) &&
                    !EditorUtility.DisplayDialog("未验证的凭据输入",
                        $"{SdkAuthStore.EnvDisplayName(env)}有已输入但未验证的凭据，切换环境将丢弃这些输入。确认切换？",
                        "切换", "取消"))
                {
                    return;
                }
                env = k_EnvOrder[newIndex];
                Undo.RecordObject(settings, "切换 SDK 环境");
                settings.activeEnvironment = env;
                EditorUtility.SetDirty(settings);
                RegionDataMigration.SyncActiveEnvironment(m_DataList, env);
                AssetDatabase.SaveAssets();
                m_SealStateDirty = true;
                LoadFromSettings();   // 输入缓冲切到新环境的凭据
                GUI.FocusControl(null);
            }
            // 持续对账：设置资产可能被直接在 Inspector 改了 activeEnvironment（不经本面板），
            // 地块资产标记会漂移——窗口开着就拉齐（字段比较廉价，仅不一致时写盘）
            else if (m_DataList != null && m_DataList.activeEnvironment != env)
            {
                RegionDataMigration.SyncActiveEnvironment(m_DataList, env);
                AssetDatabase.SaveAssets();
                m_SealStateDirty = true;
            }

            // ------ 地址（内置只读；override 仅在配置了时提示）------
            var url = settings.GetServerUrl(env);
            EditorGUILayout.LabelField("服务地址", env == SdkEnvironment.Offline ? "无（离线模式）" : url);
            if (env != SdkEnvironment.Offline && !string.IsNullOrWhiteSpace(settings.serverUrlOverride))
                EditorGUILayout.HelpBox($"地址已被设置资产的 serverUrlOverride 覆盖：{url}", MessageType.Warning);

            EditorGUILayout.Space(4f);

            if (env == SdkEnvironment.Offline)
            {
                EditorGUILayout.HelpBox(
                    "离线模式：验证与远端功能（规格拉取/地块上传）不可用。\n" +
                    "地块编辑器改为仅本地保存（无防篡改封签），新建条目需手填 id。", MessageType.Info);
                return;
            }

            // ------ 当前环境凭据 + 验证 ------
            var cred = settings.GetCredential(env);
            EditorGUILayout.LabelField($"凭据（{SdkAuthStore.EnvDisplayName(env)}独立保存）", EditorStyles.boldLabel);
            m_AppId = EditorGUILayout.TextField("AppId", m_AppId);
            m_AccessToken = EditorGUILayout.PasswordField("AccessToken", m_AccessToken);

            if (cred != null && cred.verified)
                EditorGUILayout.HelpBox($"{SdkAuthStore.EnvDisplayName(env)}已验证通过。重新验证会覆盖该环境已保存的凭据。", MessageType.Info);
            else
                EditorGUILayout.HelpBox($"{SdkAuthStore.EnvDisplayName(env)}尚未验证。", MessageType.Warning);
            if (!string.IsNullOrEmpty(m_ErrorMessage))
                EditorGUILayout.HelpBox(m_ErrorMessage, MessageType.Error);

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(m_Verifying || m_Resealing))
            {
                if (GUILayout.Button(m_Verifying ? "验证中…" : "验证", GUILayout.Height(26f)))
                    DoVerify(settings, env);
            }

            DrawResealSection(env, cred);
        }

        private void DoVerify(MVXRSDKSettingsAsset settings, SdkEnvironment env)
        {
            m_Verifying = true;
            m_ErrorMessage = null;
            var serverUrl = settings.GetServerUrl(env);
            var appId = m_AppId?.Trim();
            var accessToken = m_AccessToken?.Trim();

            SdkAuthServices.Verifier.Verify(serverUrl, appId, accessToken, (ok, message) =>
            {
                if (ok)
                {
                    // 重新查找而非闭包捕获：回调期间资产可能被删除重建
                    var target = SdkAuthStore.GetOrCreateSettings();
                    var cred = target.GetCredential(env);
                    if (cred != null)
                    {
                        cred.appId = appId;
                        cred.accessToken = accessToken;
                        cred.verified = true;
                        EditorUtility.SetDirty(target);
                        AssetDatabase.SaveAssets();
                    }
                }
                // 异步回调期间窗口可能已被关闭（Unity 假 null）：结果已落盘，仅跳过 UI 更新
                if (this == null)
                {
                    if (!ok) Debug.LogWarning($"[MVXRSDK] SDK 验证失败：{message}");
                    return;
                }
                m_Verifying = false;
                if (!ok)
                {
                    m_ErrorMessage = $"验证失败：{message}";
                    Repaint();
                    return;
                }
                m_ErrorMessage = null;
                ShowNotification(new GUIContent("验证通过"));
                Repaint();
            });
        }

        // ------ 重新封签：迁移/手改后的存量地块逐条重传，全部成功后写回签名 ------

        private void DrawResealSection(SdkEnvironment env, MVXRSDKSettingsAsset.EnvCredential cred)
        {
            var set = m_DataList != null ? m_DataList.GetSet(env) : null;
            if (set == null || set.entries.Count == 0) return;

            // 封签校验结果缓存：只在标记失效时重算（HMAC 别跟着 OnGUI 每事件跑）
            if (m_SealStateDirty)
            {
                m_SealStateDirty = false;
                m_SealValid = RegionDataSeal.Verify(set);
            }

            EditorGUILayout.Space(8f);
            if (m_SealValid)
            {
                EditorGUILayout.HelpBox($"当前环境地块（{set.entries.Count} 条）封签有效。", MessageType.Info);
                return;
            }
            EditorGUILayout.HelpBox(
                $"当前环境地块（{set.entries.Count} 条）无有效封签（旧版迁移数据或被手改），运行时将整组拒用。\n" +
                "重新封签会把全部条目重新上传远端，成功后写入签名。", MessageType.Warning);
            using (new EditorGUI.DisabledScope(m_Resealing || m_Verifying || cred == null || !cred.verified))
            {
                if (GUILayout.Button(m_Resealing ? "封签中…" : "重新封签（逐条上传）", GUILayout.Height(24f)))
                    DoReseal(env, m_DataList, set);
            }
            if (cred == null || !cred.verified)
                EditorGUILayout.HelpBox("重新封签需先完成当前环境验证。", MessageType.None);
        }

        private void DoReseal(SdkEnvironment env, LocalRegionDataList dataList, EnvironmentTileSet set)
        {
            m_Resealing = true;
            m_ErrorMessage = null;
            // 上传用开始时的快照：封签期间条目列表可能被地块编辑器（另一个未冻结的 Inspector）增删，
            // 直接按 live 列表索引递进会漏传/越界
            var snapshot = new List<LocalRegionData>(set.entries);
            UploadNext(0);

            void UploadNext(int index)
            {
                // 资产可能在异步链中途被删除/重导入（Unity 假 null），落盘目标没了就中止
                if (dataList == null)
                {
                    Finish(false, "地块数据资产已失效（被删除/重导入），签名未写入");
                    return;
                }
                if (index >= snapshot.Count)
                {
                    // 全部上传成功。若期间条目被改动，快照与现状不一致——签了等于给未上传的数据背书，拒签
                    if (!EntriesUnchanged(set.entries, snapshot))
                    {
                        Finish(false, "封签期间地块条目被修改，签名未写入——请重新执行封签");
                        return;
                    }
                    RegionDataSeal.Reseal(set);
                    EditorUtility.SetDirty(dataList);
                    AssetDatabase.SaveAssets();
                    Finish(true, null);
                    Debug.Log($"[MVXRSDK] {SdkAuthStore.EnvDisplayName(env)}地块重新封签完成（{snapshot.Count} 条）");
                    return;
                }
                RegionToolServices.Uploader.Upload(snapshot[index],
                    onDone: () => UploadNext(index + 1),
                    onError: error => Finish(false, $"重新封签中断：条目 [{snapshot[index].id}] 上传失败：{error}（签名未写入，可重试）"));
            }

            void Finish(bool ok, string error)
            {
                if (this != null)
                {
                    m_Resealing = false;
                    m_SealStateDirty = true;
                    if (ok) ShowNotification(new GUIContent("封签完成"));
                    else m_ErrorMessage = error;
                    Repaint();
                }
                else if (!ok)
                {
                    Debug.LogError($"[MVXRSDK] {error}");
                }
            }
        }

        // 输入缓冲是否有未落盘（未验证）的凭据改动——切环境前的丢失保护判据
        private bool HasUnsavedCredentialInput(MVXRSDKSettingsAsset settings, SdkEnvironment env)
        {
            var cred = settings.GetCredential(env);
            if (cred == null) return false;   // Offline 无凭据输入
            return !string.Equals(m_AppId ?? "", cred.appId ?? "") ||
                   !string.Equals(m_AccessToken ?? "", cred.accessToken ?? "");
        }

        // 快照一致性：数量相同且逐项同一引用（编辑器保存/删除都会替换或增删元素，引用比对足够灵敏）
        private static bool EntriesUnchanged(List<LocalRegionData> current, List<LocalRegionData> snapshot)
        {
            if (current.Count != snapshot.Count) return false;
            for (int i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(current[i], snapshot[i])) return false;
            }
            return true;
        }
    }
}
