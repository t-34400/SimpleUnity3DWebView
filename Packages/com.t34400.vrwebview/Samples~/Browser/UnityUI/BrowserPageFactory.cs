using UnityEngine;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Optional Inspector adapter. AndroidBrowserPageFactory is supplied
    /// separately; applications may also configure BrowserPanel with a delegate.</summary>
    public abstract class BrowserPageFactory : MonoBehaviour
    {
        public abstract BrowserPage CreatePage(BrowserDispatcher dispatcher);
    }
}
