using System;
using UnityEngine;

namespace T34400.VRWebView
{
    public sealed class VulkanProbeButton : MonoBehaviour
    {
        internal Action Action;
        internal void Press() { Action?.Invoke(); }
    }
}
