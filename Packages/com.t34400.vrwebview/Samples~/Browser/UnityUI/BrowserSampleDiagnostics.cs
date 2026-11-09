using UnityEngine;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Opt-in BS1 metadata only. Never records URLs, drafts or page text.</summary>
    public sealed class BrowserSampleDiagnostics : MonoBehaviour
    {
        public BrowserPanel panel;
        private float next;
        private bool ready;
        private void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1;
            if (panel == null) { Debug.Log("[VRWebViewSample] BS1 event=missing_panel", this); return; }
            if (!ready && panel.IsConfigured) { ready = true; Debug.Log("[VRWebViewSample] BS1 event=ui_ready", this); }
            var tab = panel.ActiveTab;
            Debug.Log("[VRWebViewSample] BS1 event=snapshot configured=" + panel.IsConfigured + " tabs=" + panel.TabCount
                + " active=" + (tab?.Id ?? -1) + " state=" + (tab == null ? "none" : tab.State.ToString())
                + " busy=" + (tab?.IsBusy ?? false) + " addressFocus=" + panel.IsAddressFocused
                + " frames=" + Time.frameCount + " appFocus=" + Application.isFocused, this);
        }
    }
}
