using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;
using T34400.VRWebView.Presentation;

internal static class UnityBridgeTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void Worker(Action action)
    {
        Exception failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.Start(); Check(thread.Join(5000), "Worker did not finish");
        if (failure != null) throw failure;
    }
    private sealed class Sink : IBrowserPointerSink, IBrowserScrollSink
    {
        internal readonly List<BrowserPointerEvent> Events = new List<BrowserPointerEvent>();
        internal readonly List<BrowserScrollEvent> Scrolls = new List<BrowserScrollEvent>();
        internal Action<BrowserPointerEvent> OnPointer;
        public void SendPointer(BrowserPointerEvent value) { Events.Add(value); OnPointer?.Invoke(value); }
        public void SendScroll(BrowserScrollEvent value) { Scrolls.Add(value); }
    }
    private sealed class Backend : IBrowserBackend
    {
        internal readonly TaskCompletionSource<bool> Init = new TaskCompletionSource<bool>();
        public Task InitializeAsync(CancellationToken token) { return Init.Task; }
        public Task NavigateAsync(Uri url, CancellationToken token) { return Task.CompletedTask; }
        public void Dispose() { }
    }

    internal static IEnumerable<(string Name, Action Run)> Scenarios()
    {
        yield return ("owner queue posts across threads and drains FIFO on its owner", () => {
            using var queue = new OwnerThreadDispatcher(); var seen = new List<int>(); int owner = Thread.CurrentThread.ManagedThreadId;
            Worker(() => { queue.Post(() => { Check(Thread.CurrentThread.ManagedThreadId == owner, "Action escaped owner"); seen.Add(1); }); queue.Post(() => seen.Add(2)); });
            Check(seen.Count == 0, "Worker executed action directly"); Check(queue.Drain() == 2 && seen.SequenceEqual(new[] { 1, 2 }), "Queue order lost");
        });
        yield return ("owner queue rejects worker drain and disposal without losing work", () => {
            using var queue = new OwnerThreadDispatcher(); int calls = 0; queue.Post(() => calls++);
            Worker(() => { Reject<InvalidOperationException>(() => queue.Drain()); Reject<InvalidOperationException>(() => queue.Dispose()); });
            queue.Drain(); Check(calls == 1 && !queue.IsDisposed, "Rejected worker operation damaged queue");
        });
        yield return ("owner queue capacity and per-frame budget preserve pending order", () => {
            using var queue = new OwnerThreadDispatcher(2); var seen = new List<int>();
            queue.Post(() => seen.Add(1)); queue.Post(() => seen.Add(2)); Reject<InvalidOperationException>(() => queue.Post(() => seen.Add(99)));
            Check(queue.Drain(1) == 1 && queue.PendingCount == 1, "Budget ignored"); queue.Post(() => seen.Add(3)); queue.Drain();
            Check(seen.SequenceEqual(new[] { 1, 2, 3 }), "Overflow or budget reordered work");
        });
        yield return ("owner queue keeps draining after callback and reporter errors", () => {
            using var queue = new OwnerThreadDispatcher(10, _ => throw new Exception("reporter failure")); bool completed = false;
            queue.Post(() => throw new InvalidOperationException("callback failure")); queue.Post(() => completed = true); queue.Drain();
            Check(completed && queue.LastError is InvalidOperationException, "Callback error stranded following work");
        });
        yield return ("owner queue closes a finite backlog and rejects captured old schedulers", () => {
            var queue = new OwnerThreadDispatcher(); Action<Action> old = queue.Post; int settled = 0;
            queue.Post(() => settled++); queue.Post(() => settled++); queue.Dispose(); queue.Dispose();
            Check(settled == 2 && queue.PendingCount == 0, "Shutdown dropped queued work");
            Reject<ObjectDisposedException>(() => old(() => settled++));
            using var replacement = new OwnerThreadDispatcher(); replacement.Post(() => settled++); replacement.Drain();
            Check(settled == 3, "Replacement queue did not remain independent");
        });
        yield return ("owner queue reentrant close finishes backlog without recursive drain", () => {
            var queue = new OwnerThreadDispatcher(); int settled = 0;
            queue.Post(() => { settled++; queue.Dispose(); }); queue.Post(() => settled++); queue.Drain(1);
            Check(queue.IsDisposed && settled == 2, "Reentrant shutdown left pending work");
        });
        yield return ("owner queue recursively draining action is rejected but later work survives", () => {
            using var queue = new OwnerThreadDispatcher(); bool completed = false;
            queue.Post(() => queue.Drain()); queue.Post(() => completed = true); queue.Drain();
            Check(completed && queue.LastError is InvalidOperationException, "Nested drain corrupted execution");
        });
        yield return ("real owner queue drives session init and close without a synchronization context", () => {
            var queue = new OwnerThreadDispatcher(); var backend = new Backend(); var session = new BrowserSession(backend, queue.Post);
            var init = session.InitializeAsync(); Worker(() => backend.Init.SetResult(true));
            Check(session.State == BrowserState.Initializing, "Worker bypassed owner queue"); queue.Drain();
            Check(init.IsCompletedSuccessfully && session.State == BrowserState.Ready, "Owner failed to initialize session");
            session.Dispose(); queue.Dispose(); Check(session.State == BrowserState.Disposed, "Queue close revived session");
        });
        yield return ("pointer router cancels old target once before a new target receives down", () => {
            var router = new BrowserPointerRouter(); var first = new Sink(); var next = new Sink(); router.Bind(first);
            router.Send(new BrowserPointerEvent(7, PointerPhase.Down, .2f, .3f)); router.Send(new BrowserPointerEvent(7, PointerPhase.Move, .4f, .5f));
            router.Bind(next); router.Bind(next);
            Check(first.Events.Count == 3 && first.Events[2].Phase == PointerPhase.Cancel && first.Events[2].X == .4f, "Old gesture cancellation missing/duplicated");
            Check(!router.Send(new BrowserPointerEvent(7, PointerPhase.Move, .5f, .5f)), "Held gesture moved into new target");
            Check(router.Send(new BrowserPointerEvent(7, PointerPhase.Down, .1f, .1f)) && next.Events.Count == 1, "Fresh new-target gesture missing");
        });
        yield return ("pointer router rejects foreign gestures but preserves captured outside coordinates", () => {
            var router = new BrowserPointerRouter(); var sink = new Sink(); router.Bind(sink);
            router.Send(new BrowserPointerEvent(1, PointerPhase.Down, 0, 0));
            Check(!router.Send(new BrowserPointerEvent(2, PointerPhase.Up, 0, 0)), "Foreign release accepted");
            Check(!router.Send(new BrowserPointerEvent(2, PointerPhase.Down, 0, 0)), "Capture stolen");
            Check(router.Send(new BrowserPointerEvent(1, PointerPhase.Move, -2, 3)), "Captured coordinates were clamped");
            router.Cancel(); router.Cancel(); Check(sink.Events.Count == 3 && sink.Events[2].X == -2 && sink.Events[2].Y == 3, "Cancel lost last mapped point");
        });
        yield return ("pointer cancellation reentry leaves the most recent target selected", () => {
            var router = new BrowserPointerRouter(); var first = new Sink(); var outer = new Sink(); var newest = new Sink();
            first.OnPointer = value => { if (value.Phase == PointerPhase.Cancel) router.Bind(newest); };
            router.Bind(first); router.Send(new BrowserPointerEvent(1, PointerPhase.Down, 0, 0));
            Check(!router.Bind(outer), "Stale outer target overwrote reentrant binding");
            router.Send(new BrowserPointerEvent(1, PointerPhase.Down, 0, 0)); Check(newest.Events.Count == 1 && outer.Events.Count == 0, "Wrong target received new input");
        });
        yield return ("pointer sink exception records error and cancels uncertain down once", () => {
            var router = new BrowserPointerRouter(); var sink = new Sink(); sink.OnPointer = value => { if (value.Phase == PointerPhase.Down) throw new InvalidOperationException("synthetic input failure"); };
            router.Bind(sink); Check(!router.Send(new BrowserPointerEvent(1, PointerPhase.Down, 0, 0)), "Failed down claimed success");
            Check(router.LastError != null && sink.Events.Count == 2 && sink.Events[1].Phase == PointerPhase.Cancel, "Failed gesture was retained");
            Check(!router.Send(new BrowserPointerEvent(1, PointerPhase.Move, 0, 0)), "Failed gesture continued");
        });
        yield return ("scroll routing preserves logical deltas and respects pointer ownership", () => {
            var router = new BrowserPointerRouter(); var sink = new Sink(); router.Bind(sink);
            router.Send(new BrowserPointerEvent(1, PointerPhase.Down, .5f, .5f));
            Check(!router.SendScroll(new BrowserScrollEvent(2, 0, 0, 0, 1)), "Foreign scroll during capture accepted");
            Check(router.SendScroll(new BrowserScrollEvent(1, .3f, .4f, -1, 2)) && sink.Scrolls[0].DeltaY == 2, "Logical scroll changed");
            Check(!router.SendScroll(new BrowserScrollEvent(1, 0, 0, 0, float.NaN)), "Nonfinite scroll reached backend");
        });
        yield return ("surface origin mapping makes top-first and bottom-first textures agree", () => {
            Check(BrowserSurfaceCoordinates.TryMap(.2f, .25f, 0, 1, 1, -1, BrowserSurfaceOrigin.TopLeft, out float x, out float y) && Math.Abs(y - .25f) < .00001, "Top-first texture input inverted");
            Check(BrowserSurfaceCoordinates.TryMap(.2f, .25f, 0, 0, 1, 1, BrowserSurfaceOrigin.BottomLeft, out x, out y) && Math.Abs(y - .25f) < .00001, "Bottom-first texture input inverted");
        });
        yield return ("surface mapping preserves crop and captured drag while rejecting invalid spans", () => {
            Check(BrowserSurfaceCoordinates.TryMap(.5f, .2f, .1f, .2f, .6f, .5f, BrowserSurfaceOrigin.TopLeft, out float x, out float y) && Math.Abs(x - .4f) < .00001 && Math.Abs(y - .6f) < .00001, "Crop lost");
            Check(BrowserSurfaceCoordinates.TryMap(-.5f, 1.5f, 0, 0, 1, 1, BrowserSurfaceOrigin.BottomLeft, out x, out y) && x == -.5f && y == 1.5f, "Captured drag clamped");
            Check(!BrowserSurfaceCoordinates.TryMap(0, 0, 0, 0, 0, 1, BrowserSurfaceOrigin.TopLeft, out x, out y), "Collapsed UV accepted");
            Check(!BrowserSurfaceCoordinates.TryMap(float.NaN, 0, 0, 0, 1, 1, BrowserSurfaceOrigin.TopLeft, out x, out y), "Nonfinite input accepted");
        });
    }
}
