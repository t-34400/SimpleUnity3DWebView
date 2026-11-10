using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace T34400.VRWebView.Editor
{
    public static class ActivityProbeFocusSetup
    {
        [MenuItem("VRWebView/Diagnostics/Activity GPU/Enable OpenXR Focus Probe")]
        public static void Enable()
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, ActivityProbeFocusFeature.Id);
            if (feature == null) throw new InvalidOperationException("Android OpenXR settings or the VRWebView focus feature could not be found. Enable Android OpenXR, let scripts import, then retry.");
            feature.enabled = true; EditorUtility.SetDirty(feature); AssetDatabase.SaveAssets();
            Debug.Log("Enabled VRWebView Activity Focus Probe for Android. This observes session state only; Meta features, loader and input focus settings are unchanged.");
        }
    }
    internal sealed class ActivityProbeFocusBuildCheck : IProcessSceneWithReport
    {
        public int callbackOrder => 0;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || report.summary.platform != BuildTarget.Android
                || !scene.GetRootGameObjects().Any(root => root.GetComponentsInChildren<ActivityGpuWebViewProbe>(true).Length > 0)) return;
            var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, ActivityProbeFocusFeature.Id);
            if (feature == null || !feature.enabled)
                throw new BuildFailedException("AG1 scene requires VRWebView > Activity GPU > Enable OpenXR Focus Probe before building. Missing focus observations would leave the Unity ray blocked.");
        }
    }
}
