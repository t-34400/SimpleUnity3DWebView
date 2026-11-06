using System;
using System.Threading;
using System.Threading.Tasks;

namespace T34400.VRWebView
{
    public enum BrowserState { Created, Initializing, Ready, Faulted, Disposed }

    /// <summary>Implementations own their native resources and make Dispose idempotent.
    /// Initialize and operations must honor cancellation. Cancellation does not undo navigation.</summary>
    public interface IBrowserBackend : IDisposable
    {
        Task InitializeAsync(CancellationToken cancellationToken);
        Task NavigateAsync(Uri url, CancellationToken cancellationToken);
    }

    /// <summary>Owns one backend. Invoke APIs on the owner thread. Notifications are dispatched
    /// through the supplied dispatcher (Unity integrations must supply their main-thread dispatcher).</summary>
    public sealed class BrowserSession : IDisposable
    {
        private readonly IBrowserBackend backend;
        private readonly Action<Action> dispatch;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Task initialization;
        private int generation;
        public BrowserState State { get; private set; } = BrowserState.Created;
        public event Action<BrowserState> StateChanged;

        public BrowserSession(IBrowserBackend backend, Action<Action> dispatcher)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            dispatch = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public Task InitializeAsync()
        {
            ThrowIfDisposed();
            if (initialization != null) return initialization;
            if (State != BrowserState.Created) throw new InvalidOperationException("Session cannot initialize.");
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            initialization = completion.Task;
            ChangeState(BrowserState.Initializing);
            _ = InitializeCoreAsync(completion, generation);
            return initialization;
        }

        private async Task InitializeCoreAsync(TaskCompletionSource<bool> completion, int version)
        {
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                await backend.InitializeAsync(lifetime.Token);
                lifetime.Token.ThrowIfCancellationRequested();
                if (version != generation) throw new OperationCanceledException();
                ChangeState(BrowserState.Ready);
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(); }
            catch (Exception error)
            {
                if (State != BrowserState.Disposed) ChangeState(BrowserState.Faulted);
                completion.TrySetException(error);
            }
        }

        public async Task NavigateAsync(string address, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (State != BrowserState.Ready) throw new InvalidOperationException("Await InitializeAsync before navigating.");
            Uri url = NavigationPolicy.Parse(address);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken))
            {
                linked.Token.ThrowIfCancellationRequested();
                await backend.NavigateAsync(url, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
            }
        }

        private void ChangeState(BrowserState state)
        {
            State = state;
            int version = generation;
            dispatch(() => { if (version == generation) StateChanged?.Invoke(state); });
        }

        private void ThrowIfDisposed()
        {
            if (State == BrowserState.Disposed) throw new ObjectDisposedException(nameof(BrowserSession));
        }

        public void Dispose()
        {
            if (State == BrowserState.Disposed) return;
            generation++;
            State = BrowserState.Disposed;
            try { lifetime.Cancel(); }
            finally
            {
                try { backend.Dispose(); }
                finally { ChangeState(BrowserState.Disposed); }
            }
            // The CTS remains readable by in-flight asynchronous operations after disposal.
        }
    }
}
