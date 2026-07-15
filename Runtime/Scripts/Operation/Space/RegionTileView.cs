using UnityEngine;
using UnityEngine.Rendering;

namespace MyVerseXRSDK
{
    /// <summary>
    /// 运行时本地区域地块可视物：自发光（Unlit，不受场景光照/烘焙影响）半透明贴地矩形。
    /// 由 RegionTileModule 实例化并挂在 XR 偏移节点下，按匹配地块的长宽/位姿显示。
    /// 尺寸走 localScale（单位 quad ×(宽,1,长)），随地块热更新无需重建 mesh。
    /// 自发光的意义：Unlit 不依赖 lightmap，第三方烘焙光工程里动态实例化也照常可见。
    /// 材质走包内预置 Resources 资产（<see cref="MaterialResourcesPath"/>）而非运行时 Shader.Find——
    /// 序列化引用强制 URP Unlit shader 进 build，规避第三方工程里 shader 被剥离导致地块不显示。
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class RegionTileView : MonoBehaviour
    {
        // 默认外观：半透明青蓝，自发光观感（后续如需可调可外露）
        private static readonly Color DefaultColor = new Color(0f, 0.85f, 1f, 0.6f);

        // 包内预置材质（URP Unlit 透明自发光）。序列化引用 → 强制 shader 进 build，规避运行时
        // Shader.Find 在 build 里被剥离（尤其第三方烘焙光工程不用 URP/Unlit 时）导致地块不显示。
        private const string MaterialResourcesPath = "MVXRSDK/RegionTileMaterial";

        private static Mesh s_UnitQuad;   // 单位 quad 静态共享
        private Material m_Material;       // 每实例一份，OnDestroy 清理

        private void Awake()
        {
            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = GetUnitQuad();

            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode    = ShadowCastingMode.Off;
            mr.receiveShadows       = false;
            mr.lightProbeUsage      = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            m_Material = LoadOrCreateMaterial(DefaultColor);
            if (m_Material != null) mr.sharedMaterial = m_Material;
        }

        /// <summary>按地块长宽/位姿更新可视物（挂在 XR 偏移节点下，用地块 local 坐标）。</summary>
        public void Apply(LocalRegionData tile)
        {
            transform.localPosition    = tile.position;
            transform.localEulerAngles = tile.rotation;
            // 宽沿本地 X、长沿本地 Z（与编辑器底框、区域约定一致）
            transform.localScale = new Vector3(Mathf.Abs(tile.width), 1f, Mathf.Abs(tile.len));
        }

        private void OnDestroy()
        {
            if (m_Material != null) Destroy(m_Material);
        }

        // 单位 quad（1×1，XZ 平面，中心原点）；cull off 双面渲染，绕序/法线不影响 Unlit 观感
        private static Mesh GetUnitQuad()
        {
            if (s_UnitQuad != null) return s_UnitQuad;
            var mesh = new Mesh { name = "MVXR_RegionTileQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f,  0.5f),
                new Vector3(-0.5f, 0f,  0.5f),
            };
            mesh.uv       = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.normals  = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            s_UnitQuad = mesh;
            return mesh;
        }

        // 材质来源：优先加载包内预置材质（强制 URP Unlit shader 进 build），实例化后按 color 覆盖颜色；
        // 实例化而非直接用资产 → OnDestroy 可安全销毁、不污染 Resources 资产本体。
        // 仅在预置材质缺失（被误删等）时才退化到运行时构造。
        private static Material LoadOrCreateMaterial(Color color)
        {
            var shipped = Resources.Load<Material>(MaterialResourcesPath);
            if (shipped != null)
            {
                var mat = new Material(shipped);
                mat.SetColor("_BaseColor", color);
                mat.SetColor("_Color", color);   // 兼容部分 URP 版本的属性名
                return mat;
            }
            MVXRSDKLog.Warning($"RegionTileView: 未找到包内材质 Resources/{MaterialResourcesPath}，退化到运行时构造" +
                               "（build 里 URP Unlit 可能被剥离而不渲染）");
            return CreateEmissiveMaterial(color);
        }

        // 兜底：运行时构造自发光半透明材质。仅在包内预置材质缺失时走到；
        // 注意 Shader.Find 在 build 里若 shader 被剥离会返回 null，故正常路径应走上面的预置材质。
        private static Material CreateEmissiveMaterial(Color color)
        {
            var urp = Shader.Find("Universal Render Pipeline/Unlit");
            if (urp != null)
            {
                var mat = new Material(urp);
                mat.SetColor("_BaseColor", color);
                mat.SetColor("_Color", color);                         // 兼容部分 URP 版本的属性名
                mat.SetFloat("_Surface", 1f);                          // Transparent
                mat.SetFloat("_Blend", 0f);                            // Alpha
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", (float)CullMode.Off);            // 双面
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
                mat.SetOverrideTag("RenderType", "Transparent");
                MVXRSDKLog.Info("RegionTileView: 使用 URP Unlit 材质（透明自发光）");
                return mat;
            }

            // 兜底：内置 always-included，unlit 双面 alpha 混合（不会被 shader stripping 剔除）
            var sprite = Shader.Find("Sprites/Default");
            if (sprite != null)
            {
                var mat = new Material(sprite) { color = color };
                mat.renderQueue = (int)RenderQueue.Transparent;
                MVXRSDKLog.Warning("RegionTileView: 未找到 URP Unlit，退化到 Sprites/Default（URP 下可能不渲染，请检查管线）");
                return mat;
            }

            MVXRSDKLog.Warning("RegionTileView: 未找到可用 Unlit shader，地块可视物将不可见");
            return null;
        }
    }
}
