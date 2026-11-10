using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using T34400.VRWebView;
using T34400.VRWebView.Samples.Browser;
internal static class BrowserFeatureTests {
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static IEnumerable<(string Name, Action Run)> Scenarios() {
        yield return ("address bar separates web URLs, hostnames, ports and escaped Japanese searches", () => {
            Check(BrowserAddressResolver.Resolve(" example.org/a?q=1 ") == "https://example.org/a?q=1", "Host URL");
            Check(BrowserAddressResolver.Resolve("http://localhost:8080/") == "http://localhost:8080/", "Explicit local URL");
            Check(BrowserAddressResolver.Resolve("localhost:8080/a") == "https://localhost:8080/a", "Local port");
            Check(BrowserAddressResolver.Resolve("日本語 VR #入力").EndsWith(Uri.EscapeDataString("日本語 VR #入力")), "Search encoded once");
            Check(BrowserAddressResolver.Resolve("question?x=1&y=2").Contains("question%3Fx%3D1%26y%3D2"), "Search query injection");
            Throws<ArgumentException>(() => BrowserAddressResolver.Resolve("javascript:alert(1)"));
            Throws<ArgumentException>(() => BrowserAddressResolver.Resolve("file:///private"));
            Throws<ArgumentException>(() => BrowserAddressResolver.Resolve("https://name:pass@example.org"));
            Throws<ArgumentException>(() => BrowserAddressResolver.Resolve(""));
            Throws<ArgumentException>(() => BrowserAddressResolver.Resolve("search words", "http://search.invalid/?q="));
        });
        yield return ("content policy rejects oversized scripts, invalid zoom and invalid find revisions", () => {
            BrowserContentPolicy.ValidateScript("document.title"); BrowserContentPolicy.ValidateTextZoom(50); BrowserContentPolicy.ValidateTextZoom(200); BrowserContentPolicy.ValidateFind("日本語", 1);
            Throws<ArgumentException>(() => BrowserContentPolicy.ValidateScript(null));
            Throws<ArgumentException>(() => BrowserContentPolicy.ValidateScript(new string('x', 1024 * 1024 + 1)));
            Throws<ArgumentOutOfRangeException>(() => BrowserContentPolicy.ValidateTextZoom(201));
            Throws<ArgumentException>(() => BrowserContentPolicy.ValidateFind("query", -1));
        });
        yield return ("history ignores drafts, failed and loading pages and deduplicates per tab", () => {
            var data = new BrowserLibrary();
            Check(!data.Observe(1, BrowserPageState.Empty, 1), "Empty visit");
            Check(!data.Observe(1, new BrowserPageState("https://example.org/", "", false, false, true, 50, 0), 1), "Loading visit");
            Check(!data.Observe(1, new BrowserPageState("https://example.org/", "", false, false, false, 100, -2), 1), "Failed visit");
            var success = new BrowserPageState("https://example.org/", "Example", false, false, false, 100, 0);
            Check(data.Observe(1, success, 20) && !data.Observe(1, success, 21), "Duplicate visit");
            Check(data.Observe(2, success, 22) && data.Data.history.Count == 2, "Different tab visit lost");
            data.ForgetTab(1); Check(data.Observe(1, success, 23), "Closed id not forgotten");
        });
        yield return ("library bounds history, bookmarks and closed tabs and supports remove/reopen", () => {
            var library = new BrowserLibrary();
            for (int i = 0; i < 240; i++) {
                string address = "https://example.org/" + i;
                library.Observe(1, new BrowserPageState(address, "Entry", false, false, false, 100, 0), i);
                library.ToggleBookmark(address, "Entry"); library.RememberClosed(new BrowserSavedPage(address, "Closed", 120));
            }
            Check(library.Data.history.Count == 200 && library.Data.bookmarks.Count == 200 && library.Data.recentlyClosed.Count == 10, "Unbounded data");
            Check(library.TakeClosed().address.EndsWith("/239") && library.TakeClosed().textZoom == 120, "Closed tab stack");
            Check(library.ToggleBookmark("https://example.org/239", "Entry") && !library.IsBookmarked("https://example.org/239"), "Remove bookmark");
            Check(!library.ToggleBookmark("data:invalid", "Invalid"), "Non-web bookmark");
            library.ToggleBookmark("https://EXAMPLE.org:443/canonical", "Canonical");
            Check(library.IsBookmarked("https://example.org/canonical"), "Bookmark address normalization");
        });
        yield return ("session sanitizer keeps valid windows, selected tab, text size and safe placement", () => {
            var saved = new BrowserSavedState();
            saved.windows.Add(new BrowserSavedWindow { active = 2, hasPlacement = true, x = float.NaN,
                tabs = new List<BrowserSavedPage> { new BrowserSavedPage("file:///bad", "Bad"), new BrowserSavedPage("https://example.org/1", "One", 300), new BrowserSavedPage("https://example.org/2", "Two", 10) } });
            saved.windows.Add(new BrowserSavedWindow { hasPlacement = true, x = 300, width = 100000, height = 1 });
            var clean = BrowserLibrary.Sanitize(saved);
            Check(clean.windows[0].tabs.Count == 2 && clean.windows[0].active == 1 && !clean.windows[0].hasPlacement, "Invalid records/active selection");
            Check(clean.windows[0].tabs[0].textZoom == 200 && clean.windows[0].tabs[1].textZoom == 50, "Zoom bounds");
            Check(clean.windows[1].hasPlacement && clean.windows[1].width == 2560 && clean.windows[1].height == 480 && clean.windows[1].tabs.Count == 1, "Placement clamp/blank tab");
            saved.schema = 2; Throws<NotSupportedException>(() => BrowserLibrary.Sanitize(saved));
        });
        yield return ("session data survives atomic replace and recovers read-only from last good backup", () => {
            string directory = Path.Combine(Path.GetTempPath(), "vrwebview-state-check-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "state.json");
            var options = new JsonSerializerOptions { IncludeFields = true };
            BrowserStateStore Store() => new BrowserStateStore(path, text => JsonSerializer.Deserialize<BrowserSavedState>(text, options), state => JsonSerializer.Serialize(state, options));
            try {
                var store = Store(); var state = store.Load(); state.bookmarks.Add(new BrowserSavedPage("https://example.org/", "Example"));
                Check(store.Save(state), "Initial save"); state.history.Add(new BrowserSavedPage("https://example.org/new", "New")); Check(store.Save(state), "Replace");
                Check(Store().Load().history.Count == 1 && File.Exists(path + ".bak"), "Session roundtrip");
                var oversized = new BrowserStateStore(path, _ => new BrowserSavedState(), _ => new string('x', 2 * 1024 * 1024 + 1));
                Check(!oversized.Save(state) && !string.IsNullOrEmpty(oversized.Warning) && Store().Load().history.Count == 1, "Storage limit corrupted existing data or escaped into UI");
                File.WriteAllText(path, "{\"schema\":99}"); var future = Store(); var recovered = future.Load();
                Check(future.IsReadOnly && recovered.bookmarks.Count == 1 && recovered.history.Count == 0, "Last-good backup");
                Check(!future.Save(state) && File.ReadAllText(path).Contains("99"), "Unknown schema overwritten");
                future.Forget(); Check(!future.IsReadOnly && future.Save(new BrowserSavedState()), "Explicit recovery");
                Check(!File.Exists(path + ".tmp"), "Temporary file leaked");
            } finally { Directory.Delete(directory, true); }
        });
        yield return ("session serializer cannot persist non-web addresses and limits restored topology", () => {
            var state = new BrowserSavedState();
            for (int i = 0; i < 12; i++) { var w = new BrowserSavedWindow(); for (int j = 0; j < 40; j++) w.tabs.Add(new BrowserSavedPage("https://example.org/" + j, "Title")); state.windows.Add(w); }
            state.bookmarks.Add(new BrowserSavedPage("https://user:pass@example.org/", "Bad"));
            var clean = BrowserLibrary.Sanitize(state);
            Check(clean.windows.Count == 8 && clean.windows[0].tabs.Count == 32 && clean.bookmarks.Count == 0, "Topology or URL bounds");
        });
        yield return ("optional content session rejects early access and dispatches events on its owner", () => {
            var backend = new ContentBackend(); var queue = new Queue<Action>(); var session = new BrowserSession(backend, queue.Enqueue);
            var early = session.EvaluateJavaScriptAsync("1"); Check(early.IsFaulted, "Script accepted before initialization");
            var init = session.InitializeAsync(); while (queue.Count > 0) queue.Dequeue()(); init.GetAwaiter().GetResult();
            int notifications = 0; session.FindResultChanged += _ => notifications++;
            backend.Emit(); Check(notifications == 0, "Find callback ran outside owner"); while (queue.Count > 0) queue.Dequeue()(); Check(notifications == 1, "Find callback lost");
            Check(session.EvaluateJavaScriptAsync("1+1").GetAwaiter().GetResult() == "2", "JSON result changed");
            backend.Emit(); session.Dispose(); while (queue.Count > 0) queue.Dequeue()(); Check(notifications == 1, "Late find callback after dispose");
        });
    }
    private sealed class ContentBackend : IBrowserBackend, IBrowserContentBackend {
        public event Action<BrowserFindResult> FindResultChanged;
        public event Action<BrowserContextTarget> ContextRequested { add { } remove { } }
        public void Emit() => FindResultChanged?.Invoke(new BrowserFindResult(1, 1, 2, true));
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task NavigateAsync(Uri uri, CancellationToken token) => Task.CompletedTask;
        public void Dispose() { }
        public Task<string> EvaluateJavaScriptAsync(string script, CancellationToken token = default) => Task.FromResult("2");
        public Task SetTextZoomAsync(int percent, CancellationToken token = default) => Task.CompletedTask;
        public Task FindAsync(string query, long revision, CancellationToken token = default) => Task.CompletedTask;
        public Task FindNextAsync(bool forward, CancellationToken token = default) => Task.CompletedTask;
        public Task ClearFindAsync(CancellationToken token = default) => Task.CompletedTask;
    }
}
