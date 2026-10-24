using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MyVerseXRSDK.EditorDemos.Editor
{
    /// <summary>以Editor API创建新示例资产；已有输出或未保存场景均拒绝，不改Build Settings。</summary>
    public static class EditorDemoSceneBuilder
    {
        private const string Root = "Assets/EditorDemoGenerated";
        private static readonly string[] Names = { "01_Connection", "02_Spatial", "03_Business", "04_Streaming", "05_CrossSceneA", "06_CrossSceneB", "07_ControlledStreaming" };

        [MenuItem("MyVerse/Editor接入Demo/生成全新场景副本")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出Play Mode后生成");
            if (Directory.Exists(Root)) throw new InvalidOperationException("输出目录已存在，不覆盖：" + Root);
            PrepareWorkspace();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            try
            {
                var visuals = new EditorDemoVisuals(Root);
                foreach (string name in Names) BuildScene(name, visuals);
                LinkPair();
                Debug.Log("[Editor Demo] 七个独立场景生成完成；未修改Build Settings。");
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
        [MenuItem("MyVerse/Editor接入Demo/仅生成中控推流新场景")]
        public static void GenerateControlledStreaming()
        {
            const string output = "Assets/EditorControlledStreamGenerated";
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出Play Mode后生成");
            if (Directory.Exists(output)) throw new InvalidOperationException("输出已存在，不覆盖：" + output);
            PrepareWorkspace();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            Directory.CreateDirectory(output + "/Scenes"); AssetDatabase.Refresh();
            try
            {
                string shaderPath = AssetDatabase.GetAssetPath(Shader.Find("MyVerse/EditorDemoUnlit"));
                if (string.IsNullOrEmpty(shaderPath)) throw new InvalidOperationException("缺少示例Shader");
                string sampleRoot = Path.GetDirectoryName(shaderPath).Replace(Path.DirectorySeparatorChar, '/');
                BuildScene("07_ControlledStreaming", new EditorDemoVisuals(sampleRoot, true), output);
                Debug.Log("[Editor Demo] 中控推流新场景生成完成，复用示例材质；原场景未改。");
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
        private static void PrepareWorkspace()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!Application.isBatchMode && (scene.isDirty || string.IsNullOrEmpty(scene.path)))
                    throw new InvalidOperationException("请自行保存或关闭未保存场景；生成器不代为保存");
                if (Application.isBatchMode && string.IsNullOrEmpty(scene.path))
                {
                    string path = AssetDatabase.GenerateUniqueAssetPath("Assets/EditorDemoGenerationWorkspace.unity");
                    if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("隔离生成工作场景保存失败");
                }
            }
        }
        private static void BuildScene(string name, EditorDemoVisuals visuals, string outputRoot = Root)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
                var observer = visuals.CreateWorld();
                if (name != "02_Spatial") observer.rect = new Rect(0, 0, 1, 1);
                var entry = new GameObject("接入入口_" + name);
                switch (name)
                {
                    case "01_Connection": entry.AddComponent<ConnectionExample>(); break;
                    case "02_Spatial":
                        var spatial = entry.AddComponent<SpatialExample>();
                        visuals.CreateRig(out spatial.motionRoot, out spatial.calibrationRoot, out spatial.trackingRoot, out spatial.head,
                            out spatial.leftHand, out spatial.rightHand);
                        break;
                    case "03_Business":
                        entry.AddComponent<BusinessExample>().directorCamera = visuals.CreateSourceCamera();
                        break;
                    case "04_Streaming":
                        var streaming = entry.AddComponent<StreamingExample>();
                        streaming.sourceCamera = visuals.CreateSourceCamera();
                        streaming.gameListener = observer.GetComponent<AudioListener>();
                        streaming.audioSignal = new GameObject("游戏测试音_非麦克风").AddComponent<DemoAudioSignal>();
                        streaming.audioSignal.GetComponent<AudioSource>().playOnAwake = false;
                        break;
                    case "07_ControlledStreaming":
                        var controlled = entry.AddComponent<ControlledStreamingExample>();
                        controlled.sourceCamera = visuals.CreateSourceCamera();
                        controlled.gameListener = observer.GetComponent<AudioListener>();
                        controlled.audioSignal = new GameObject("游戏测试音_非麦克风").AddComponent<DemoAudioSignal>();
                        controlled.audioSignal.GetComponent<AudioSource>().playOnAwake = false;
                        break;
                    default:
                        var cross = entry.AddComponent<CrossSceneExample>();
                        visuals.CreateRig(out cross.motionRoot, out cross.calibrationRoot, out cross.trackingRoot, out cross.head, out _, out _);
                        if (name == "06_CrossSceneB") cross.motionRoot.SetPositionAndRotation(Vector3.right * 2, Quaternion.Euler(0, 30, 0));
                        break;
                }
                if (!EditorSceneManager.SaveScene(scene, outputRoot + "/Scenes/" + name + ".unity")) throw new IOException("场景保存失败");
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        private static void LinkPair()
        {
            for (int index = 4; index < 6; index++)
            {
                string path = Root + "/Scenes/" + Names[index] + ".unity";
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        var example = root.GetComponent<CrossSceneExample>();
                        if (example == null) continue;
                        example.nextScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Root + "/Scenes/" + Names[index == 4 ? 5 : 4] + ".unity");
                        EditorUtility.SetDirty(example);
                    }
                    EditorSceneManager.SaveScene(scene);
                }
                finally { EditorSceneManager.CloseScene(scene, true); }
            }
        }
    }
}
