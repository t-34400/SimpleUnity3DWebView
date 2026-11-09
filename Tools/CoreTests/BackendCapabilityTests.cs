using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;

internal static class BackendCapabilityTests
{
    private sealed class Backend : IBrowserBackend, IBrowserHtmlBackend, IBrowserFailureSource, IBrowserNavigationBackend
    {
        public event Action<Exception> Failed;
        public event Action<BrowserPageState> PageStateChanged;
        public BrowserPageState PageState { get; private set; } = BrowserPageState.Empty;
        internal int Commands;
        public Task ExecuteAsync(BrowserPageCommand command, CancellationToken token) { token.ThrowIfCancellationRequested(); Commands++; return Task.CompletedTask; }
        internal void Observe(BrowserPageState state) { PageState = state; PageStateChanged?.Invoke(state); }
        internal Uri BaseUrl;
        internal string Html;
        internal int Calls;
        internal CancellationToken Token;
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task NavigateAsync(Uri url, CancellationToken token) => Task.CompletedTask;
        public Task LoadHtmlAsync(string html, Uri baseUrl, CancellationToken token) {
            Calls++; Html = html; BaseUrl = baseUrl; Token = token; return Task.CompletedTask;
        }
        internal void Fail(Exception error) => Failed?.Invoke(error);
        public void Dispose() { }
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Reject<T>(Task task) where T : Exception {
        try { await task; } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name);
    }
    internal static IEnumerable<(string Name, Func<Task> Run)> Scenarios()
    {
        yield return ("page observations cross the owner queue and disposed replies are discarded", async () => {
            var backend = new Backend(); var queue = new Queue<Action>(); var session = new BrowserSession(backend, action => queue.Enqueue(action));
            int observed = 0; session.PageStateChanged += state => observed++;
            backend.Observe(new BrowserPageState("https://example.com/", "Example", true, false, false, 100, 0));
            Check(observed == 0, "Page event bypassed dispatcher"); while (queue.Count > 0) queue.Dequeue()(); Check(observed == 1, "Owner lost page observation");
            backend.Observe(BrowserPageState.Empty); session.Dispose(); while (queue.Count > 0) queue.Dequeue()();
            Check(observed == 1, "Disposed page delivered an old observation"); await Task.CompletedTask;
        });
        yield return ("page command cancellation and sample leases preserve navigation capability", async () => {
            var backend = new Backend(); using var lease = new T34400.VRWebView.Samples.Browser.BrowserBackendLease(backend);
            using var session = new BrowserSession(lease, action => action()); await session.InitializeAsync();
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Reject<OperationCanceledException>(session.ExecuteAsync(BrowserPageCommand.Reload, cancel.Token));
            Check(backend.Commands == 0, "Canceled command reached backend");
            await session.ExecuteAsync(BrowserPageCommand.Back); Check(backend.Commands == 1, "Sample lease swallowed command");
            var state = new BrowserPageState("https://example.com/", "Example", true, false, false, 100, 0); backend.Observe(state);
            Check(ReferenceEquals(session.PageState, state), "Sample lease lost page state");
        });
        yield return ("page observers do not interrupt other observers", async () => {
            var backend = new Backend(); using var session = new BrowserSession(backend, action => action()); int calls = 0;
            session.PageStateChanged += _ => throw new Exception("observer"); session.PageStateChanged += _ => calls++;
            backend.Observe(BrowserPageState.Empty); Check(calls == 1 && session.LastNotificationError != null, "Observer failure escaped"); await Task.CompletedTask;
        });
        yield return ("sample leases propagate idle runtime failures into the tab model", async () => {
            var backend = new Backend();
            using var workspace = new T34400.VRWebView.Samples.Browser.BrowserWorkspace(
                () => new T34400.VRWebView.Samples.Browser.BrowserBackendLease(backend), a => a());
            var tab = workspace.CreateTab(); await tab.InitializeAsync();
            var failure = new InvalidOperationException("gpu_failed"); backend.Fail(failure);
            Check(tab.State == BrowserState.Faulted && ReferenceEquals(tab.LastError, failure), "Sample hid runtime failure");
            workspace.CloseTab(tab.Id); backend.Fail(new Exception("late"));
            Check(tab.IsClosed && tab.State == BrowserState.Disposed, "Late source fault revived closed sample");
        });
        yield return ("HTML submission validates base URLs and preserves application data", async () => {
            var backend = new Backend(); using var session = new BrowserSession(backend, a => a());
            await session.InitializeAsync(); string html = "<input value='日本語'>";
            await session.LoadHtmlAsync(html, "https://example.com/root/");
            Check(backend.Html == html && backend.BaseUrl.AbsoluteUri == "https://example.com/root/", "HTML or base URL changed");
            await Reject<ArgumentException>(session.LoadHtmlAsync(html, "file:///private"));
            await Reject<ArgumentException>(session.LoadHtmlAsync(html, "https://user:password@example.com/"));
            Check(backend.Calls == 1, "Rejected URL reached platform");
        });
        yield return ("cancelled HTML submission never enters the backend", async () => {
            var backend = new Backend(); using var session = new BrowserSession(backend, a => a()); await session.InitializeAsync();
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Reject<OperationCanceledException>(session.LoadHtmlAsync("test", cancellationToken:cancellation.Token));
            Check(backend.Calls == 0, "Cancelled HTML reached platform");
        });
        yield return ("runtime failures change session state only through its owner dispatcher", async () => {
            var backend = new Backend(); var queue = new Queue<Action>();
            using var session = new BrowserSession(backend, a => { lock (queue) queue.Enqueue(a); });
            Task init = session.InitializeAsync();
            while (true) { Action action; lock(queue) { if (queue.Count == 0) break; action = queue.Dequeue(); } action(); }
            await init;
            var failure = new InvalidOperationException("producer_draw_failed");
            await Task.Run(() => backend.Fail(failure));
            Check(session.State == BrowserState.Ready, "Worker changed owner state");
            while (true) { Action action; lock(queue) { if (queue.Count == 0) break; action = queue.Dequeue(); } action(); }
            Check(session.State == BrowserState.Faulted && ReferenceEquals(session.LastBackendError, failure), "Fatal platform failure was hidden");
            await Reject<InvalidOperationException>(session.NavigateAsync("https://example.com/"));
        });
        yield return ("late backend failure cannot revive a disposed session", async () => {
            var backend = new Backend(); var queue = new Queue<Action>();
            var session = new BrowserSession(backend, a => queue.Enqueue(a)); Task init = session.InitializeAsync();
            while (queue.Count > 0) queue.Dequeue()(); await init;
            backend.Fail(new Exception("late")); session.Dispose();
            while (queue.Count > 0) queue.Dequeue()(); backend.Fail(new Exception("after_unsubscribe"));
            Check(session.State == BrowserState.Disposed && session.LastBackendError == null, "Old failure reached disposed session");
        });
    }
}
