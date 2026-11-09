using UnityEngine;

namespace T34400.VRWebView
{
    /// <summary>Launches the native comparison without requiring controller UI or sample reimport.</summary>
    public sealed class GpuProbeLauncher : MonoBehaviour
    {
        public bool launchOnStart = true;
        public AndroidGpuProbe.Mode startupMode = AndroidGpuProbe.Mode.NormalWindow;

        private void Start()
        {
            if (launchOnStart && Application.platform == RuntimePlatform.Android) Launch();
        }

        public void Launch()
        {
            try { AndroidGpuProbe.Launch(startupMode); }
            catch (System.Exception error) { Debug.LogError("GPU probe launch failed: " + error.GetType().Name, this); }
        }
    }
}
