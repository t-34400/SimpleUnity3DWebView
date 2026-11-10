using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace T34400.VRWebView
{
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "VRWebView Keyboard Overlay", Desc = "Enable the Quest system keyboard overlay for Unity text fields",
        Company = "t34400", Version = "0.1.0", FeatureId = Id, OpenxrExtensionStrings = "", BuildTargetGroups = new[] { BuildTargetGroup.Android })]
#endif
    public sealed class KeyboardBootProbeFeature : OpenXRFeature
    {
        public const string Id = "com.t34400.vrwebview.keyboard-boot-probe";
        public const string BootKey = "xr-keyboard-overlay-enabled";
        [Tooltip("Enable the system keyboard overlay in this Android build.")]
        public bool keyboardOverlayEnabled;
        protected override bool OnInstanceCreate(ulong instance)
        {
            Debug.Log("[VRWebViewHost] HC2 keyboardBootFeature requested=" + (keyboardOverlayEnabled ? "1" : "0")
                + "; actual packaged value is reported by setup/bootCheck");
            return true;
        }
    }
}
