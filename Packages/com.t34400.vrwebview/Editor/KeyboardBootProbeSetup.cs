using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace T34400.VRWebView.Editor
{
    public static class KeyboardBootProbeSetup
    {
        internal static KeyboardBootProbeFeature Feature => FeatureHelpers.GetFeatureWithIdForBuildTarget(
            BuildTargetGroup.Android, KeyboardBootProbeFeature.Id) as KeyboardBootProbeFeature;

        
        public static void Off() { Select(false); }
        [MenuItem("VRWebView/Project Settings/Enable Quest Keyboard Overlay")]
        public static void On() { Select(true); }
        [MenuItem("VRWebView/Project Settings/Disable Keyboard Overlay Override")]
        public static void Disable()
        {
            var feature = Feature;
            if (feature != null) { feature.enabled = false; EditorUtility.SetDirty(feature); AssetDatabase.SaveAssets(); }
            Debug.Log("VRWebView keyboard overlay override disabled. Rebuild to use the project's normal boot configuration.");
        }
        private static void Select(bool enabled)
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var feature = Feature;
            if (feature == null) throw new InvalidOperationException("Android OpenXR settings or the VRWebView keyboard overlay feature could not be found. Enable Android OpenXR, finish script import, then retry.");
            feature.keyboardOverlayEnabled = enabled; feature.enabled = true;
            EditorUtility.SetDirty(feature); AssetDatabase.SaveAssets();
            Debug.Log("VRWebView next Android build: " + KeyboardBootProbeFeature.BootKey + "=" + (enabled ? "1" : "0")
                + ". Rebuild the Android player to apply this setting.");
        }
    }

    // Public hook also used by Unity OpenXR 1.16.1 MetaQuestFeatureBuildHooks.
    internal sealed class KeyboardBootProbeBuildHooks : OpenXRFeatureBuildHooks
    {
        public override int callbackOrder => 100001;
        public override Type featureType => typeof(KeyboardBootProbeFeature);
        protected override void OnProcessBootConfigExt(BuildReport report, BootConfigBuilder builder)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            var feature = KeyboardBootProbeSetup.Feature;
            if (feature == null || !builder.SetBootConfigBoolean(KeyboardBootProbeFeature.BootKey, feature.keyboardOverlayEnabled))
                throw new BuildFailedException("VRWebView could not set the keyboard overlay boot value");
            Debug.Log("VRWebView keyboard overlay boot hook " + KeyboardBootProbeFeature.BootKey + "=" + (feature.keyboardOverlayEnabled ? "1" : "0"));
        }
        protected override void OnPreprocessBuildExt(BuildReport report) { }
        protected override void OnPostprocessBuildExt(BuildReport report) { }
        protected override void OnPostGenerateGradleAndroidProjectExt(string path)
        {
            string boot = Path.Combine(path, "src/main/assets/bin/Data/boot.config");
            if (!File.Exists(boot)) {
                Debug.LogWarning("VRWebView generated boot.config is not yet available at the Gradle callback.");
                return;
            }
            var entries = File.ReadAllLines(boot).Where(line => line.StartsWith(KeyboardBootProbeFeature.BootKey + "=", StringComparison.Ordinal)).ToArray();
            var feature = KeyboardBootProbeSetup.Feature;
            string expected = KeyboardBootProbeFeature.BootKey + "=" + (feature.keyboardOverlayEnabled ? "1" : "0");
            if (entries.Length != 1 || entries[0] != expected)
                throw new BuildFailedException("VRWebView generated boot.config does not contain exactly the selected keyboard overlay value");
            Debug.Log("VRWebView verified generated " + expected);
        }
    }

    internal sealed class KeyboardBootProbeSceneCheck : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.Android) return;
            bool browser = scene.GetRootGameObjects().Any(root => root.GetComponentsInChildren<MonoBehaviour>(true)
                .Any(component => component != null && component.GetType().FullName == "T34400.VRWebView.Samples.Browser.BrowserDesktopPanel"));
            bool inputControl = scene.GetRootGameObjects().Any(root => root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c != null && c.GetType().FullName == "T34400.VRWebView.UnityInputFieldProbe"));
            bool comparison = scene.GetRootGameObjects().Any(root => root.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c != null && c.GetType().FullName == "T34400.VRWebView.HostComparisonProbe"));
            if (!browser && !comparison && !inputControl) return;
            var feature = KeyboardBootProbeSetup.Feature;
            if ((browser || inputControl) && (feature == null || !feature.enabled || !feature.keyboardOverlayEnabled))
                throw new BuildFailedException("Browser text input requires the keyboard overlay. Select VRWebView > Project Settings > Enable Quest Keyboard Overlay before building.");
            if (feature == null || !feature.enabled)
                throw new BuildFailedException("The diagnostic scene requires VRWebView > Diagnostics > Host Comparison > Keyboard Boot > OFF (0) or ON (1) before building.");
            if (PlayerSettings.Android.applicationEntry != AndroidApplicationEntry.GameActivity)
                throw new BuildFailedException("VRWebView text input uses GameActivity. Select GameActivity in Android Player Settings before building.");
        }
    }
}
