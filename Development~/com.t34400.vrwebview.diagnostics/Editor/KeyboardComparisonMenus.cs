using UnityEditor;
namespace T34400.VRWebView.Editor {
    internal static class KeyboardComparisonMenus {
        [MenuItem("VRWebView/Diagnostics/Host Comparison/Keyboard Boot/OFF (0)")] private static void Off() => KeyboardBootProbeSetup.Off();
        [MenuItem("VRWebView/Diagnostics/Host Comparison/Keyboard Boot/ON (1)")] private static void On() => KeyboardBootProbeSetup.On();
        [MenuItem("VRWebView/Diagnostics/Host Comparison/Keyboard Boot/Disable Override")] private static void Disable() => KeyboardBootProbeSetup.Disable();
    }
}
