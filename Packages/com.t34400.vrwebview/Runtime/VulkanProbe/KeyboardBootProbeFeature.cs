using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace T34400.VRWebView
{
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "VRWebView Keyboard Boot Probe", Desc = "Controlled keyboard-overlay boot flag comparison; diagnostic only",
        Company = "t34400", Version = "0.1.0", FeatureId = Id, OpenxrExtensionStrings = "", BuildTargetGroups = new[] { BuildTargetGroup.Android })]
#endif
    public sealed class KeyboardBootProbeFeature : OpenXRFeature
    {
        public const string Id = "com.t34400.vrwebview.keyboard-boot-probe";
        public const string BootKey = "xr-keyboard-overlay-enabled";
        [Tooltip("When this diagnostic feature is enabled, explicitly build the keyboard boot flag as 0 or 1.")]
        public bool keyboardOverlayEnabled;
        protected override bool OnInstanceCreate(ulong instance)
        {
            Debug.Log("[VRWebViewHost] HC2 keyboardBootFeature requested=" + (keyboardOverlayEnabled ? "1" : "0")
                + "; actual packaged value is reported by setup/bootCheck");
            return true;
        }
    }
}
