using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;

internal static class BackendCapabilityTests
{
    private sealed class Backend : IBrowserBackend, IBrowserHtmlBackend, IBrowserFailureSource
    {
        public event Action<Exception> Failed;
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
