using System;
using T34400.VRWebView.Presentation;
using UnityEngine;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Sample composition roles for one page. The runtime backend does
    /// not depend on this type; an application adapter constructs it.</summary>
    public sealed class BrowserPage : IDisposable
    {
        public BrowserBackendLease Backend { get; }
        public IBrowserSurfaceSource<Texture> Surface { get; }
        public IBrowserPointerSink Input { get; }
        public IBrowserEditingSink Editing { get; }
        public BrowserPage(IBrowserBackend backend, IBrowserSurfaceSource<Texture> surface,
            IBrowserPointerSink input = null, IDisposable connectionLifetime = null, IBrowserEditingSink editing = null)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            Surface = surface ?? throw new ArgumentNullException(nameof(surface));
            Input = input; Editing = editing ?? backend as IBrowserEditingSink ?? input as IBrowserEditingSink;
            Backend = new BrowserBackendLease(backend, connectionLifetime);
        }
        public void Dispose() { Backend.Dispose(); }
    }
}
