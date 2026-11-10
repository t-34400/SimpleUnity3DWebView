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
        public BrowserPageState PageState => (backend as IBrowserNavigationBackend)?.PageState ?? BrowserPageState.Empty;
        public event Action<BrowserPageState> PageStateChanged;
        public event Action<BrowserFindResult> FindResultChanged;
        public event Action<BrowserContextTarget> ContextRequested;
        private void FindChanged(BrowserFindResult value) { ContentChanged(() => FindResultChanged?.Invoke(value)); }
        private void ContextChanged(BrowserContextTarget value) { ContentChanged(() => ContextRequested?.Invoke(value)); }
        private void ContentChanged(Action notify) {
            int version = generation;
            try { dispatch(() => { if (version != generation || State == BrowserState.Disposed) return; try { notify(); } catch (Exception error) { LastNotificationError = error; } }); }
            catch (Exception error) { LastNotificationError = error; }
        }
        private void PageChanged(BrowserPageState value) {
            int version = generation;
            try { dispatch(() => {
                if (version != generation || State == BrowserState.Disposed) return;
                var handlers = PageStateChanged; if (handlers == null) return;
                foreach (Action<BrowserPageState> handler in handlers.GetInvocationList()) {
                    if (version != generation) break;
                    try { handler(value); } catch (Exception error) { LastNotificationError = error; }
                }
            }); } catch (Exception error) { LastNotificationError = error; }
        }
        public BrowserState State { get; private set; } = BrowserState.Created;
        public event Action<BrowserState> StateChanged;
        public Exception LastNotificationError { get; private set; }
        public Exception LastBackendError { get; private set; }

        public BrowserSession(IBrowserBackend backend, Action<Action> dispatcher)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            dispatch = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            if (backend is IBrowserFailureSource failures) failures.Failed += BackendFailed;
            if (backend is IBrowserNavigationBackend page) page.PageStateChanged += PageChanged;
            if (backend is IBrowserContentBackend content) { content.FindResultChanged += FindChanged; content.ContextRequested += ContextChanged; }
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

        public async Task LoadHtmlAsync(string html, string baseAddress = "https://vrwebview.invalid/", CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (State != BrowserState.Ready) throw new InvalidOperationException("Await InitializeAsync before loading HTML.");
            if (!(backend is IBrowserHtmlBackend content)) throw new NotSupportedException("Backend cannot load application HTML.");
            if (html == null) throw new ArgumentNullException(nameof(html));
            Uri baseUrl = NavigationPolicy.Parse(baseAddress);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken)) {
                linked.Token.ThrowIfCancellationRequested();
                await content.LoadHtmlAsync(html, baseUrl, linked.Token);
                linked.Token.ThrowIfCancellationRequested();
            }
        }
        public async Task ExecuteAsync(BrowserPageCommand command, CancellationToken token = default) {
            ThrowIfDisposed(); if (State != BrowserState.Ready) throw new InvalidOperationException("Initialize the browser first");
            if (!(backend is IBrowserNavigationBackend page)) throw new NotSupportedException("Backend has no page navigation capability");
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token)) {
                linked.Token.ThrowIfCancellationRequested(); await page.ExecuteAsync(command, linked.Token); linked.Token.ThrowIfCancellationRequested();
            }
        }
        private IBrowserContentBackend Content() {
            ThrowIfDisposed(); if (State != BrowserState.Ready) throw new InvalidOperationException("Initialize the browser first");
            return backend as IBrowserContentBackend ?? throw new NotSupportedException("Backend has no content capability");
        }
        public async Task<string> EvaluateJavaScriptAsync(string script, CancellationToken token = default) {
            var content = Content(); BrowserContentPolicy.ValidateScript(script); using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token)) {
                linked.Token.ThrowIfCancellationRequested(); var result = await content.EvaluateJavaScriptAsync(script, linked.Token); linked.Token.ThrowIfCancellationRequested(); return result;
            }
        }
        private async Task ContentOperation(Func<IBrowserContentBackend, CancellationToken, Task> operation, CancellationToken token) {
            var content = Content(); using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, token)) {
                linked.Token.ThrowIfCancellationRequested(); await operation(content, linked.Token); linked.Token.ThrowIfCancellationRequested();
            }
        }
        public Task SetTextZoomAsync(int percent, CancellationToken token = default) { BrowserContentPolicy.ValidateTextZoom(percent); return ContentOperation((content, ct) => content.SetTextZoomAsync(percent, ct), token); }
        public Task FindAsync(string query, long revision, CancellationToken token = default) { BrowserContentPolicy.ValidateFind(query, revision); return ContentOperation((content, ct) => content.FindAsync(query, revision, ct), token); }
        public Task FindNextAsync(bool forward, CancellationToken token = default) => ContentOperation((content, ct) => content.FindNextAsync(forward, ct), token);
        public Task ClearFindAsync(CancellationToken token = default) => ContentOperation((content, ct) => content.ClearFindAsync(ct), token);
        private void BackendFailed(Exception error)
        {
            int version = generation;
            try { dispatch(() => {
                if (version != generation || State == BrowserState.Disposed) return;
                LastBackendError = error;
                if (State == BrowserState.Ready) ChangeState(BrowserState.Faulted);
            }); }
            catch (Exception failure) { LastNotificationError = failure; }
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
            if (backend is IBrowserFailureSource failures) failures.Failed -= BackendFailed;
            if (backend is IBrowserNavigationBackend page) page.PageStateChanged -= PageChanged;
            if (backend is IBrowserContentBackend content) { content.FindResultChanged -= FindChanged; content.ContextRequested -= ContextChanged; }
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
