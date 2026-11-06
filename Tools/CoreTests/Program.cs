using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Rejects<T>(Task task) where T : Exception
    { try { await task; } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static async Task Test(string label, Func<Task> action)
    { await action(); passed++; Console.WriteLine("PASS " + label); }
    private static Task Sync(Action action) { action(); return Task.CompletedTask; }

    private sealed class Backend : IBrowserBackend
    {
        public readonly TaskCompletionSource<bool> Init = new TaskCompletionSource<bool>();
        public int Initializations, Disposals, Navigations;
        public Uri LastUrl;
        public Task InitializeAsync(CancellationToken token)
        { Initializations++; token.Register(() => Init.TrySetCanceled()); return Init.Task; }
        public Task NavigateAsync(Uri url, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Navigations++; LastUrl = url; return Task.CompletedTask; }
        public void Dispose() { Disposals++; }
    }

    public static async Task Main()
    {
        await Test("initialization is shared and navigation waits for readiness", async () => {
            var backend = new Backend(); using var session = new BrowserSession(backend, a => a());
            await Rejects<InvalidOperationException>(session.NavigateAsync("https://example.invalid"));
            var init = session.InitializeAsync();
            Check(ReferenceEquals(init, session.InitializeAsync()), "Duplicate initialization");
            Check(backend.Initializations == 1 && session.State == BrowserState.Initializing, "Initial state");
            backend.Init.SetResult(true); await init;
            await session.NavigateAsync("https://example.invalid/page");
            Check(session.State == BrowserState.Ready && backend.Navigations == 1, "Ready navigation");
        });
        await Test("dispose cancels pending init and never returns to ready", async () => {
            var backend = new Backend(); var session = new BrowserSession(backend, a => a());
            var init = session.InitializeAsync(); session.Dispose(); session.Dispose();
            await Rejects<OperationCanceledException>(init);
            Check(session.State == BrowserState.Disposed && backend.Disposals == 1, "Disposed state");
            Throws<ObjectDisposedException>(() => session.InitializeAsync());
        });
        await Test("initialization errors are observable", async () => {
            var backend = new Backend(); using var session = new BrowserSession(backend, a => a());
            var init = session.InitializeAsync(); backend.Init.SetException(new InvalidOperationException("probe failure"));
            await Rejects<InvalidOperationException>(init);
            Check(session.State == BrowserState.Faulted, "Failure was hidden");
        });
        await Test("queued old-generation notifications are dropped", async () => {
            var queue = new Queue<Action>(); var seen = new List<BrowserState>();
            var backend = new Backend(); var session = new BrowserSession(backend, a => queue.Enqueue(a));
            session.StateChanged += seen.Add;
            var init = session.InitializeAsync(); backend.Init.SetResult(true); await init;
            session.Dispose(); while (queue.Count > 0) queue.Dequeue()();
            Check(seen.Count == 1 && seen[0] == BrowserState.Disposed, "Stale notification delivered");
        });
        await Test("canceled navigation does not reach backend", async () => {
            var backend = new Backend(); backend.Init.SetResult(true);
            using var session = new BrowserSession(backend, a => a()); await session.InitializeAsync();
            using var source = new CancellationTokenSource(); source.Cancel();
            await Rejects<OperationCanceledException>(session.NavigateAsync("https://example.invalid", source.Token));
            Check(backend.Navigations == 0, "Canceled navigation dispatched");
        });
        await Test("navigation rejects unsafe schemes and URL credentials", () => Sync(() => {
            foreach (var address in new[] { "javascript:alert(1)", "file:///tmp/a", "relative", "https://user:secret@example.invalid" })
                Throws<ArgumentException>(() => NavigationPolicy.Parse(address));
            Check(NavigationPolicy.Parse("https://example.invalid/a").Host == "example.invalid", "Valid URL rejected");
        }));
        await Test("second pointer cannot steal capture", () => Sync(() => {
            var capture = new PointerCapture(); var events = new List<BrowserPointerEvent>(); capture.Received += events.Add;
            Check(capture.Send(1, PointerPhase.Down, .2f, .2f), "Down rejected");
            Check(!capture.Send(2, PointerPhase.Down, .3f, .3f), "Capture stolen");
            Check(!capture.Send(2, PointerPhase.Up, .3f, .3f), "Foreign Up accepted");
            Check(capture.Send(1, PointerPhase.Up, .2f, .2f) && events.Count == 2, "Owner Up lost");
        }));
        await Test("drag returning to press point preserves movement and actual release", () => Sync(() => {
            var capture = new PointerCapture(); var events = new List<BrowserPointerEvent>(); capture.Received += events.Add;
            capture.Send(1, PointerPhase.Down, .5f, .5f); capture.Send(1, PointerPhase.Move, .9f, .9f);
            capture.Send(1, PointerPhase.Move, .51f, .51f); capture.Send(1, PointerPhase.Up, .51f, .51f);
            Check(events.Count == 4 && events[3].X == .51f, "Slop discarded real motion");
        }));
        await Test("cancel releases capture exactly once", () => Sync(() => {
            var capture = new PointerCapture(); int canceled = 0;
            capture.Received += e => { if (e.Phase == PointerPhase.Cancel) canceled++; };
            capture.Send(8, PointerPhase.Down, 0, 0); capture.Cancel(); capture.Cancel();
            Check(canceled == 1 && capture.Send(9, PointerPhase.Down, 0, 0), "Cancel failed");
        }));
        await Test("coordinates support arbitrary pivot and captured out-of-bounds drag", () => Sync(() => {
            Check(BrowserCoordinates.TryNormalize(25, 75, 0, 0, 100, 100, out float x, out float y)
                && x == .25f && y == .25f, "Noncenter pivot");
            Check(BrowserCoordinates.TryNormalize(0, 0, -50, -50, 100, 100, out x, out y)
                && x == .5f && y == .5f, "Center pivot");
            Check(BrowserCoordinates.TryNormalize(200, 0, 0, 0, 100, 100, out x, out y) && x == 2, "Drag clamped");
            Check(!BrowserCoordinates.TryNormalize(0, 0, 0, 0, 0, 100, out x, out y), "Zero width accepted");
        }));
        await Test("nonfinite pointer data cannot poison capture", () => Sync(() => {
            var capture = new PointerCapture();
            Check(!capture.Send(1, PointerPhase.Down, float.NaN, 0), "NaN accepted");
            Check(capture.Send(2, PointerPhase.Down, 0, 0), "Bad input kept capture");
        }));
        await Test("request correlation rejects duplicate and stale responses", async () => {
            using var ledger = new RequestLedger();
            var first = ledger.Begin(out long id, out int generation);
            Check(!ledger.Complete(generation + 1, id, "stale"), "Wrong generation accepted");
            Check(ledger.Complete(generation, id, "ok") && await first == "ok", "Response lost");
            Check(!ledger.Complete(generation, id, "duplicate"), "Duplicate accepted");
        });
        await Test("detach cancels pending requests and next generation works", async () => {
            using var ledger = new RequestLedger(); var old = ledger.Begin(out long id, out int generation);
            ledger.Detach(); await Rejects<OperationCanceledException>(old);
            Check(!ledger.Complete(generation, id, "late"), "Detached response accepted");
            var current = ledger.Begin(out id, out int currentGeneration);
            Check(currentGeneration != generation && ledger.Complete(currentGeneration, id, "new"), "New request failed");
            Check(await current == "new", "New result lost");
        });
        await Test("disposing request ledger cancels pending work and blocks new requests", async () => {
            var ledger = new RequestLedger(); var pending = ledger.Begin(out long id, out int generation);
            ledger.Dispose(); ledger.Dispose(); await Rejects<OperationCanceledException>(pending);
            Throws<ObjectDisposedException>(() => ledger.Begin(out id, out generation));
        });
        Console.WriteLine($"Passed {passed} core tests; no Unity or Android runtime was exercised.");
    }
}
