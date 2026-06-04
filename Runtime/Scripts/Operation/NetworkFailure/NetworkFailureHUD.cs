using UnityEngine;

namespace MyVerseXRSDK
{
    internal static class NetworkFailureHUD
    {
        private static GameObject m_HudRoot;

        public static void Init()
        {
            EventSystem.AddEventListener(MVXRSDKEventType.SOCKET_RECONNECT_FAILED, Show);
        }

        public static void UnInit()
        {
            EventSystem.RemoveEventListener(MVXRSDKEventType.SOCKET_RECONNECT_FAILED, Show);
            Hide();
        }

        private static void Show()
        {
            if (m_HudRoot != null) return;

            Transform camera = MVXRSDK.SelfTransform;
            if (camera == null)
            {
                MVXRSDKLog.Warning("NetworkFailureHUD:相机节点未就绪,无法显示网络失败提示");
                return;
            }

            // 容器节点：挂在相机前方 1m，保持 scale=1，避免子级被拉伸
            m_HudRoot = new GameObject("NetworkFailureHUD");
            m_HudRoot.transform.SetParent(camera, false);
            m_HudRoot.transform.localPosition = new Vector3(0f, 0f, 1f);
            m_HudRoot.transform.localRotation = Quaternion.identity;
            m_HudRoot.transform.localScale = Vector3.one;

            // 背景 Quad（容器的子节点，负责控制遮罩尺寸）
            // 放在容器 +Z 方向（远离相机一点），避免与 Text 深度冲突
            var bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgQuad.name = "Background";
            Object.Destroy(bgQuad.GetComponent<MeshCollider>());
            bgQuad.transform.SetParent(m_HudRoot.transform, false);
            bgQuad.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            bgQuad.transform.localRotation = Quaternion.identity;
            bgQuad.transform.localScale = new Vector3(1.6f, 0.8f, 1f);

            // 材质：URP Unlit，降级兼容内置管线
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var mat = new Material(shader)
            {
                color = new Color(0f, 0f, 0f, 0.9f)
            };
            // 关闭深度写入，避免遮挡后续 TextMesh
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 4999;
            bgQuad.GetComponent<MeshRenderer>().material = mat;

            // 文字节点（容器的子节点，位于相机侧，确保在 Quad 前方）
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(m_HudRoot.transform, false);
            textGO.transform.localPosition = Vector3.zero;
            textGO.transform.localRotation = Quaternion.identity;
            textGO.transform.localScale = Vector3.one * 0.02f;

            var textMesh = textGO.AddComponent<TextMesh>();
            textMesh.text = "网络连接失败，请退出重试";
            textMesh.fontSize = 200;
            textMesh.color = Color.white;
            textMesh.alignment = TextAlignment.Center;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.characterSize = 0.2f;

            // 确保文字渲染在遮罩之后
            var textRenderer = textGO.GetComponent<MeshRenderer>();
            if (textRenderer != null)
            {
                textRenderer.material.renderQueue = 5000;
            }

            MVXRSDKLog.Warning("NetworkFailureHUD:网络重连失败,已显示提示遮罩");
        }

        private static void Hide()
        {
            if (m_HudRoot == null) return;
            Object.Destroy(m_HudRoot);
            m_HudRoot = null;
        }
    }
}
