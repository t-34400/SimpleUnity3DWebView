using UnityEngine;
using UnityEngine.XR.OpenXR.Features;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.XR.OpenXR.Features;
#endif

namespace T34400.VRWebView
{
#if UNITY_EDITOR
    [OpenXRFeature(UiName = "VRWebView Focus Observer", Desc = "Observe OpenXR session focus; retained for existing project compatibility",
        Company = "t34400", Version = "0.1.0", FeatureId = Id, OpenxrExtensionStrings = "", BuildTargetGroups = new[] { BuildTargetGroup.Android })]
#endif
    public sealed class ActivityProbeFocusFeature : OpenXRFeature
    {
        public const string Id = "com.t34400.vrwebview.activity-focus-probe";
        private static readonly ProbeXrFocusState focus = new ProbeXrFocusState();
        private static volatile bool seenInstance;
        private ulong session;
        public static bool SeenInstance => seenInstance;
        public static bool? SessionFocus => focus.Focus;
        public static int SessionState => focus.State;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState() { seenInstance = false; focus.Reset(); }
        protected override bool OnInstanceCreate(ulong instance) { seenInstance = true; focus.Reset(); return true; }
        protected override void OnSessionCreate(ulong handle) { session = handle; focus.Reset(); }
        protected override void OnSessionStateChange(int before, int after)
        {
            focus.Observe(after);
            Debug.Log("[VRWebViewActivityGPU] focusFeature sessionState=" + before + ">" + after);
        }
        protected override void OnSessionDestroy(ulong handle) { if (session == handle) { session = 0; focus.Reset(); } }
        protected override void OnInstanceDestroy(ulong instance) { session = 0; focus.Reset(); }
    }
}
