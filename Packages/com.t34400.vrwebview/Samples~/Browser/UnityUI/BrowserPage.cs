using System;
using System.Threading;
using System.Threading.Tasks;
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
        public IBrowserContentBackend Content { get; }
        private readonly Action<bool, int, int> presentation;
        public void SetPresentation(bool visible, int width, int height) { presentation?.Invoke(visible, width, height); }
        public Task EndEditingAsync() => Editing is IBrowserAsyncEditingSink asyncSink ? asyncSink.EndEditingAsync(CancellationToken.None) : EndSync();
        private Task EndSync() { Editing?.EndEditing(); return Task.CompletedTask; }
        public BrowserPage(IBrowserBackend backend, IBrowserSurfaceSource<Texture> surface,
            IBrowserPointerSink input = null, IDisposable connectionLifetime = null, IBrowserEditingSink editing = null, Action<bool, int, int> presentation = null)
        {
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            Content = backend as IBrowserContentBackend;
            Surface = surface ?? throw new ArgumentNullException(nameof(surface));
            this.presentation = presentation; Input = input; Editing = editing ?? backend as IBrowserEditingSink ?? input as IBrowserEditingSink;
            Backend = new BrowserBackendLease(backend, connectionLifetime);
        }
        public void Dispose() { Backend.Dispose(); }
    }
}
