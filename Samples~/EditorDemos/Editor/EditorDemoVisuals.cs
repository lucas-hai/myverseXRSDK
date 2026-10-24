using System;
using UnityEditor;
using UnityEngine;

namespace MyVerseXRSDK.EditorDemos.Editor
{
    internal sealed class EditorDemoVisuals
    {
        private readonly Material floor, grid, red, blue, green, amber;
        internal EditorDemoVisuals(string root, bool loadExisting = false)
        {
            floor = Material(root, loadExisting, "Floor", new Color(0.08f, 0.12f, 0.17f));
            grid = Material(root, loadExisting, "Grid", new Color(0.2f, 0.28f, 0.35f));
            red = Material(root, loadExisting, "X", new Color(0.9f, 0.25f, 0.3f));
            blue = Material(root, loadExisting, "Z", new Color(0.15f, 0.55f, 0.95f));
            green = Material(root, loadExisting, "Head", new Color(0.2f, 0.9f, 0.65f));
            amber = Material(root, loadExisting, "Landmark", new Color(1, 0.65f, 0.15f));
        }
        private static Material Material(string root, bool loadExisting, string name, Color color)
        {
            if (loadExisting)
            {
                var existing = AssetDatabase.LoadAssetAtPath<Material>(root + "/Materials/" + name + ".mat");
                if (existing == null) throw new InvalidOperationException("缺少可复用示例材质：" + name);
                return existing;
            }
            var shader = Shader.Find("MyVerse/EditorDemoUnlit");
            if (shader == null) throw new InvalidOperationException("缺少Editor Demo材质Shader");
            var material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, root + "/Materials/" + name + ".mat");
            return material;
        }
        internal Camera CreateWorld()
        {
            var map = new GameObject("G_Map_不随SDK移动").transform;
            Box("Ground", map, new Vector3(0, -0.08f, 0), new Vector3(14, 0.1f, 14), floor);
            for (int index = -6; index <= 6; index++)
            {
                Box("GridX", map, new Vector3(0, -0.02f, index), new Vector3(12, 0.01f, 0.015f), grid);
                Box("GridZ", map, new Vector3(index, -0.02f, 0), new Vector3(0.015f, 0.01f, 12), grid);
            }
            Box("G_X", map, new Vector3(1.5f, 0, 0), new Vector3(3, 0.04f, 0.04f), red);
            Box("G_Z", map, new Vector3(0, 0, 1.5f), new Vector3(0.04f, 0.04f, 3), blue);
            Box("MapLandmark", map, new Vector3(2.5f, 0.5f, 2.5f), new Vector3(0.6f, 1, 0.6f), amber);
            var camera = new GameObject("Editor观察相机_不作为推流源").AddComponent<Camera>();
            camera.tag = "MainCamera"; camera.gameObject.AddComponent<AudioListener>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.035f, 0.05f, 0.075f);
            camera.transform.position = new Vector3(8, 10, -10); camera.transform.LookAt(Vector3.zero);
            camera.rect = new Rect(0.36f, 0, 0.64f, 1); camera.fieldOfView = 52;
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1; light.transform.rotation = Quaternion.Euler(45, -30, 0);
            return camera;
        }
        internal Camera CreateSourceCamera()
        {
            var camera = new GameObject("独立推流相机_普通RT").AddComponent<Camera>();
            camera.transform.position = new Vector3(4, 4, -6); camera.transform.LookAt(new Vector3(0, 0.5f, 1));
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.05f, 0.1f, 0.18f);
            camera.enabled = false; camera.stereoTargetEye = StereoTargetEyeMask.None;
            return camera;
        }
        internal void CreateRig(out Transform motion, out Transform calibration, out Transform tracking, out Transform head,
            out Transform leftHand, out Transform rightHand)
        {
            motion = Node("MotionRoot_B", null, Vector3.zero);
            calibration = Node("CalibrationRoot_C_SDK独占", motion, Vector3.zero);
            tracking = Node("TrackingRoot_普通Transform", calibration, Vector3.zero);
            head = Node("Head_普通Transform", tracking, new Vector3(0, 1.6f, 0));
            leftHand = Node("LeftHand", tracking, new Vector3(-0.35f, 1.2f, 0.3f));
            rightHand = Node("RightHand", tracking, new Vector3(0.35f, 1.2f, 0.3f));
            Box("HeadMarker", head, Vector3.zero, Vector3.one * 0.22f, green);
            Box("Forward", head, Vector3.forward * 0.25f, new Vector3(0.07f, 0.07f, 0.3f), blue);
            Box("HandMarker", leftHand, Vector3.zero, Vector3.one * 0.12f, red);
            Box("HandMarker", rightHand, Vector3.zero, Vector3.one * 0.12f, blue);
        }
        private static Transform Node(string name, Transform parent, Vector3 position)
        {
            var node = new GameObject(name).transform; node.SetParent(parent, false); node.localPosition = position; return node;
        }
        private static void Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
            box.transform.SetParent(parent, false); box.transform.localPosition = position; box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(box.GetComponent<Collider>());
        }
    }
}
