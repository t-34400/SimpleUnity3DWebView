using UnityEngine;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Gravity-aligned sample placement. Fixed pitch is independent from head/hand tilt.</summary>
    public static class BrowserWindowPose {
        public static Quaternion Yaw(Vector3 forward, Quaternion fallback) {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < .000001f) flat = Vector3.ProjectOnPlane(fallback * Vector3.forward, Vector3.up);
            return flat.sqrMagnitude < .000001f ? Quaternion.identity : Quaternion.LookRotation(flat, Vector3.up);
        }
        public static Quaternion WithTilt(Vector3 forward, float degrees, Quaternion fallback) => Yaw(forward, fallback) * Quaternion.Euler(Mathf.Clamp(degrees, 0, 10), 0, 0);
        public static Quaternion Normalize(Quaternion rotation, float degrees) => WithTilt(rotation * Vector3.forward, degrees, rotation);
    }
}
