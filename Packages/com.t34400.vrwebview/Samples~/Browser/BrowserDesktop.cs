using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
namespace T34400.VRWebView.Samples.Browser
{
    public sealed class BrowserWindow
    {
        internal readonly List<long> Order = new List<long>();
        public long Id { get; }
        public ReadOnlyCollection<long> Tabs { get; }
        public long? ActiveTabId { get; internal set; }
        internal BrowserWindow(long id) { Id = id; Tabs = Order.AsReadOnly(); }
    }
    /// <summary>One owner for all page lifetimes. Windows contain references only.
    /// Invoke on the serial owner context. Move publishes only the final topology.</summary>
    public sealed class BrowserDesktop : IDisposable
    {
        private readonly BrowserWorkspace pages;
        private readonly List<BrowserWindow> windows = new List<BrowserWindow>();
        private long nextWindow;
        private int editing;
        private bool disposed;
        public ReadOnlyCollection<BrowserWindow> Windows { get; }
        public ReadOnlyCollection<BrowserTab> Tabs => pages.Tabs;
        public event Action Changed;
        public BrowserDesktop(Func<IBrowserBackend> create, Action<Action> dispatch) {
            pages = new BrowserWorkspace(create, dispatch); Windows = windows.AsReadOnly();
            pages.Changed += PageChanged;
        }
        private void Verify() { if (disposed) throw new ObjectDisposedException(nameof(BrowserDesktop)); }
        private void PageChanged() { if (!disposed && editing == 0) Changed?.Invoke(); }
        public BrowserWindow CreateWindow() { Verify(); var window = new BrowserWindow(checked(++nextWindow)); windows.Add(window); PageChanged(); return window; }
        public BrowserTab FindTab(long id) => pages.Tabs.FirstOrDefault(tab => tab.Id == id);
        public BrowserWindow FindWindow(long id) => windows.FirstOrDefault(window => window.Id == id);
        public BrowserWindow WindowOf(long tab) => windows.FirstOrDefault(window => window.Order.Contains(tab));
        public BrowserTab CreateTab(long windowId) {
            Verify(); var window = FindWindow(windowId) ?? throw new ArgumentException("Unknown window", nameof(windowId));
            editing++;
            try { var tab = pages.CreateTab(false); window.Order.Add(tab.Id); window.ActiveTabId = tab.Id; return tab; }
            finally { editing--; PageChanged(); }
        }
        public bool Select(long windowId, long tabId) {
            Verify(); var window = FindWindow(windowId);
            if (window == null || !window.Order.Contains(tabId)) return false;
            if (window.ActiveTabId != tabId) { window.ActiveTabId = tabId; PageChanged(); }
            return true;
        }
        /// <param name="index">Insertion slot before removal, in 0..target.Tabs.Count.</param>
        public bool MoveTab(long tabId, long targetId, int index) {
            Verify(); var from = WindowOf(tabId); var target = FindWindow(targetId);
            if (from == null || target == null || index < 0 || index > target.Order.Count) return false;
            int old = from.Order.IndexOf(tabId); int slot = index;
            if (from == target && old < slot) slot--;
            from.Order.RemoveAt(old); target.Order.Insert(slot, tabId);
            if (from != target && from.ActiveTabId == tabId)
                from.ActiveTabId = from.Order.Count == 0 ? (long?)null : from.Order[Math.Min(old, from.Order.Count - 1)];
            target.ActiveTabId = tabId;
            if (from != target && from.Order.Count == 0) windows.Remove(from);
            PageChanged(); return true;
        }
        public BrowserWindow DetachTab(long tabId) {
            Verify(); if (WindowOf(tabId) == null) return null;
            editing++;
            try { var window = CreateWindow(); MoveTab(tabId, window.Id, 0); return window; }
            finally { editing--; PageChanged(); }
        }
        public bool CloseTab(long id) {
            Verify(); var window = WindowOf(id); if (window == null) return false;
            editing++;
            try {
                int index = window.Order.IndexOf(id); window.Order.RemoveAt(index);
                if (window.ActiveTabId == id) window.ActiveTabId = window.Order.Count == 0 ? (long?)null : window.Order[Math.Min(index, window.Order.Count - 1)];
                return pages.CloseTab(id);
            } finally { editing--; PageChanged(); }
        }
        public bool CloseWindow(long id) {
            Verify(); var window = FindWindow(id); if (window == null) return false;
            editing++; var errors = new List<Exception>();
            try {
                windows.Remove(window);
                foreach (long tab in window.Order.ToArray()) try { pages.CloseTab(tab); } catch (Exception error) { errors.Add(error); }
                window.Order.Clear(); window.ActiveTabId = null;
            } finally { editing--; PageChanged(); }
            if (errors.Count > 0) throw new AggregateException(errors);
            return true;
        }
        public void Dispose() {
            if (disposed) return; disposed = true; pages.Changed -= PageChanged;
            windows.Clear(); try { pages.Dispose(); } finally { Changed = null; }
        }
    }
}
