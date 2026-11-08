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
    /// through a serial FIFO owner dispatcher (Unity integrations supply their main-thread dispatcher).</summary>
    public sealed class BrowserSession : IDisposable
    {
        private readonly IBrowserBackend backend;
        private readonly Action<Action> dispatch;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Task initialization;
        private TaskCompletionSource<bool> initializationCompletion;
        private int generation;
        public BrowserState State { get; private set; } = BrowserState.Created;
        public event Action<BrowserState> StateChanged;
        public Exception LastNotificationError { get; private set; }

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
            initializationCompletion = completion;
            initialization = completion.Task;
            ChangeState(BrowserState.Initializing);
            _ = InitializeCoreAsync(completion, generation);
            return initialization;
        }

        private async Task InitializeCoreAsync(TaskCompletionSource<bool> completion, int version)
        {
            Exception failure = null;
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                await backend.InitializeAsync(lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; }
            try
            {
                dispatch(() => {
                    if (version != generation || State == BrowserState.Disposed || lifetime.IsCancellationRequested)
                    { completion.TrySetCanceled(); return; }
                    // Both state and notifications belong to the supplied owner context.
                    State = failure == null ? BrowserState.Ready : BrowserState.Faulted;
                    if (failure == null) completion.TrySetResult(true);
                    else if (failure is OperationCanceledException) completion.TrySetCanceled();
                    else completion.TrySetException(failure);
                    NotifyState(State);
                });
            }
            catch (Exception error) { completion.TrySetException(error); }
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
            NotifyState(state);
        }

        private void NotifyState(BrowserState state)
        {
            int version = generation;
            try { dispatch(() => {
                if (version != generation) return;
                var handlers = StateChanged;
                if (handlers == null) return;
                foreach (Action<BrowserState> handler in handlers.GetInvocationList())
                {
                    if (version != generation) break;
                    try { handler(state); }
                    catch (Exception error) { LastNotificationError = error; }
                }
            }); }
            catch (Exception error) { LastNotificationError = error; }
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
            initializationCompletion?.TrySetCanceled();
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
