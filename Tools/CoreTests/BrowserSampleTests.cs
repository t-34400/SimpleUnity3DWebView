using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;
using T34400.VRWebView.Samples.Browser;

internal static class BrowserSampleTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static T Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class Backend : IBrowserBackend
    {
        internal int Initializations, Navigations, Disposals;
        internal bool FailClose;
        public Task InitializeAsync(CancellationToken token) { Initializations++; return Task.CompletedTask; }
        public Task NavigateAsync(Uri address, CancellationToken token) { Navigations++; return Task.CompletedTask; }
        public void Dispose() { Disposals++; if (FailClose) throw new InvalidOperationException("synthetic backend close failure"); }
    }
    private sealed class Connection : IDisposable
    {
        internal int Disposals;
        internal bool FailClose;
        public void Dispose() { Disposals++; if (FailClose) throw new InvalidOperationException("synthetic connection close failure"); }
    }

    internal static IEnumerable<(string Name, Action Run)> Scenarios()
    {
        yield return ("address draft acknowledges requested URI once without claiming committed location", () => {
            var draft = new BrowserAddressDraft(); draft.Edit("https://example.invalid"); var submitted = draft.Capture();
            Check(draft.Acknowledge(submitted, new Uri("https://example.invalid/")) && draft.Text.EndsWith("/"), "Requested address not reflected");
            Check(!draft.Acknowledge(submitted, new Uri("https://example.invalid/other")), "Reply replay overwrote draft");
        });
        yield return ("address draft preserves edits made while a navigation request is pending", () => {
            var draft = new BrowserAddressDraft(); draft.Edit("https://example.invalid/old"); var submitted = draft.Capture();
            draft.Edit("https://example.invalid/new");
            Check(!draft.Acknowledge(submitted, new Uri(submitted.Text)) && draft.Text.EndsWith("new"), "Late navigation overwrote current typing");
        });
        yield return ("address edit away and back still invalidates the old submission", () => {
            var draft = new BrowserAddressDraft(); draft.Edit("https://example.invalid/"); var submitted = draft.Capture();
            draft.Edit("temporary text"); draft.Edit(submitted.Text);
            Check(!draft.Acknowledge(submitted, new Uri("https://example.invalid/late")), "String equality confused edit ownership");
        });
        yield return ("address submission cannot update a different tab with identical text", () => {
            var left = new BrowserAddressDraft(); var right = new BrowserAddressDraft(); left.Edit("https://example.invalid/"); right.Edit(left.Text);
            Check(!right.Acknowledge(left.Capture(), new Uri("https://example.invalid/foreign")), "Foreign tab reply accepted");
            Check(right.Text == left.Text, "Foreign reply changed the draft");
        });
        yield return ("invalid address acknowledgement does not consume a valid receipt", () => {
            var draft = new BrowserAddressDraft(); var submitted = draft.Capture();
            Reject<ArgumentException>(() => draft.Acknowledge(submitted, new Uri("relative", UriKind.Relative)));
            Check(draft.Acknowledge(submitted, new Uri("https://example.invalid/")), "Invalid URI damaged receipt");
        });
        yield return ("backend lease forwards commands and closes both logical owners once", () => {
            var backend = new Backend(); var connection = new Connection(); var lease = new BrowserBackendLease(backend, connection);
            lease.InitializeAsync(default).GetAwaiter().GetResult(); lease.NavigateAsync(new Uri("https://example.invalid/"), default).GetAwaiter().GetResult();
            lease.Dispose(); lease.Dispose();
            Check(backend.Initializations == 1 && backend.Navigations == 1 && backend.Disposals == 1 && connection.Disposals == 1, "Ownership or command forwarding lost");
            Reject<ObjectDisposedException>(() => lease.InitializeAsync(default));
        });
        yield return ("backend failure still closes its independent connection lifetime", () => {
            var backend = new Backend { FailClose = true }; var connection = new Connection(); var lease = new BrowserBackendLease(backend, connection);
            var error = Reject<AggregateException>(() => lease.Dispose()); lease.Dispose();
            Check(error.InnerExceptions.Count == 1 && backend.Disposals == 1 && connection.Disposals == 1, "Close failure leaked or retried ownership");
        });
        yield return ("backend and connection failure retain both diagnostics", () => {
            var backend = new Backend { FailClose = true }; var connection = new Connection { FailClose = true }; var lease = new BrowserBackendLease(backend, connection);
            var error = Reject<AggregateException>(() => lease.Dispose());
            Check(error.InnerExceptions.Count == 2 && connection.Disposals == 1, "One cleanup failure hid the other");
        });
        yield return ("one object serving as both backend and connection is not disposed twice", () => {
            var backend = new Backend(); var lease = new BrowserBackendLease(backend, backend); lease.Dispose();
            Check(backend.Disposals == 1, "Aliased ownership disposed twice");
        });
        yield return ("workspace selection retains connection lifetimes and explicit close releases one", () => {
            var backends = new List<Backend>(); var connections = new List<Connection>();
            var workspace = new BrowserWorkspace(() => { var backend = new Backend(); var connection = new Connection(); backends.Add(backend); connections.Add(connection); return new BrowserBackendLease(backend, connection); }, action => action());
            var first = workspace.CreateTab(); var next = workspace.CreateTab(); workspace.SelectTab(first.Id);
            Check(connections[0].Disposals == 0 && connections[1].Disposals == 0, "Selection closed a background connection");
            workspace.CloseTab(first.Id); Check(connections[0].Disposals == 1 && connections[1].Disposals == 0 && workspace.ActiveTab == next, "Close affected the wrong connection");
            workspace.Dispose(); Check(backends[0].Disposals == 1 && backends[1].Disposals == 1 && connections[1].Disposals == 1, "Workspace leaked ownership");
        });
    }
}
