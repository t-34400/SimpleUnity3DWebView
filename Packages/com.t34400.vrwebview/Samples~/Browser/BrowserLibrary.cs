using System;
using System.Collections.Generic;
using System.Linq;
namespace T34400.VRWebView.Samples.Browser
{
    [Serializable] public sealed class BrowserSavedPage {
        public string address = "", title = "";
        public long visitedUtcTicks;
        public int textZoom = 100;
        public BrowserSavedPage() { }
        public BrowserSavedPage(string url, string name, int zoom = 100) { address = url; title = name ?? ""; textZoom = zoom; }
    }
    [Serializable] public sealed class BrowserSavedWindow {
        public List<BrowserSavedPage> tabs = new List<BrowserSavedPage>();
        public int active;
        public bool hasPlacement;
        public float x, y, z, qx, qy, qz, qw = 1, width = 1280, height = 1000;
    }
    [Serializable] public sealed class BrowserSavedState {
        public int schema = 1;
        public int poseVersion;
        public float fixedTiltDegrees = 5;
        public List<BrowserSavedPage> bookmarks = new List<BrowserSavedPage>();
        public List<BrowserSavedPage> history = new List<BrowserSavedPage>();
        public List<BrowserSavedPage> recentlyClosed = new List<BrowserSavedPage>();
        public List<BrowserSavedWindow> windows = new List<BrowserSavedWindow>();
    }
    /// <summary>Bounded, local sample data. No cookies, credentials, drafts or form contents.</summary>
    public sealed class BrowserLibrary {
        public BrowserSavedState Data { get; }
        private readonly Dictionary<long, string> lastVisit = new Dictionary<long, string>();
        public BrowserLibrary(BrowserSavedState state = null) { Data = Sanitize(state ?? new BrowserSavedState()); }
        public static bool IsWebAddress(string address) { try { return !string.IsNullOrEmpty(address) && address.Length <= 8192 && NavigationPolicy.Parse(address) != null; } catch (ArgumentException) { return false; } }
        private static BrowserSavedPage Copy(BrowserSavedPage page) {
            if (page == null || !IsWebAddress(page.address)) return null;
            return new BrowserSavedPage(NavigationPolicy.Parse(page.address).AbsoluteUri, (page.title ?? "").Length > 256 ? page.title.Substring(0, 256) : page.title,
                Math.Max(50, Math.Min(200, page.textZoom))) { visitedUtcTicks = Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, page.visitedUtcTicks)) };
        }
        private static List<BrowserSavedPage> Clean(IEnumerable<BrowserSavedPage> source, int limit) => (source ?? Enumerable.Empty<BrowserSavedPage>()).Take(limit).Select(Copy).Where(p => p != null).ToList();
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static BrowserSavedState Sanitize(BrowserSavedState source) {
            if (source == null || source.schema != 1) throw new NotSupportedException("Unknown browser data schema");
            var clean = new BrowserSavedState { bookmarks = Clean(source.bookmarks, 200), history = Clean(source.history, 200), recentlyClosed = Clean(source.recentlyClosed, 10),
                poseVersion = source.poseVersion == 1 ? 1 : 0, fixedTiltDegrees = source.poseVersion == 1 && Finite(source.fixedTiltDegrees) ? Math.Max(0, Math.Min(10, source.fixedTiltDegrees)) : 5 };
            foreach (var window in (source.windows ?? new List<BrowserSavedWindow>()).Take(8)) {
                if (window == null) continue;
                var saved = new BrowserSavedWindow(); int active = 0, originalIndex = -1;
                foreach (var tab in (window.tabs ?? new List<BrowserSavedPage>()).Take(32)) {
                    originalIndex++;
                    // Empty new tabs have no address; invalid nonempty URLs are discarded.
                    if (tab == null) continue;
                    var page = string.IsNullOrEmpty(tab.address) ? new BrowserSavedPage() : Copy(tab);
                    if (page == null) continue;
                    if (originalIndex <= window.active) active = saved.tabs.Count;
                    saved.tabs.Add(page);
                }
                if (saved.tabs.Count == 0) saved.tabs.Add(new BrowserSavedPage());
                saved.active = Math.Max(0, Math.Min(saved.tabs.Count - 1, active));
                bool finite = new[] { window.x, window.y, window.z, window.qx, window.qy, window.qz, window.qw, window.width, window.height }.All(Finite);
                float norm = window.qx * window.qx + window.qy * window.qy + window.qz * window.qz + window.qw * window.qw;
                saved.hasPlacement = window.hasPlacement && finite && norm > .1f && norm < 4 && Math.Abs(window.x) < 100000 && Math.Abs(window.y) < 100000 && Math.Abs(window.z) < 100000;
                if (saved.hasPlacement) { saved.x = window.x; saved.y = window.y; saved.z = window.z; saved.qx = window.qx; saved.qy = window.qy; saved.qz = window.qz; saved.qw = window.qw; saved.width = Math.Max(640, Math.Min(2560, window.width)); saved.height = Math.Max(480, Math.Min(2560, window.height)); }
                clean.windows.Add(saved);
            }
            return clean;
        }
        public bool Observe(long tab, BrowserPageState state, long utcTicks) {
            if (state == null || state.IsLoading || state.ErrorCode != 0 || !IsWebAddress(state.Address)) return false;
            string address = NavigationPolicy.Parse(state.Address).AbsoluteUri;
            if (lastVisit.TryGetValue(tab, out var previous) && previous == address) return false;
            lastVisit[tab] = address;
            Data.history.Insert(0, new BrowserSavedPage(address, state.Title) { visitedUtcTicks = utcTicks });
            if (Data.history.Count > 200) Data.history.RemoveRange(200, Data.history.Count - 200);
            return true;
        }
        public void ForgetTab(long tab) { lastVisit.Remove(tab); }
        public bool IsBookmarked(string address) => IsWebAddress(address) && Data.bookmarks.Any(p => p.address == NavigationPolicy.Parse(address).AbsoluteUri);
        public BrowserSavedPage FindBookmark(string address) => IsWebAddress(address) ? Data.bookmarks.FirstOrDefault(p => p.address == NavigationPolicy.Parse(address).AbsoluteUri) : null;
        public bool SaveBookmark(string address, string title) {
            if (!IsWebAddress(address)) return false;
            address = NavigationPolicy.Parse(address).AbsoluteUri;
            string label = (title ?? "").Trim(); if (label.Length > 256) label = label.Substring(0, 256);
            var saved = FindBookmark(address);
            if (saved == null) { Data.bookmarks.Insert(0, new BrowserSavedPage(address, label)); if (Data.bookmarks.Count > 200) Data.bookmarks.RemoveAt(200); }
            else saved.title = label;
            return true;
        }
        public bool RemoveBookmark(string address) {
            var saved = FindBookmark(address); return saved != null && Data.bookmarks.Remove(saved);
        }
        public bool ToggleBookmark(string address, string title) {
            if (!IsWebAddress(address)) return false;
            address = NavigationPolicy.Parse(address).AbsoluteUri;
            int existing = Data.bookmarks.FindIndex(p => p.address == address);
            if (existing >= 0) Data.bookmarks.RemoveAt(existing);
            else { Data.bookmarks.Insert(0, new BrowserSavedPage(address, title)); if (Data.bookmarks.Count > 200) Data.bookmarks.RemoveAt(200); }
            return true;
        }
        public void RememberClosed(BrowserSavedPage page) { var saved = Copy(page); if (saved == null) return; Data.recentlyClosed.Insert(0, saved); if (Data.recentlyClosed.Count > 10) Data.recentlyClosed.RemoveAt(10); }
        public BrowserSavedPage TakeClosed() { if (Data.recentlyClosed.Count == 0) return null; var page = Data.recentlyClosed[0]; Data.recentlyClosed.RemoveAt(0); return page; }
    }
}
