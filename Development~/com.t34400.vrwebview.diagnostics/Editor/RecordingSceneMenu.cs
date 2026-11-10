using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace T34400.VRWebView.Editor
{
    public static class RecordingSceneMenu
    {
        [MenuItem("VRWebView/Development/Create Recording Scene")]
        public static void Create()
        {
            var before = SceneManager.GetActiveScene();
            BrowserSetupMenu.CreateWebViewScene();
            var scene = SceneManager.GetActiveScene();
            if (scene.handle == before.handle) return;
            var browser = Object.FindFirstObjectByType<BrowserController>();
            if (browser == null) return;
            var fields = new SerializedObject(browser);
            fields.FindProperty("initialUrl").stringValue = "";
            fields.ApplyModifiedPropertiesWithoutUndo();
            const string root = "Packages/com.t34400.vrwebview.diagnostics/Runtime/Recording/";
            var page = browser.gameObject.AddComponent<RecordingPage>();
            page.browser = browser;
            page.pageTemplate = AssetDatabase.LoadAssetAtPath<TextAsset>(root + "page.html");
            page.video = AssetDatabase.LoadAssetAtPath<TextAsset>(root + "video.bytes");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Recording scene ready. Add it first in the active Android Build Profile. See Documentation/Recording.md in the repository.");
        }
    }
}
