using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace T34400.VRWebView.Samples.Browser
{
    [DisallowMultipleComponent]
    public sealed class BrowserAddressFocus : MonoBehaviour, ISelectHandler
    {
        public event Action Selected;
        public void OnSelect(BaseEventData data) { Selected?.Invoke(); }
    }
}
