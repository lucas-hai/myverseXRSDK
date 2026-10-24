using System;
using UnityEngine;

namespace MyVerseXRSDK
{
    /// <summary>只观察会话终态和XR绑定的默认故障提示，不参与重连或持有业务节点所有权。</summary>
    internal sealed class NetworkFailurePresenter : IDisposable
    {
        private readonly SessionService session;
        private readonly SpatialService space;
        private GameObject root;
        private Material backgroundMaterial;
        private Material textMaterial;
        private bool disposed;

        internal NetworkFailurePresenter(SessionService session, SpatialService space)
        {
            this.session = session;
            this.space = space;
            session.Changed += OnSession;
            space.Changed += OnSpace;
            space.BindingChanging += Hide;
        }

        private void OnSession(SessionSnapshot _) => Refresh();
        private void OnSpace(SpatialStatus _) => Refresh();

        private void Refresh()
        {
            var head = space.HudAnchor;
            if (disposed || session.State != SessionState.Failed || head == null) { Hide(); return; }
            if (root != null && root.transform.parent == head) return;
            Hide();
            try
            {
                root = new GameObject("MVXRSDK.NetworkFailureHUD") { hideFlags = HideFlags.DontSave };
                root.transform.SetParent(head, false);
                root.transform.localPosition = Vector3.forward;
                CreateBackground();
                CreateText();
            }
            catch
            {
                Hide();
                throw;
            }
        }

        private void CreateBackground()
        {
            var template = Resources.Load<Material>("MVXRSDK/RegionTileMaterial");
            if (template == null) throw new InvalidOperationException("默认故障HUD缺少包内Unlit材质");
            backgroundMaterial = new Material(template) { hideFlags = HideFlags.DontSave, renderQueue = 4999 };
            var color = new Color(0, 0, 0, 0.9f);
            if (backgroundMaterial.HasProperty("_BaseColor")) backgroundMaterial.SetColor("_BaseColor", color);
            if (backgroundMaterial.HasProperty("_Color")) backgroundMaterial.SetColor("_Color", color);
            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Background";
            background.transform.SetParent(root.transform, false);
            background.transform.localPosition = new Vector3(0, 0, 0.1f);
            background.transform.localScale = new Vector3(1.6f, 0.8f, 1);
            SpatialContext.Destroy(background.GetComponent<Collider>());
            background.GetComponent<MeshRenderer>().sharedMaterial = backgroundMaterial;
        }

        private void CreateText()
        {
            var label = new GameObject("Text");
            label.transform.SetParent(root.transform, false);
            label.transform.localScale = Vector3.one * 0.02f;
            var text = label.AddComponent<TextMesh>();
            text.text = "网络连接失败，请退出重试";
            text.fontSize = 200;
            text.color = Color.white;
            text.alignment = TextAlignment.Center;
            text.anchor = TextAnchor.MiddleCenter;
            text.characterSize = 0.2f;
            var renderer = label.GetComponent<MeshRenderer>();
            if (renderer.sharedMaterial == null) return;
            textMaterial = new Material(renderer.sharedMaterial) { hideFlags = HideFlags.DontSave, renderQueue = 5000 };
            renderer.sharedMaterial = textMaterial;
        }

        private void Hide()
        {
            if (root != null) { root.SetActive(false); SpatialContext.Destroy(root); }
            if (backgroundMaterial != null) SpatialContext.Destroy(backgroundMaterial);
            if (textMaterial != null) SpatialContext.Destroy(textMaterial);
            root = null;
            backgroundMaterial = textMaterial = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            session.Changed -= OnSession;
            space.Changed -= OnSpace;
            space.BindingChanging -= Hide;
            Hide();
        }
    }
}
