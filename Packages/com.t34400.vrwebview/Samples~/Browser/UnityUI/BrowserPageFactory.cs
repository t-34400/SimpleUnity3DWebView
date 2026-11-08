using UnityEngine;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Optional Inspector adapter. No Android implementation or fake is
    /// supplied; applications may also configure BrowserPanel with a delegate.</summary>
    public abstract class BrowserPageFactory : MonoBehaviour
    {
        public abstract BrowserPage CreatePage(BrowserDispatcher dispatcher);
    }
}
