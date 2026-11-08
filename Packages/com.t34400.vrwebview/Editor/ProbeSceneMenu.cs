using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace T34400.VRWebView.Editor
{
    public static class ProbeSceneMenu
    {
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
