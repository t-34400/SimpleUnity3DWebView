using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;
using T34400.VRWebView.Presentation;
using T34400.VRWebView.Samples.Browser;

internal static class UpperLayerTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Rejected<T>(Task task) where T : Exception
    { Reject<T>(() => task.GetAwaiter().GetResult()); }

    private sealed class Owner
    {
        private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        internal readonly int ThreadId = Environment.CurrentManagedThreadId;
        internal void Post(Action action) { queue.Enqueue(action); }
        internal void Drain()
        {
            Check(Environment.CurrentManagedThreadId == ThreadId, "Owner queue drained on another thread");
            int count = 0;
            while (queue.TryDequeue(out var action)) {
                Check(++count < 10000, "Notification loop"); action();
            }
        }
        internal void Until(Task task)
        {
            var end = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < end) { Drain(); Thread.Yield(); }
            Drain(); Check(task.IsCompleted, "Task did not settle through the owner queue");
        }
    }

    private sealed class Backend : IBrowserBackend
    {
        internal Task Init = Task.CompletedTask;
        internal Task Navigation = Task.CompletedTask;
        internal int Initializations, Navigations, Disposals;
        internal bool ThrowOnDispose;
        internal readonly List<int> Threads = new List<int>();
        public Task InitializeAsync(CancellationToken token) { Threads.Add(Environment.CurrentManagedThreadId); Initializations++; return Init; }
        public Task NavigateAsync(Uri url, CancellationToken token) { Threads.Add(Environment.CurrentManagedThreadId); Navigations++; return Navigation; }
        public void Dispose() { Threads.Add(Environment.CurrentManagedThreadId); Disposals++; if (ThrowOnDispose) throw new InvalidOperationException("synthetic close failure"); }
    }

    private sealed class Source : IBrowserSurfaceSource<object>, IDisposable
    {
        private BrowserSurfaceSnapshot<object> current;
        internal Action OnRead;
        public BrowserSurfaceSnapshot<object> Current { get { var callback = OnRead; OnRead = null; callback?.Invoke(); return current; } }
        public event Action<BrowserSurfaceSnapshot<object>> Changed;
        internal int Disposals;
        internal void Publish(BrowserSurfaceSnapshot<object> value) { current = value; Changed?.Invoke(value); }
        public void Dispose() { Disposals++; }
    }

    private static BrowserTab Ready(BrowserWorkspace workspace, Owner owner)
    {
        var tab = workspace.CreateTab(); var init = tab.InitializeAsync(); owner.Until(init); init.GetAwaiter().GetResult(); return tab;
    }

    internal static IEnumerable<(string Name, Action Run)> Scenarios()
    {
        yield return ("session completion changes state only after dispatch to its owner", () => {
            var owner = new Owner(); var finish = new TaskCompletionSource<bool>();
            var backend = new Backend { Init = finish.Task }; using var session = new BrowserSession(backend, owner.Post);
            var threads = new List<int>(); session.StateChanged += _ => threads.Add(Environment.CurrentManagedThreadId);
            var init = session.InitializeAsync();
            Task.Run(() => finish.SetResult(true)).GetAwaiter().GetResult();
            Check(session.State == BrowserState.Initializing && !init.IsCompleted, "Worker completion mutated owner state");
            owner.Until(init); init.GetAwaiter().GetResult();
            Check(session.State == BrowserState.Ready && threads.All(id => id == owner.ThreadId), "State notification escaped the owner");
        });
        yield return ("session disposal cancels init even when backend ignores cancellation", () => {
            var owner = new Owner(); var finish = new TaskCompletionSource<bool>(); var backend = new Backend { Init = finish.Task };
            var session = new BrowserSession(backend, owner.Post); var init = session.InitializeAsync(); session.Dispose();
            Check(init.IsCanceled && backend.Disposals == 1, "Logical close waited for a broken backend");
            finish.SetResult(true); owner.Drain();
            Check(session.State == BrowserState.Disposed, "Late init revived a closed session");
        });
        yield return ("session unexpected backend cancellation is observable and not Ready", () => {
            var owner = new Owner(); var backend = new Backend { Init = Task.FromCanceled(new CancellationToken(true)) };
            using var session = new BrowserSession(backend, owner.Post); var init = session.InitializeAsync(); owner.Until(init);
            Rejected<OperationCanceledException>(init); Check(session.State == BrowserState.Faulted, "Canceled backend left a usable session");
        });
        yield return ("session notification exceptions do not break lifetime or other listeners", () => {
            var backend = new Backend(); using var session = new BrowserSession(backend, action => action()); int ready = 0;
            session.StateChanged += _ => throw new InvalidOperationException("synthetic observer failure");
            session.StateChanged += state => { if (state == BrowserState.Ready) ready++; };
            session.InitializeAsync().GetAwaiter().GetResult();
            Check(ready == 1 && session.LastNotificationError != null && session.State == BrowserState.Ready, "Observer exception corrupted initialization");
        });
        yield return ("surface observations validate revision and withdrawal dimensions", () => {
            Reject<ArgumentOutOfRangeException>(() => new BrowserSurfaceSnapshot<object>(0, new object(), 1, 1));
            Reject<ArgumentOutOfRangeException>(() => new BrowserSurfaceSnapshot<object>(1, null, 1, 1));
            Reject<ArgumentOutOfRangeException>(() => new BrowserSurfaceSnapshot<object>(1, new object(), 0, 1));
        });
        yield return ("surface binding borrows current surface and never disposes its owner", () => {
            var owner = new Owner(); var source = new Source(); var resource = new object(); source.Publish(new BrowserSurfaceSnapshot<object>(1, resource, 100, 200));
            BrowserSurfaceSnapshot<object> shown = null; var binding = new BrowserSurfaceBinding<object>(owner.Post, value => shown = value);
            binding.Bind(source); Check(shown.Surface == resource && binding.Current.Width == 100, "Initial borrow missing");
            binding.Dispose(); binding.Dispose(); Check(shown == null && source.Disposals == 0, "Binding destroyed an owned resource/provider");
        });
        yield return ("surface source replacement rejects already queued old-source events", () => {
            var owner = new Owner(); var first = new Source(); var next = new Source(); var replacement = new object();
            next.Publish(new BrowserSurfaceSnapshot<object>(1, replacement, 10, 20));
            using var binding = new BrowserSurfaceBinding<object>(owner.Post, _ => { }); binding.Bind(first);
            first.Publish(new BrowserSurfaceSnapshot<object>(100, new object(), 20, 20)); binding.Bind(next); owner.Drain();
            Check(binding.Current.Surface == replacement && binding.Current.Revision == 1, "Old source overwrote a replacement");
        });
        yield return ("surface withdrawal and late revisions cannot restore an obsolete texture", () => {
            var owner = new Owner(); var source = new Source(); using var binding = new BrowserSurfaceBinding<object>(owner.Post, _ => { });
            binding.Bind(source); source.Publish(new BrowserSurfaceSnapshot<object>(3, null, 0, 0));
            source.Publish(new BrowserSurfaceSnapshot<object>(2, new object(), 1, 1)); owner.Drain();
            Check(binding.Current.Revision == 3 && !binding.Current.HasSurface, "Late surface revived after withdrawal");
        });
        yield return ("surface callbacks execute on owner and disappear after detach or disposal", () => {
            var owner = new Owner(); var source = new Source(); int calls = 0;
            var binding = new BrowserSurfaceBinding<object>(owner.Post, value => { Check(Environment.CurrentManagedThreadId == owner.ThreadId, "Presenter escaped owner"); calls++; });
            binding.Bind(source); Task.Run(() => source.Publish(new BrowserSurfaceSnapshot<object>(1, new object(), 1, 1))).GetAwaiter().GetResult();
            Check(calls == 1, "Worker presented directly"); binding.Bind(null); owner.Drain(); Check(calls == 2, "Detached event was delivered");
            binding.Bind(source); source.Publish(new BrowserSurfaceSnapshot<object>(2, null, 0, 0)); binding.Dispose(); int closed = calls; owner.Drain();
            Check(calls == closed, "Disposed binding delivered queued surface"); Reject<ObjectDisposedException>(() => binding.Bind(source));
        });
        yield return ("surface presenter exceptions do not leave a partially subscribed binding", () => {
            var source = new Source(); using var binding = new BrowserSurfaceBinding<object>(a => a(), _ => throw new InvalidOperationException("synthetic view failure"));
            binding.Bind(source); source.Publish(new BrowserSurfaceSnapshot<object>(1, new object(), 1, 1));
            Check(binding.Current.Revision == 1 && binding.LastPresentationError != null, "Presenter error damaged model state");
        });
        yield return ("surface getter reentry cannot overwrite a newly selected source", () => {
            var first = new Source(); var next = new Source(); var replacement = new object();
            first.Publish(new BrowserSurfaceSnapshot<object>(10, new object(), 1, 1)); next.Publish(new BrowserSurfaceSnapshot<object>(1, replacement, 2, 2));
            using var binding = new BrowserSurfaceBinding<object>(a => a(), _ => { }); first.OnRead = () => binding.Bind(next);
            binding.Bind(first); Check(binding.Current.Surface == replacement, "Reentrant getter restored the old source");
        });
        yield return ("session broken owner dispatcher fails the task without claiming Ready", () => {
            using var session = new BrowserSession(new Backend(), _ => throw new InvalidOperationException("synthetic dispatch failure"));
            Rejected<InvalidOperationException>(session.InitializeAsync());
            Check(session.State != BrowserState.Ready && session.LastNotificationError != null, "Broken dispatcher established a ready session");
        });
        yield return ("workspace selection preserves background sessions and stable tab IDs", () => {
            var owner = new Owner(); var backends = new List<Backend>();
            using var workspace = new BrowserWorkspace(() => { var backend = new Backend(); backends.Add(backend); return backend; }, owner.Post);
            var first = Ready(workspace, owner); var next = Ready(workspace, owner);
            Check(first.Id != next.Id && workspace.ActiveTab == next, "Tab identity/selection lost");
            Check(workspace.SelectTab(first.Id) && !workspace.SelectTab(-1), "Selection contract");
            Check(backends.All(b => b.Disposals == 0 && b.Initializations == 1), "Selection recreated or destroyed a session");
        });
        yield return ("workspace closing active tab picks a neighbor and closes each backend once", () => {
            var owner = new Owner(); var backends = new List<Backend>();
            var workspace = new BrowserWorkspace(() => { var b = new Backend(); backends.Add(b); return b; }, owner.Post);
            var a = workspace.CreateTab(); var b = workspace.CreateTab(); var c = workspace.CreateTab(); workspace.SelectTab(b.Id);
            Check(workspace.CloseTab(b.Id) && workspace.ActiveTab == c && b.IsClosed, "Active close did not select right neighbor");
            Check(!workspace.CloseTab(b.Id), "Repeated close accepted"); workspace.Dispose(); workspace.Dispose();
            Check(a.IsClosed && c.IsClosed && workspace.ActiveTab == null && backends.All(x => x.Disposals == 1), "Workspace leaked or double closed");
        });
        yield return ("workspace validates addresses before changing requested address or busy state", () => {
            var owner = new Owner(); var backend = new Backend(); using var workspace = new BrowserWorkspace(() => backend, owner.Post); var tab = Ready(workspace, owner);
            Reject<ArgumentException>(() => tab.NavigateAsync("file:///not-a-page"));
            Check(tab.RequestedAddress == null && !tab.IsBusy && backend.Navigations == 0, "Invalid address changed the tab");
        });
        yield return ("workspace command completion is not a committed URL or page-load event", () => {
            var owner = new Owner(); var backend = new Backend(); using var workspace = new BrowserWorkspace(() => backend, owner.Post); var tab = Ready(workspace, owner);
            var request = tab.NavigateAsync("https://example.invalid/requested"); owner.Until(request); request.GetAwaiter().GetResult();
            Check(tab.RequestedAddress.AbsolutePath == "/requested" && !tab.IsBusy && backend.Navigations == 1, "Requested address command failed");
            Check(backend.Threads.All(id => id == owner.ThreadId), "Backend invoked off owner thread");
        });
        yield return ("workspace closed pending navigation cannot update or replace a newer tab", () => {
            var owner = new Owner(); var finish = new TaskCompletionSource<bool>(); var oldBackend = new Backend { Navigation = finish.Task }; int count = 0;
            using var workspace = new BrowserWorkspace(() => ++count == 1 ? oldBackend : new Backend(), owner.Post);
            var old = Ready(workspace, owner); var task = old.NavigateAsync("https://example.invalid/old"); workspace.CloseTab(old.Id);
            Check(task.IsCanceled && old.IsClosed, "Close did not cancel the logical command promptly"); var replacement = Ready(workspace, owner);
            finish.SetResult(true); owner.Drain(); Check(workspace.ActiveTab == replacement && replacement.RequestedAddress == null && old.IsClosed && !old.IsBusy, "Late navigation modified replacement");
        });
        yield return ("workspace failed initialization keeps a visible failed tab and shared init task", () => {
            var owner = new Owner(); var backend = new Backend { Init = Task.FromException(new InvalidOperationException("synthetic init failure")) };
            using var workspace = new BrowserWorkspace(() => backend, owner.Post); var tab = workspace.CreateTab(); var task = tab.InitializeAsync();
            Check(ReferenceEquals(task, tab.InitializeAsync()), "Init was not shared"); owner.Until(task); Rejected<InvalidOperationException>(task);
            Check(workspace.Tabs.Contains(tab) && tab.State == BrowserState.Faulted && tab.LastError != null && !tab.IsBusy, "Failure hidden or tab leaked");
        });
        yield return ("workspace backend factory failure leaves no tab or stale selection", () => {
            using var workspace = new BrowserWorkspace(() => null, a => a()); Reject<InvalidOperationException>(() => workspace.CreateTab());
            Check(workspace.Tabs.Count == 0 && workspace.ActiveTab == null, "Failed creation inserted a tab");
        });
        yield return ("workspace reentrant factory cannot duplicate tab identifiers", () => {
            BrowserWorkspace workspace = null; workspace = new BrowserWorkspace(() => { Reject<InvalidOperationException>(() => workspace.CreateTab()); return new Backend(); }, a => a());
            using (workspace) { var a = workspace.CreateTab(); var b = workspace.CreateTab(); Check(a.Id != b.Id && workspace.Tabs.Count == 2, "Reentrant factory corrupted IDs"); }
        });
        yield return ("workspace close failures do not prevent other sessions from closing", () => {
            var backends = new List<Backend>(); var workspace = new BrowserWorkspace(() => { var b = new Backend { ThrowOnDispose = backends.Count == 0 }; backends.Add(b); return b; }, a => a());
            workspace.CreateTab(); workspace.CreateTab(); Reject<AggregateException>(() => workspace.Dispose());
            Check(workspace.Tabs.Count == 0 && backends.All(x => x.Disposals == 1), "One close failure leaked other tabs"); workspace.Dispose();
        });
        yield return ("workspace observer failure cannot interrupt ownership or other observers", () => {
            int notices = 0; var backend = new Backend(); using var workspace = new BrowserWorkspace(() => backend, a => a());
            workspace.Changed += () => throw new InvalidOperationException("synthetic UI observer failure"); workspace.Changed += () => notices++;
            var tab = workspace.CreateTab(); Check(notices == 1 && workspace.LastNotificationError != null, "Observer error was lost");
            workspace.CloseTab(tab.Id); Check(backend.Disposals == 1 && notices == 2, "Observer prevented backend close");
        });
        yield return ("workspace concurrent navigation is rejected without stealing the first command", () => {
            var owner = new Owner(); var finish = new TaskCompletionSource<bool>(); var backend = new Backend { Navigation = finish.Task };
            using var workspace = new BrowserWorkspace(() => backend, owner.Post); var tab = Ready(workspace, owner);
            var first = tab.NavigateAsync("https://example.invalid/first"); Reject<InvalidOperationException>(() => tab.NavigateAsync("https://example.invalid/second"));
            Check(tab.RequestedAddress.AbsolutePath == "/first" && backend.Navigations == 1, "Rejected request changed pending navigation");
            finish.SetResult(true); owner.Until(first); first.GetAwaiter().GetResult();
        });
        yield return ("workspace reentrant close during busy notification prevents backend invocation", () => {
            var backend = new Backend(); using var workspace = new BrowserWorkspace(() => backend, action => action());
            var tab = workspace.CreateTab(); workspace.Changed += () => { if (tab.IsBusy) workspace.CloseTab(tab.Id); };
            var init = tab.InitializeAsync(); Check(init.IsCanceled && tab.IsClosed && backend.Initializations == 0 && backend.Disposals == 1, "Reentrant close started or leaked a backend");
        });
        yield return ("workspace disposal during factory execution releases the returned backend", () => {
            var backend = new Backend(); BrowserWorkspace workspace = null;
            workspace = new BrowserWorkspace(() => { workspace.Dispose(); return backend; }, action => action());
            Reject<ObjectDisposedException>(() => workspace.CreateTab());
            Check(backend.Disposals == 1 && workspace.Tabs.Count == 0, "Factory return escaped disposed ownership");
        });
        yield return ("workspace individual close failure still removes the tab and selects its neighbor", () => {
            var backends = new List<Backend>(); using var workspace = new BrowserWorkspace(() => { var b = new Backend(); backends.Add(b); return b; }, action => action());
            var a = workspace.CreateTab(); var b = workspace.CreateTab(); backends[1].ThrowOnDispose = true;
            Reject<InvalidOperationException>(() => workspace.CloseTab(b.Id));
            Check(b.IsClosed && workspace.ActiveTab == a && workspace.Tabs.Count == 1 && backends[1].Disposals == 1, "Close failure left a selected dead tab");
        });
    }
}
