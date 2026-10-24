using System;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos
{
    /// <summary>仅绘制普通RT的只读预览，不拥有客户端、源或纹理。</summary>
    internal sealed class DemoGui : IDisposable
    {
        private Font font;
        private GUIStyle label;

        internal void DrawPreview(RenderTexture texture)
        {
            if (texture == null || !texture.IsCreated() || Screen.width < 64 || Screen.height < 100) return;
            EnsureStyles();
            GUI.Label(new Rect(16, 16, Mathf.Min(440, Screen.width - 32), 40), "本地源预览，不代表编码/网络已成功", label);
            GUI.DrawTexture(new Rect(16, 60, Mathf.Min(440, Screen.width - 32), Mathf.Min(Screen.height - 76, 248)), texture, ScaleMode.ScaleToFit);
        }
        private void EnsureStyles()
        {
            if (label != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 16);
            label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 16, wordWrap = true, richText = false };

        }
        public void Dispose()
        {
            if (font != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(font);
                else UnityEngine.Object.DestroyImmediate(font);
            }
            font = null; label = null;
        }
    }
}
