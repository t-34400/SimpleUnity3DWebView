using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Transfers a page backend and optional connection lifetime to the
    /// workspace. Disposal ends logical lifetimes; it does not wait for GPU release.</summary>
    public sealed class BrowserBackendLease : IBrowserBackend, IBrowserFailureSource
    {
        private readonly IBrowserBackend backend;
        private readonly IDisposable connection;
        public event Action<Exception> Failed;
        private void ForwardFailure(Exception error) { if (!IsDisposed) Failed?.Invoke(error); }
        public bool IsDisposed { get; private set; }
        public BrowserBackendLease(IBrowserBackend backend, IDisposable connectionLifetime = null)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend)); connection = connectionLifetime;
            if (backend is IBrowserFailureSource source) source.Failed += ForwardFailure;
        }
        public Task InitializeAsync(CancellationToken token)
        { ThrowIfDisposed(); return backend.InitializeAsync(token); }
        public Task NavigateAsync(Uri address, CancellationToken token)
        { ThrowIfDisposed(); return backend.NavigateAsync(address, token); }
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (backend is IBrowserFailureSource source) source.Failed -= ForwardFailure;
            Failed = null;
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
