namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Optional real Android composition for BrowserPanel's page factory field.</summary>
    public sealed class AndroidBrowserPageFactory : BrowserPageFactory
    {
        public int width = 1024, height = 768;
        public bool enableKnownProviderImeWorkaround = true;
        public override BrowserPage CreatePage(BrowserDispatcher dispatcher)
        {
            var backend = new AndroidBrowserBackend(dispatcher, width, height, enableKnownProviderImeWorkaround);
            return new BrowserPage(backend, backend.Surface, backend);
        }
    }
}
