using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView.Editor
{
    /// <summary>Consumer setup paths, independent from the development probe package.</summary>
    public static class BrowserSetupMenu {
        [MenuItem("VRWebView/Create WebView Scene")]
        public static void CreateWebViewScene() {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main; if (camera != null) camera.transform.position = Vector3.zero;
            var canvas = CanvasRoot("WebView", camera, 1024, 768);
            var display = new GameObject("WebView display", typeof(RectTransform), typeof(RawImage), typeof(BrowserView));
            display.transform.SetParent(canvas.transform, false); var rect = (RectTransform)display.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            display.AddComponent<BrowserController>().Configure(display.GetComponent<BrowserView>());
            AddInput(canvas, camera, false); Save(scene, "WebView");
        }
        [MenuItem("VRWebView/Create Rich Browser Sample Scene")]
        public static void CreateRichBrowserSample() { CreateRichBrowserSample(false); }
        public static void CreateRichBrowserSample(bool diagnostics) {
            Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("T34400.VRWebView.Samples.Browser." + name)).FirstOrDefault(t => t != null);
            Type panelType = Find("BrowserDesktopPanel"), factoryType = Find("AndroidBrowserPageFactory");
            if (panelType == null || factoryType == null) {
                EditorUtility.DisplayDialog("Import Browser UI and Model", "Import Browser UI and Model from VRWebView in Package Manager, wait for compilation, then run this command again.", "OK"); return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            KeyboardBootProbeSetup.On();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main; if (camera != null) camera.transform.position = Vector3.zero;
            var canvas = CanvasRoot("VRWebView Browser", camera, 1280, 1000);
            var factory = canvas.gameObject.AddComponent(factoryType); var factoryFields = new SerializedObject(factory);
            factoryFields.FindProperty("width").intValue = 1280; factoryFields.FindProperty("height").intValue = 800;
            factoryFields.FindProperty("enableDiagnostics").boolValue = diagnostics; factoryFields.ApplyModifiedPropertiesWithoutUndo();
            var panel = canvas.gameObject.AddComponent(panelType); var fields = new SerializedObject(panel);
            fields.FindProperty("factory").objectReferenceValue = factory; fields.FindProperty("worldCamera").objectReferenceValue = camera;
            fields.FindProperty("diagnostics").boolValue = diagnostics; fields.ApplyModifiedPropertiesWithoutUndo();
            AddInput(canvas, camera, diagnostics); Save(scene, "RichBrowser");
        }
        private static Canvas CanvasRoot(string name, Camera camera, int width, int height) {
            var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(width, height);
            root.transform.position = new Vector3(0, 1.45f, 1.6f); root.transform.localScale = Vector3.one * .0012f; return canvas;
        }
        private static void AddInput(Canvas canvas, Camera camera, bool diagnostics) {
            var input = new GameObject("WebView EventSystem", typeof(EventSystem), typeof(ProbeCanvasInputModule)).GetComponent<ProbeCanvasInputModule>();
            input.targetCanvas = canvas; input.headCamera = camera; input.enableDiagnostics = diagnostics;
            input.GetComponent<EventSystem>().pixelDragThreshold = 16;
        }
        private static void Save(UnityEngine.SceneManagement.Scene scene, string name) {
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewExamples")) AssetDatabase.CreateFolder("Assets", "VRWebViewExamples");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewExamples/" + name + ".unity");
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("Created " + path + ". Add this scene to the active Android Build Profile's Scene List before Build And Run. See the VRWebView README.");
        }
    }
}
