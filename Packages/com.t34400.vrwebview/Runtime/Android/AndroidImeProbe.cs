using System;
using UnityEngine;

namespace T34400.VRWebView
{
    /// <summary>Diagnostic entry points only. Not a production browser backend.</summary>
    public static class AndroidImeProbe
    {
        public enum Mode { HostWindow, VirtualDisplay }
        public static bool IsSupported => Application.platform == RuntimePlatform.Android;

        /// <summary>Starts a separate Android Activity to establish an IME baseline.</summary>
        public static void Launch(Mode mode) { Call("launch", mode); }

        /// <summary>Experiments with the actual Unity Activity. Native views may not be visible
        /// in XR. This is a separate test from the standalone baseline.</summary>
        public static void OpenOnUnityActivity(Mode mode) { Call("open", mode); }

        public static void CloseUnityOverlay()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var probe = new AndroidJavaClass("com.t34400.vrwebview.ImeProbe"))
                probe.CallStatic("close", activity);
#endif
        }

        private static void Call(string method, Mode mode)
        {
            if (!IsSupported) throw new PlatformNotSupportedException("IME probes require an Android player.");
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var probe = new AndroidJavaClass("com.t34400.vrwebview.ImeProbe"))
                probe.CallStatic(method, activity, mode == Mode.VirtualDisplay);
#endif
        }
    }
}
