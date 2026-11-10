using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace T34400.VRWebView.Editor
{
    public static class HostComparisonMenu
    {
        internal const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        internal static string ExpectedKey => "VRWebView.HostComparison.ExpectedEntry." + Hash128.Compute(Application.dataPath);

        [MenuItem("VRWebView/Diagnostics/Create Host Comparison Scene")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var probe = new GameObject("Host Comparison Probe", typeof(HostComparisonProbe)).GetComponent<HostComparisonProbe>();
            probe.headCamera = Camera.main;
            if (Camera.main != null) Camera.main.transform.position = Vector3.zero;
            if (!AssetDatabase.IsValidFolder("Assets/VRWebViewPlayground")) AssetDatabase.CreateFolder("Assets", "VRWebViewPlayground");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/VRWebViewPlayground/HostComparison.unity");
            EditorSceneManager.SaveScene(scene, path);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
            Debug.Log("Created HC2 host comparison scene. Select Host Comparison > Keyboard Boot > OFF (0) or ON (1) before building; see KeyboardComparison.md.");
        }
        [MenuItem("VRWebView/Diagnostics/Host Comparison/Use GameActivity")]
        public static void UseGameActivity() { SetEntry(true); }
        [MenuItem("VRWebView/Diagnostics/Host Comparison/Use Activity (Experimental)")]
        public static void UseActivity() { SetEntry(false); }
        [MenuItem("VRWebView/Diagnostics/Host Comparison/Clear Expected Entry Check")]
        public static void ClearExpected() { EditorPrefs.DeleteKey(ExpectedKey); Debug.Log("Cleared the comparison entry-point expectation; Player Settings and manifest are unchanged."); }

        private static void SetEntry(bool game)
        {
            if (!File.Exists(ManifestPath)) throw new InvalidOperationException("Enable Custom Main Manifest before using the comparison menu");
            string before = File.ReadAllText(ManifestPath);
            var previous = PlayerSettings.Android.applicationEntry;
            var wanted = game ? AndroidApplicationEntry.GameActivity : AndroidApplicationEntry.Activity;
            string wantedClass = game ? HostEntryManifest.GameActivity : HostEntryManifest.Activity;
            if (previous == wanted && HostEntryManifest.LauncherName(before) == wantedClass) {
                EditorPrefs.SetInt(ExpectedKey, game ? 2 : 1);
                Debug.Log("HC1 expected launcher=" + wantedClass + "; existing manifest and Player Settings preserved. Verify the actual owner in logcat.");
                return;
            }
            string after = HostEntryManifest.Rewrite(before, game); // Validate before changing either setting.
            try {
                PlayerSettings.Android.applicationEntry = game ? AndroidApplicationEntry.GameActivity : AndroidApplicationEntry.Activity;
                File.WriteAllText(ManifestPath, after, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(ManifestPath); AssetDatabase.SaveAssets();
                EditorPrefs.SetInt(ExpectedKey, game ? 2 : 1);
            }
            catch {
                PlayerSettings.Android.applicationEntry = previous;
                File.WriteAllText(ManifestPath, before, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(ManifestPath); throw;
            }
            Debug.Log("HC1 expected launcher=" + (game ? HostEntryManifest.GameActivity : HostEntryManifest.Activity)
                + ". Meta SDK 207 marks GameActivity required; Activity is a sideload comparison only. SDK checks are not disabled. Confirm the actual owner in logcat.");
        }
    }
    internal sealed class HostComparisonBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => 100000;
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            int expected = EditorPrefs.GetInt(HostComparisonMenu.ExpectedKey, 0);
            if (expected == 0) return;
            bool game = expected == 2;
            var wanted = game ? AndroidApplicationEntry.GameActivity : AndroidApplicationEntry.Activity;
            string wantedClass = game ? HostEntryManifest.GameActivity : HostEntryManifest.Activity;
            if (PlayerSettings.Android.applicationEntry != wanted || !File.Exists(HostComparisonMenu.ManifestPath)
                    || HostEntryManifest.LauncherName(File.ReadAllText(HostComparisonMenu.ManifestPath)) != wantedClass)
                throw new BuildFailedException("HC1 entry point changed after selection or disagrees with the custom manifest. The SDK may have restored GameActivity. This build would not test the selected comparison; SDK validation was not bypassed.");
            Debug.Log("HC1 prebuild expected=" + wantedClass + "; verify runtime owner before comparing results.");
        }
    }
}
