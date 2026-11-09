using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace T34400.VRWebView.Editor
{
    public static class ProbeSceneMenu
    {
        [MenuItem("VRWebView/Create Rich Browser Sample Scene")]
        public static void CreateRichBrowserSample()
        {
            Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("T34400.VRWebView.Samples.Browser." + name)).FirstOrDefault(t => t != null);
            Type builderType = Find("BrowserDesktopPanel"), factoryType = Find("AndroidBrowserPageFactory");
            if (builderType == null || factoryType == null) {
                EditorUtility.DisplayDialog("Import current Browser sample", "Import Browser UI and Model from VRWebView in Package Manager. If an old copy exists, back it up outside Assets before importing this version. Wait for compilation, then run this menu again.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            KeyboardBootProbeSetup.On();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main; if (camera != null) camera.transform.position = Vector3.zero;
            var root = new GameObject("BS1 Browser sample", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(1280, 1000);
            root.transform.position = new Vector3(0, 1.45f, 1.6f); root.transform.localScale = Vector3.one * .0012f;
            var factory = root.AddComponent(factoryType); var factorySettings = new SerializedObject(factory);
            factorySettings.FindProperty("width").intValue = 1280; factorySettings.FindProperty("height").intValue = 800;
            factorySettings.FindProperty("enableDiagnostics").boolValue = true; factorySettings.ApplyModifiedPropertiesWithoutUndo();
            var builder = root.AddComponent(builderType); var builderSettings = new SerializedObject(builder);
            builderSettings.FindProperty("factory").objectReferenceValue = factory;
            builderSettings.FindProperty("worldCamera").objectReferenceValue = camera;
            builderSettings.ApplyModifiedPropertiesWithoutUndo();
            var input = new GameObject("BS1 EventSystem", typeof(EventSystem), typeof(ProbeCanvasInputModule)).GetComponent<ProbeCanvasInputModule>();
            input.targetCanvas = canvas; input.headCamera = camera;
            input.GetComponent<EventSystem>().pixelDragThreshold = 16;
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewPlayground")) AssetDatabase.CreateFolder("Assets", "VRWebViewPlayground");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewPlayground/RichBrowser.unity");
            EditorSceneManager.SaveScene(scene, path); EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Created BS1 rich Browser sample scene using the shared multi-window desktop and demand-driven output. Keyboard boot enabled for Unity address input. See RichBrowserSample.md.");
        }

        [MenuItem("VRWebView/Create Runtime Browser Scene")]
        public static void CreateRuntimeBrowser()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var probe = new GameObject("RT1 runtime browser integration", typeof(RuntimeBrowserProbe)).GetComponent<RuntimeBrowserProbe>();
            probe.headCamera = Camera.main;
            if (Camera.main != null) Camera.main.transform.position = Vector3.zero;
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewPlayground")) AssetDatabase.CreateFolder("Assets", "VRWebViewPlayground");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewPlayground/RuntimeBrowser.unity");
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Created RT1 production backend integration scene. Keep the device-tested GameActivity/Vulkan/ARM64/keyboard settings. See AndroidBackend.md.");
        }
        [MenuItem("VRWebView/Create Activity GPU Probe Scene")]
        public static void CreateActivityGpuProbe()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            ActivityProbeFocusSetup.Enable();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var probe = new GameObject("Activity GPU Probe", typeof(ActivityGpuWebViewProbe)).GetComponent<ActivityGpuWebViewProbe>();
            probe.headCamera = Camera.main;
            if (Camera.main != null) Camera.main.transform.position = Vector3.zero;
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewPlayground")) AssetDatabase.CreateFolder("Assets", "VRWebViewPlayground");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewPlayground/ActivityGpuProbe.unity");
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Created AG1 separate-Activity GPU probe. Keep current GameActivity/Vulkan/ARM64 settings; see ActivityGpuProbe.md for fixed test cases and input focus gates.");
        }
        [MenuItem("VRWebView/Create Android IME Proxy Probe Scene")]
        public static void CreateImeProxyProbe()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var probe = new GameObject("IP1 GPU + IME proxy", typeof(GpuWebViewProbe)).GetComponent<GpuWebViewProbe>();
            probe.testInputConnectionProxy = true; probe.restoreBrowserFocus = false;
            probe.headCamera = Camera.main;
            if (Camera.main != null) Camera.main.transform.position = Vector3.zero;
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewPlayground")) AssetDatabase.CreateFolder("Assets", "VRWebViewPlayground");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewPlayground/ImeProxyProbe.unity");
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Created IP1 same-Activity GPU/borrowed InputConnection probe. Keep existing GameActivity, graphics and keyboard boot settings. No focus retry or trial timer. See ImeProxyProbe.md.");
        }

        [MenuItem("VRWebView/Create GPU XR Probe Scene")]
        public static void CreateGpuXrProbe() { CreateVulkanProbe(); }

        [MenuItem("VRWebView/Create Vulkan XR Probe Scene")]
        public static void CreateVulkanProbe()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var probe = new GameObject("GPU XR Probe", typeof(GpuWebViewProbe)).GetComponent<GpuWebViewProbe>();
            probe.headCamera = Camera.main;
            if (Camera.main != null) Camera.main.transform.position = Vector3.zero;
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewPlayground")) AssetDatabase.CreateFolder("Assets", "VRWebViewPlayground");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewPlayground/GpuXrProbe.unity");
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Created same-Activity GPU XR probe. Select Android Vulkan or OpenGL ES 3, keep ARM64; see GpuXrProbe.md for controls and required device checks.");
        }

        [MenuItem("VRWebView/Create GPU Probe Scene")]
        public static void CreateGpuProbe()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("GPU Probe", typeof(GpuProbeLauncher));
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewPlayground")) AssetDatabase.CreateFolder("Assets", "VRWebViewPlayground");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewPlayground/GpuProbe.unity");
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Created standalone GPU probe scene. Android mode buttons switch H0/H1/V1 without rebuilding; see package documentation.");
        }

        [MenuItem("VRWebView/Create IME Probe Scene")]
        public static void Create()
        {
            Type panel = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("T34400.VRWebView.Samples.ImeProbePanel")).FirstOrDefault(t => t != null);
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Import sample first", "Import the IME Probe sample from VRWebView in Package Manager, then run this command again.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("IME Probe", panel);
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewPlayground")) AssetDatabase.CreateFolder("Assets", "VRWebViewPlayground");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewPlayground/ImeProbe.unity");
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Created probe scene. Configure ImeProbePanel startup mode in the Inspector; see package documentation.");
        }
    }
}
