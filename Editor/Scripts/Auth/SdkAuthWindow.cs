using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.Editor
{
    /// <summary>SDK 验证面板：输入 appid + 验证密钥，通过后写入 MVXRSDKSettingsAsset。</summary>
    public sealed class SdkAuthWindow : EditorWindow
    {
        private string m_AppId;
        private string m_SecretKey;
        private string m_ErrorMessage;

        [MenuItem("Tools/MyVerse XRSDK/SDK 验证")]
        public static void Open()
        {
            var window = GetWindow<SdkAuthWindow>(utility: true, title: "MyVerse XRSDK 验证");
            window.minSize = new Vector2(380f, 150f);
            window.LoadFromSettings();
            window.Show();
        }

        // 已有设置时回填输入框，便于查看/更新凭据
        private void LoadFromSettings()
        {
            var settings = SdkAuthStore.FindSettings();
            if (settings == null) return;
            m_AppId = settings.appId;
            m_SecretKey = settings.secretKey;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("请输入 appid 与验证密钥完成 SDK 验证（区域规格拉取与地块上传需要）",
                                       EditorStyles.wordWrappedLabel);
            m_AppId = EditorGUILayout.TextField("AppId", m_AppId);
            m_SecretKey = EditorGUILayout.PasswordField("验证密钥", m_SecretKey);

            if (SdkAuthStore.IsVerified)
                EditorGUILayout.HelpBox("当前项目已验证通过。重新验证会覆盖已保存的凭据。", MessageType.Info);
            if (!string.IsNullOrEmpty(m_ErrorMessage))
                EditorGUILayout.HelpBox(m_ErrorMessage, MessageType.Error);

            EditorGUILayout.Space(4f);
            if (GUILayout.Button("验证", GUILayout.Height(26f)))
                DoVerify();
        }

        private void DoVerify()
        {
            SdkAuthServices.Verifier.Verify(m_AppId, m_SecretKey, (ok, message) =>
            {
                if (!ok)
                {
                    m_ErrorMessage = $"验证失败：{message}";
                    Repaint();
                    return;
                }
                var settings = SdkAuthStore.GetOrCreateSettings();
                settings.appId = m_AppId;
                settings.secretKey = m_SecretKey;
                settings.verified = true;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                m_ErrorMessage = null;
                ShowNotification(new GUIContent("验证通过"));
                Repaint();
            });
        }
    }
}
