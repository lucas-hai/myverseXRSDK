using System;
using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos.Editor
{
    /// <summary>现场 WHIP 地址只保存在当前 Inspector，不参与场景、撤销记录或程序集热重载序列化。</summary>
    [CustomEditor(typeof(StreamingExample))]
    internal sealed class StreamingExampleInspector : UnityEditor.Editor
    {
        [NonSerialized] private string temporaryUrl = "";

        private void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeChanged;
        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            temporaryUrl = "";
        }
        private void OnPlayModeChanged(PlayModeStateChange state) => temporaryUrl = "";

        public override void OnInspectorGUI()
        {
            var example = (StreamingExample)target;
            if (!Application.IsPlaying(example.gameObject)) { DrawDefaultInspector(); return; }
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "whipUrl");
            serializedObject.ApplyModifiedProperties();
            temporaryUrl = EditorGUILayout.PasswordField("临时 WHIP 地址", temporaryUrl);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(temporaryUrl)))
                if (GUILayout.Button("使用临时地址开始直推")) SubmitTemporaryUrl();
        }

        private void SubmitTemporaryUrl()
        {
            string url = temporaryUrl;
            temporaryUrl = "";
            ((StreamingExample)target).SubmitWhip(url);
        }
    }
}
