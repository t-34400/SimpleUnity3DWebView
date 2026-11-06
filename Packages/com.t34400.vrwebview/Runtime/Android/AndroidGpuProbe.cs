using System;
using UnityEngine;

namespace T34400.VRWebView
{
    /// <summary>Standalone Android video/GPU/IME comparison, not a Unity texture backend.</summary>
    public static class AndroidGpuProbe
    {
        public enum Mode { NormalWindow = 0, HostHardwareCanvas = 1, VirtualDisplayGpu = 2 }

        public static void Launch(Mode mode = Mode.NormalWindow)
        {
            if (Application.platform != RuntimePlatform.Android)
                throw new PlatformNotSupportedException("GPU probes require an Android player.");
            if (!Enum.IsDefined(typeof(Mode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var probe = new AndroidJavaClass("com.t34400.vrwebview.GpuProbe"))
                probe.CallStatic("launch", activity, (int)mode, SystemInfo.graphicsDeviceType.ToString());
#endif
        }
    }
}
