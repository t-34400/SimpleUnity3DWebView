using UnityEngine;

namespace T34400.VRWebView.Samples
{
    /// <summary>Assign public methods to Unity UI buttons for VR. OnGUI is a desktop/debug convenience.</summary>
    public sealed class ImeProbePanel : MonoBehaviour
    {
        [Tooltip("Launch the selected standalone probe automatically, so VR controller UI is not a prerequisite.")]
        public bool launchOnStart = true;
        public AndroidImeProbe.Mode startupMode = AndroidImeProbe.Mode.HostWindow;
        private string message = "Android IME probes: use synthetic input only.";
        private void Start()
        {
            if (launchOnStart && AndroidImeProbe.IsSupported)
                Run(() => AndroidImeProbe.Launch(startupMode));
        }
        public void LaunchHost() { Run(() => AndroidImeProbe.Launch(AndroidImeProbe.Mode.HostWindow)); }
        public void LaunchVirtual() { Run(() => AndroidImeProbe.Launch(AndroidImeProbe.Mode.VirtualDisplay)); }
        public void OpenUnityHost() { Run(() => AndroidImeProbe.OpenOnUnityActivity(AndroidImeProbe.Mode.HostWindow)); }
        public void OpenUnityVirtual() { Run(() => AndroidImeProbe.OpenOnUnityActivity(AndroidImeProbe.Mode.VirtualDisplay)); }
        public void CloseOverlay() { AndroidImeProbe.CloseUnityOverlay(); }
        private void Run(System.Action action)
        {
            try { action(); message = "Request sent. Observe the Android panel and Logcat; this is not a pass result."; }
            catch (System.Exception error) { message = error.GetType().Name + ": " + error.Message; }
        }
        private void OnApplicationPause(bool paused) { if (paused) CloseOverlay(); }
        private void OnDestroy() { CloseOverlay(); }
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20, 20, 700, 360), GUI.skin.box);
            GUILayout.Label(message);
            if (GUILayout.Button("Standalone Activity: normal WebView")) LaunchHost();
            if (GUILayout.Button("Standalone Activity: VirtualDisplay")) LaunchVirtual();
            if (GUILayout.Button("Unity Activity overlay: normal WebView (experimental)")) OpenUnityHost();
            if (GUILayout.Button("Unity Activity overlay: VirtualDisplay (experimental)")) OpenUnityVirtual();
            if (GUILayout.Button("Close Unity overlay")) CloseOverlay();
            GUILayout.EndArea();
        }
    }
}
