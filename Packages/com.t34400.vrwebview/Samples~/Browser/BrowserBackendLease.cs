using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Transfers a page backend and optional connection lifetime to the
    /// workspace. Disposal ends logical lifetimes; it does not wait for GPU release.</summary>
    public sealed class BrowserBackendLease : IBrowserBackend
    {
        private readonly IBrowserBackend backend;
        private readonly IDisposable connection;
        public bool IsDisposed { get; private set; }
        public BrowserBackendLease(IBrowserBackend backend, IDisposable connectionLifetime = null)
        { this.backend = backend ?? throw new ArgumentNullException(nameof(backend)); connection = connectionLifetime; }
        public Task InitializeAsync(CancellationToken token)
        { ThrowIfDisposed(); return backend.InitializeAsync(token); }
        public Task NavigateAsync(Uri address, CancellationToken token)
        { ThrowIfDisposed(); return backend.NavigateAsync(address, token); }
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            var failures = new List<Exception>();
            try { backend.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (connection != null && !ReferenceEquals(connection, backend)) {
                try { connection.Dispose(); } catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0) throw new AggregateException("Could not close browser page", failures);
        }
        private void ThrowIfDisposed()
        { if (IsDisposed) throw new ObjectDisposedException(nameof(BrowserBackendLease)); }
    }
}
