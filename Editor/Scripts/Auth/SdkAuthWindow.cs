using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>SDK 验证面板：输入服务器地址 + appId + AccessToken，校验通过后写入 MVXRSDKSettingsAsset。</summary>
    public sealed class SdkAuthWindow : EditorWindow
    {
        private string m_ServerUrl;
        private string m_AppId;
        private string m_AccessToken;
        private string m_ErrorMessage;
        private bool m_Verifying;

        [MenuItem("Tools/MyVerse XRSDK/SDK 验证")]
        public static void Open()
        {
            var window = GetWindow<SdkAuthWindow>(utility: true, title: "MyVerse XRSDK 验证");
            window.minSize = new Vector2(420f, 190f);
            window.LoadFromSettings();
            window.Show();
        }

        // 已有设置时回填输入框，便于查看/更新凭据
        private void LoadFromSettings()
        {
            m_ServerUrl = MVXRSDKSettingsAsset.DefaultServerUrl;
            var settings = SdkAuthStore.FindSettings();
            if (settings == null) return;
            m_ServerUrl = settings.ResolveServerUrl();
            m_AppId = settings.appId;
            m_AccessToken = settings.accessToken;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("请输入 appId 与开发者平台生成的 AccessToken 完成 SDK 验证（区域规格拉取与地块上传需要）",
                                       EditorStyles.wordWrappedLabel);
            m_ServerUrl = EditorGUILayout.TextField("服务器地址", m_ServerUrl);
            m_AppId = EditorGUILayout.TextField("AppId", m_AppId);
            m_AccessToken = EditorGUILayout.PasswordField("AccessToken", m_AccessToken);

            if (SdkAuthStore.IsVerified)
                EditorGUILayout.HelpBox("当前项目已验证通过。重新验证会覆盖已保存的凭据。", MessageType.Info);
            if (!string.IsNullOrEmpty(m_ErrorMessage))
                EditorGUILayout.HelpBox(m_ErrorMessage, MessageType.Error);

            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(m_Verifying))
            {
                if (GUILayout.Button(m_Verifying ? "验证中…" : "验证", GUILayout.Height(26f)))
                    DoVerify();
            }
        }

        private void DoVerify()
        {
            m_Verifying = true;
            m_ErrorMessage = null;
            var serverUrl = string.IsNullOrWhiteSpace(m_ServerUrl)
                ? MVXRSDKSettingsAsset.DefaultServerUrl
                : m_ServerUrl.Trim();
            var appId = m_AppId?.Trim();
            var accessToken = m_AccessToken?.Trim();

            SdkAuthServices.Verifier.Verify(serverUrl, appId, accessToken, (ok, message) =>
            {
                if (ok)
                {
                    var settings = SdkAuthStore.GetOrCreateSettings();
                    settings.serverUrl = serverUrl;
                    settings.appId = appId;
                    settings.accessToken = accessToken;
                    settings.verified = true;
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
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
    }
}
