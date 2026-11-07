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

        [MenuItem("VRWebView/Host Comparison/Keyboard Boot/OFF (0)")]
        public static void Off() { Select(false); }
        [MenuItem("VRWebView/Host Comparison/Keyboard Boot/ON (1)")]
        public static void On() { Select(true); }
        [MenuItem("VRWebView/Host Comparison/Keyboard Boot/Disable Diagnostic Override")]
        public static void Disable()
        {
            var feature = Feature;
            if (feature != null) { feature.enabled = false; EditorUtility.SetDirty(feature); AssetDatabase.SaveAssets(); }
            Debug.Log("HC2 diagnostic boot override disabled. Rebuild to use the project's normal boot configuration.");
        }
        private static void Select(bool enabled)
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var feature = Feature;
            if (feature == null) throw new InvalidOperationException("Android OpenXR settings or the keyboard boot probe could not be found. Enable Android OpenXR, finish script import, then retry.");
            feature.keyboardOverlayEnabled = enabled; feature.enabled = true;
            EditorUtility.SetDirty(feature); AssetDatabase.SaveAssets();
            Debug.Log("HC2 next Android build: " + KeyboardBootProbeFeature.BootKey + "=" + (enabled ? "1" : "0")
                + ". GameActivity, Meta XR Feature and graphics settings are unchanged. Verify bootCheck in the installed APK.");
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
                throw new BuildFailedException("HC2 could not set the keyboard boot comparison value");
            Debug.Log("HC2 boot hook " + KeyboardBootProbeFeature.BootKey + "=" + (feature.keyboardOverlayEnabled ? "1" : "0"));
        }
        protected override void OnPreprocessBuildExt(BuildReport report) { }
        protected override void OnPostprocessBuildExt(BuildReport report) { }
        protected override void OnPostGenerateGradleAndroidProjectExt(string path)
        {
            string boot = Path.Combine(path, "src/main/assets/bin/Data/boot.config");
            if (!File.Exists(boot)) {
                Debug.LogWarning("HC2 generated boot.config not available at the Gradle callback; check the installed APK bootCheck.");
                return;
            }
            var entries = File.ReadAllLines(boot).Where(line => line.StartsWith(KeyboardBootProbeFeature.BootKey + "=", StringComparison.Ordinal)).ToArray();
            var feature = KeyboardBootProbeSetup.Feature;
            string expected = KeyboardBootProbeFeature.BootKey + "=" + (feature.keyboardOverlayEnabled ? "1" : "0");
            if (entries.Length != 1 || entries[0] != expected)
                throw new BuildFailedException("HC2 generated boot.config does not contain exactly the selected keyboard value");
            Debug.Log("HC2 verified generated " + expected);
        }
    }

    internal sealed class KeyboardBootProbeSceneCheck : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.Android
                || !scene.GetRootGameObjects().Any(root => root.GetComponentsInChildren<HostComparisonProbe>(true).Length > 0)) return;
            var feature = KeyboardBootProbeSetup.Feature;
            if (feature == null || !feature.enabled)
                throw new BuildFailedException("HC2 scene requires VRWebView > Host Comparison > Keyboard Boot > OFF (0) or ON (1) before building.");
            if (PlayerSettings.Android.applicationEntry != AndroidApplicationEntry.GameActivity)
                throw new BuildFailedException("HC2 comparisons use the existing GameActivity. Select Host Comparison > Use GameActivity before building.");
        }
    }
}
