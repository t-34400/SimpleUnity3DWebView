using System;
using System.Threading;
using System.Threading.Tasks;

namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Sample view model; workspace owns its session. Busy means an API
    /// command is pending, not that a document or video has finished loading.</summary>
    public sealed class BrowserTab
    {
        private readonly BrowserSession session;
        private readonly Action<Action> dispatch;
        private readonly Action<BrowserTab> changed;
        private Task initialization;
        private TaskCompletionSource<bool> pending;
        private CancellationTokenSource operationCancellation;
        private long operationVersion;
        public long Id { get; }
        public BrowserState State { get; private set; } = BrowserState.Created;
        public bool IsClosed { get; private set; }
        public bool IsBusy { get; private set; }
        public Uri RequestedAddress { get; private set; }
        public Exception LastError { get; private set; }

        internal BrowserTab(long id, BrowserSession value, Action<Action> dispatcher, Action<BrowserTab> notify)
        {
            Id = id; session = value; dispatch = dispatcher; changed = notify;
            session.StateChanged += SessionChanged;
        }

        private void SessionChanged(BrowserState state)
        {
            if (IsClosed) return;
            State = state;
            if (state == BrowserState.Faulted) LastError = session.LastBackendError ?? LastError;
            changed(this);
        }

        public Task InitializeAsync()
        {
            ThrowIfClosed();
            if (initialization != null) return initialization;
            // Publish the shared task before notifying observers (which may reenter).
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            initialization = completion.Task;
            StartOperation(completion, token => session.InitializeAsync());
            return initialization;
        }

        public Task NavigateAsync(string address)
        {
            ThrowIfClosed();
            Uri parsed = NavigationPolicy.Parse(address);
            if (State != BrowserState.Ready) throw new InvalidOperationException("Initialize the tab before navigating");
            if (IsBusy) throw new InvalidOperationException("A tab command is already pending");
            RequestedAddress = parsed; // Not a committed URL, title or history entry.
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            StartOperation(completion, token => session.NavigateAsync(parsed.AbsoluteUri, token));
            return completion.Task;
        }

        private void StartOperation(TaskCompletionSource<bool> completion, Func<CancellationToken, Task> action)
        {
            long version = ++operationVersion;
            pending = completion;
            operationCancellation = new CancellationTokenSource();
            IsBusy = true; LastError = null;
            changed(this);
            if (IsClosed || version != operationVersion) { completion.TrySetCanceled(); return; }
            Task work;
            try { work = action(operationCancellation.Token); }
            catch (Exception error) { Finish(completion, version, error); return; }
            _ = AwaitOperation(work, completion, version);
        }

        private async Task AwaitOperation(Task work, TaskCompletionSource<bool> completion, long version)
        {
            Exception failure = null;
            try { await work.ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            try { dispatch(() => Finish(completion, version, failure)); }
            catch (Exception error) { completion.TrySetException(error); }
        }

        private void Finish(TaskCompletionSource<bool> completion, long version, Exception failure)
        {
            if (IsClosed || version != operationVersion) { completion.TrySetCanceled(); return; }
            IsBusy = false; LastError = failure ?? session.LastBackendError; pending = null;
            operationCancellation.Dispose(); operationCancellation = null;
            // Ready/Failed snapshot is visible before the command task completes.
            State = session.State;
            if (failure == null) completion.TrySetResult(true);
            else if (failure is OperationCanceledException) completion.TrySetCanceled();
            else completion.TrySetException(failure);
            changed(this);
        }

        internal void Close()
        {
            if (IsClosed) return;
            IsClosed = true; State = BrowserState.Disposed; IsBusy = false; ++operationVersion;
            session.StateChanged -= SessionChanged;
            pending?.TrySetCanceled(); pending = null;
            try { operationCancellation?.Cancel(); }
            finally {
                operationCancellation?.Dispose(); operationCancellation = null;
                session.Dispose();
            }
        }

        private void ThrowIfClosed()
        {
            if (IsClosed) throw new ObjectDisposedException(nameof(BrowserTab));
        }
    }
}
