using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;
using T34400.VRWebView.Samples.Browser;
internal static class BrowserDesktopTests
{
    private sealed class Backend : IBrowserBackend {
        internal int Closed;
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task NavigateAsync(Uri uri, CancellationToken token) => Task.CompletedTask;
        public void Dispose() { Closed++; }
    }
    private static void Check(bool ok) { if (!ok) throw new Exception("Desktop ownership invariant failed"); }
    public static IEnumerable<(string Name, Action Run)> Scenarios() {
        yield return ("corner resize fixes the opposite corner for all four signs", () => {
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) {
                var size = BrowserWindowGeometry.Resize(1280, 1000, 100 * x, 80 * y, x, y);
                Check(size.Width == 1380 && size.Height == 1080);
                Check(size.OffsetX - x * size.Width * .5f == -x * 640);
                Check(size.OffsetY - y * size.Height * .5f == -y * 500);
            }
        });
        yield return ("resize clamps dimensions without moving the opposite corner", () => {
            var size = BrowserWindowGeometry.Resize(1280, 1000, -10000, 10000, 1, -1);
            Check(size.Width == 800 && size.Height == 600);
            Check(size.OffsetX - size.Width * .5f == -640 && size.OffsetY + size.Height * .5f == 500);
            bool invalid = false; try { BrowserWindowGeometry.Resize(1280, 1000, float.NaN, 1, 1, 1); } catch (ArgumentOutOfRangeException) { invalid = true; }
            Check(invalid);
        });
        yield return ("tab reorder preserves pages and commits final membership", () => {
            var backends = new List<Backend>(); using var app = new BrowserDesktop(() => { var b = new Backend(); backends.Add(b); return b; }, action => action());
            var a = app.CreateWindow(); var x = app.CreateTab(a.Id); var y = app.CreateTab(a.Id); var z = app.CreateTab(a.Id);
            int notifications = 0; app.Changed += () => { notifications++; Check(app.Windows.SelectMany(w => w.Tabs).Distinct().Count() == app.Tabs.Count); };
            Check(app.MoveTab(x.Id, a.Id, 3)); Check(a.Tabs.SequenceEqual(new[] { y.Id, z.Id, x.Id }));
            Check(notifications == 1 && backends.All(b => b.Closed == 0));
        });
        yield return ("detach and dock preserve the exact session without reinitialization", () => {
            var backends = new List<Backend>(); using var app = new BrowserDesktop(() => { var b = new Backend(); backends.Add(b); return b; }, action => action());
            var a = app.CreateWindow(); var x = app.CreateTab(a.Id); var y = app.CreateTab(a.Id);
            var detached = app.DetachTab(x.Id); Check(detached.Tabs.Single() == x.Id && ReferenceEquals(app.FindTab(x.Id), x));
            Check(app.MoveTab(x.Id, a.Id, 1)); Check(app.FindWindow(detached.Id) == null && a.Tabs.SequenceEqual(new[] { y.Id, x.Id }));
            Check(backends.Count == 2 && backends.All(b => b.Closed == 0));
        });
        yield return ("invalid drag destination leaves every tab in its original window", () => {
            using var app = new BrowserDesktop(() => new Backend(), action => action()); var a = app.CreateWindow(); var x = app.CreateTab(a.Id);
            Check(!app.MoveTab(x.Id, 999, 0)); Check(!app.MoveTab(x.Id, a.Id, -1)); Check(!app.MoveTab(x.Id, a.Id, 2));
            Check(a.Tabs.Single() == x.Id && a.ActiveTabId == x.Id);
        });
        yield return ("closing a window does not dispose pages moved to another window", () => {
            var backends = new List<Backend>(); using var app = new BrowserDesktop(() => { var b = new Backend(); backends.Add(b); return b; }, action => action());
            var a = app.CreateWindow(); var x = app.CreateTab(a.Id); app.CreateTab(a.Id); var b = app.CreateWindow();
            app.MoveTab(x.Id, b.Id, 0); app.CloseWindow(a.Id);
            Check(backends[0].Closed == 0 && backends[1].Closed == 1 && ReferenceEquals(app.FindTab(x.Id), x));
            app.Dispose(); Check(backends.All(backend => backend.Closed == 1));
        });
        yield return ("moving an active tab picks a surviving source and independent target selection", () => {
            using var app = new BrowserDesktop(() => new Backend(), action => action()); var a = app.CreateWindow(); var x = app.CreateTab(a.Id); var y = app.CreateTab(a.Id);
            var b = app.CreateWindow(); app.CreateTab(b.Id); app.MoveTab(y.Id, b.Id, 0);
            Check(a.ActiveTabId == x.Id && b.ActiveTabId == y.Id && b.Tabs[0] == y.Id);
        });
    }
}
