using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Optional rich sample. Owns pages independently of all window canvases.</summary>
    public sealed class BrowserDesktopPanel : MonoBehaviour
    {
        [SerializeField] private BrowserPageFactory factory;
        public Camera worldCamera;
        public bool diagnostics = true;
        private BrowserDispatcher owner;
        private Action<Action> schedule;
        private BrowserDesktop desktop;
        private BrowserPage pending;
        private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
        private readonly Dictionary<long, BrowserWindowView> views = new Dictionary<long, BrowserWindowView>();
        private bool changing, closed;
        private float nextSnapshot;
        private long draggingTab = -1, candidateWindow = -1;
        private int insertion;
        private Text ghost;
        private BrowserWindowView sourceView;
        private Vector3 dropWorld;
        public BrowserDesktop Model => desktop;
        public IEnumerable<BrowserWindowView> WindowViews => views.Values;
        internal sealed class Entry {
            internal BrowserPage Page;
            internal BrowserAddressDraft Draft = new BrowserAddressDraft();
            internal string LastAddress = "";
            internal bool Dirty;
            internal Entry(BrowserPage page) { Page = page; }
        }
        public void SetFactory(BrowserPageFactory value) { if (desktop != null) throw new InvalidOperationException("Set factory before starting"); factory = value; }
        private void Start() {
            if (factory == null) { Debug.LogError("Browser sample requires a page factory", this); return; }
            owner = GetComponent<BrowserDispatcher>() ?? gameObject.AddComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread(); schedule = owner.Scheduler;
            desktop = new BrowserDesktop(() => { pending = factory.CreatePage(owner); return pending.Backend; }, owner.Scheduler);
            desktop.Changed += Refresh;
            CreateWindow();
            if (diagnostics) Debug.Log("[VRWebViewSample] BS1 event=ui_ready version=41 multiWindow=true");
        }
        public void CreateWindow() {
            if (closed || desktop == null) return;
            changing = true;
            try { var window = desktop.CreateWindow(); NewTab(window.Id); }
            finally { changing = false; Refresh(); }
        }
        public void NewTab(long window) {
            if (closed) return;
            bool wasChanging = changing; changing = true;
            try {
                var tab = desktop.CreateTab(window); entries.Add(tab.Id, new Entry(pending)); pending = null;
                schedule(() => { if (!closed && !tab.IsClosed) _ = Observe(tab.InitializeAsync(), window); });
            } catch (Exception error) { pending?.Dispose(); pending = null; Failure(window, error); }
            finally { changing = wasChanging; if (!changing) Refresh(); }
        }
        public void Select(long window, long tab) { if (!closed) desktop.Select(window, tab); }
        public void CloseTab(long tab) {
            if (closed) return; var previous = desktop.WindowOf(tab); if (previous == null) return;
            entries.TryGetValue(tab, out var entry); entry?.Page.Editing?.EndEditing();
            changing = true;
            try { desktop.CloseTab(tab); }
            catch (Exception error) { Failure(previous.Id, error); }
            finally { entries.Remove(tab); changing = false; Refresh(); }
        }
        public void CloseWindow(long window) {
            var model = desktop?.FindWindow(window); if (closed || model == null) return;
            var ids = model.Tabs.ToArray(); changing = true;
            try { desktop.CloseWindow(window); }
            catch (Exception error) { Failure(window, error); }
            finally {
                foreach (long id in ids) entries.Remove(id);
                changing = false;
                // Keep a usable blank browser when the last window closes.
                if (desktop.Windows.Count == 0) CreateWindow(); else Refresh();
            }
        }
        internal Entry GetEntry(long tab) { entries.TryGetValue(tab, out var entry); return entry; }
        public Task PrepareAddress(long window) {
            var active = desktop?.FindWindow(window)?.ActiveTabId;
            return active.HasValue && entries.TryGetValue(active.Value, out var entry) ? entry.Page.EndEditingAsync() : Task.CompletedTask;
        }
        public void EditAddress(long window, string text) {
            var id = desktop.FindWindow(window)?.ActiveTabId; if (!id.HasValue || !entries.TryGetValue(id.Value, out var entry)) return;
            entry.Draft.Edit(text); entry.Dirty = true;
        }
        public void Navigate(long window) {
            var id = desktop.FindWindow(window)?.ActiveTabId; if (!id.HasValue) return;
            var tab = desktop.FindTab(id.Value); var entry = GetEntry(id.Value); if (tab == null || entry == null) return;
            string address = entry.Draft.Text.Trim();
            if (!address.Contains("://")) address = "https://" + address;
            try {
                // Keep policy validation in Core. Normalization does not permit arbitrary schemes.
                address = NavigationPolicy.Parse(address).AbsoluteUri; entry.Draft.Edit(address); entry.Dirty = false;
                _ = Observe(tab.NavigateAsync(address), window);
            } catch (Exception error) { Failure(window, error); }
        }
        public void Command(long window, BrowserPageCommand command) {
            var id = desktop.FindWindow(window)?.ActiveTabId;
            try { if (id.HasValue) {
                var tab = desktop.FindTab(id.Value); var entry = GetEntry(id.Value);
                entry.Dirty = false;
                if (!string.IsNullOrEmpty(tab.PageState.Address) && tab.PageState.Address != "about:blank") entry.Draft.Edit(tab.PageState.Address);
                _ = Observe(tab.ExecuteAsync(command), window);
            } }
            catch (Exception error) { Failure(window, error); }
        }
        private async Task Observe(Task task, long window) {
            try { await task.ConfigureAwait(false); schedule(Refresh); }
            catch (Exception error) { try { schedule(() => Failure(window, error)); } catch (ObjectDisposedException) { } }
        }
        private void Failure(long window, Exception error) {
            if (closed) return;
            if (views.TryGetValue(window, out var view)) view.ShowMessage(error is ArgumentException ? "Enter an HTTP(S) address." : "Page operation failed. Retry or reload.");
            Debug.LogWarning("[VRWebViewSample] BS1 event=page_operation_failed type=" + error.GetType().Name);
        }
        internal void RequestRefresh() { if (!closed) schedule(Refresh); }
        private void Refresh() {
            if (closed || changing || desktop == null) return;
            foreach (var pair in views.ToArray()) if (desktop.FindWindow(pair.Key) == null) {
                pair.Value.Unbind(); BrowserSampleWidgets.Remove(pair.Value.gameObject); views.Remove(pair.Key);
            }
            // Withdraw old view/input bindings first; then relinquish all hidden producers.
            foreach (var pair in views) pair.Value.WithdrawIfChanged(desktop.FindWindow(pair.Key).ActiveTabId);
            var visible = new HashSet<long>(desktop.Windows.Where(w => w.ActiveTabId.HasValue).Select(w => w.ActiveTabId.Value));
            foreach (var pair in entries) if (!visible.Contains(pair.Key)) pair.Value.Page.SetPresentation(false, 0, 0);
            foreach (var window in desktop.Windows) {
                if (!views.TryGetValue(window.Id, out var view)) {
                    var node = new GameObject("Browser window " + window.Id, typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(BrowserWindowView));
                    node.transform.SetParent(transform, false);
                    var canvas = node.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = worldCamera; canvas.overrideSorting = true;
                    view = node.GetComponent<BrowserWindowView>(); views.Add(window.Id, view);
                    view.Build(this, window.Id, owner);
                    node.GetComponent<RectTransform>().anchoredPosition = new Vector2((views.Count - 1) * 1350, 0);
                }
                view.Render(window);
            }
        }
        private void Update() {
            if (desktop == null || closed) return;
            foreach (var view in views.Values) view.UpdatePresentation();
            if (!diagnostics || Time.unscaledTime < nextSnapshot) return;
            nextSnapshot = Time.unscaledTime + 1;
            Debug.Log("[VRWebViewSample] BS1 event=snapshot configured=True windows=" + views.Count + " tabs=" + desktop.Tabs.Count
                + " addressFocus=" + views.Values.Any(v => v.AddressFocused) + " frames=" + Time.frameCount + " appFocus=" + Application.isFocused);
        }
        public void BeginTabDrag(long tab, BrowserWindowView from, PointerEventData data) {
            CancelTabDrag(); draggingTab = tab; sourceView = from;
            ghost = BrowserSampleWidgets.Label(transform, "Moving tab", "Moving tab", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), 24);
            ghost.gameObject.AddComponent<Canvas>().overrideSorting = true; ghost.GetComponent<Canvas>().sortingOrder = 1000;
            ghost.rectTransform.sizeDelta = new Vector2(500, 70); DragTab(data);
        }
        public void DragTab(PointerEventData data) {
            if (draggingTab < 0) return;
            var root = (RectTransform)transform;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(root, data.position, worldCamera, out Vector3 world)) {
                dropWorld = world; ghost.transform.position = world;
            }
            BrowserWindowView candidate = null;
            // Prefer the actual ray-hit window so a farther overlapping canvas cannot steal a drop.
            var hit = data.pointerCurrentRaycast.gameObject?.GetComponentInParent<BrowserWindowView>();
            if (hit != null && hit.IsNearTabs(data.position, worldCamera, hit.WindowId == candidateWindow ? 50 : 25)) candidate = hit;
            if (candidate == null && sourceView != null && sourceView.IsNearTabs(data.position, worldCamera, 25)) candidate = sourceView;
            candidateWindow = candidate != null ? candidate.WindowId : -1;
            candidate?.AutoScrollTabs(data.position, worldCamera);
            insertion = candidate != null ? candidate.InsertionAt(data.position, worldCamera) : 0;
            ghost.text = candidate == null ? "Release to create a window" : "Release to insert tab at " + (insertion + 1);
            foreach (var view in views.Values) view.SetDropPreview(view == candidate, insertion);
        }
        public void EndTabDrag() {
            if (draggingTab < 0) return;
            long tab = draggingTab, target = candidateWindow; int index = insertion; Vector3 position = dropWorld;
            CancelTabDrag();
            // Cancel before membership changes can destroy the original drag component.
            if (target >= 0) desktop.MoveTab(tab, target, index);
            else {
                var window = desktop.DetachTab(tab); if (window != null && views.TryGetValue(window.Id, out var view)) view.transform.position = position;
            }
        }
        public void CancelTabDrag() {
            draggingTab = -1; candidateWindow = -1; sourceView = null;
            if (ghost != null) BrowserSampleWidgets.Remove(ghost.gameObject); ghost = null;
            foreach (var view in views.Values) view.SetDropPreview(false, 0);
        }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelTabDrag(); }
        private void OnDisable() {
            CancelTabDrag(); foreach (var view in views.Values) view.Unbind();
            foreach (var entry in entries.Values) { entry.Page.Editing?.EndEditing(); entry.Page.SetPresentation(false, 0, 0); }
        }
        private void OnEnable() { if (desktop != null) Refresh(); }
        private void OnDestroy() {
            if (closed) return; closed = true; CancelTabDrag();
            foreach (var view in views.Values) if (view != null) view.Unbind();
            if (desktop != null) { desktop.Changed -= Refresh; desktop.Dispose(); }
            entries.Clear(); views.Clear();
        }
    }
}
