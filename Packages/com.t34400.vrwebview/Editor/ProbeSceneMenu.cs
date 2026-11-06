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
