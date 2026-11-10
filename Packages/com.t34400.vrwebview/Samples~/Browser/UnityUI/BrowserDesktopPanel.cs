using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
        public BrowserWindowPlacementMode placementMode = BrowserWindowPlacementMode.FaceViewer;
        [Range(64, 160)] public float cornerHitSize = 96;
        public bool diagnostics = true;
        public bool restorePreviousSession = true;
        public string searchEndpoint = "https://duckduckgo.com/?q=";
        private BrowserLibrary library;
        private BrowserStateStore stateStore;
        private float saveAfter;
        private bool savePending;
        private bool persistSession = true;
        public BrowserLibrary Library => library;
        internal string StorageWarning => stateStore?.Warning;
        private BrowserDispatcher owner;
        private Action<Action> schedule;
        private BrowserDesktop desktop;
        private BrowserPage pending;
        private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
        private readonly Dictionary<long, BrowserWindowView> views = new Dictionary<long, BrowserWindowView>();
        private bool changing, closed;
        private float nextSnapshot;
        private long draggingTab = -1, candidateWindow = -1;
        private int insertion, sortingOrder;
        private float tabGrabDistance;
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
            internal int TextZoom = 100;
            internal BrowserSavedPage RestorePage;
            internal bool InitializationScheduled;
            internal Action<BrowserFindResult> FindHandler;
            internal Action<BrowserContextTarget> ContextHandler;
            internal Entry(BrowserPage page) { Page = page; }
        }
        public void SetStorage(BrowserStateStore value) { if (desktop != null) throw new InvalidOperationException("Set storage before starting"); stateStore = value ?? throw new ArgumentNullException(nameof(value)); }
        public void SetFactory(BrowserPageFactory value) { if (desktop != null) throw new InvalidOperationException("Set factory before starting"); factory = value; }
        private void Start() {
            if (factory == null) { Debug.LogError("Browser sample requires a page factory", this); return; }
            owner = GetComponent<BrowserDispatcher>() ?? gameObject.AddComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread(); schedule = owner.Scheduler;
            desktop = new BrowserDesktop(() => { pending = factory.CreatePage(owner); return pending.Backend; }, owner.Scheduler);
            desktop.Changed += Refresh;
            if (stateStore == null) stateStore = new BrowserStateStore(Path.Combine(Application.persistentDataPath, "vrwebview-browser-v1.json"),
                text => JsonUtility.FromJson<BrowserSavedState>(text), state => JsonUtility.ToJson(state));
            library = new BrowserLibrary(stateStore.Load());
            if (!restorePreviousSession || !RestoreWindows()) CreateWindow();
            if (diagnostics) Debug.Log("[VRWebViewSample] BS1 event=ui_ready version=50 multiWindow=true features=library_session_find_context");
        }
        public void CreateWindow() {
            if (closed || desktop == null) return;
            changing = true;
            try { if (desktop.Windows.Count >= 8) return; var window = desktop.CreateWindow(); NewTab(window.Id); }
            finally { changing = false; Refresh(); }
        }
        public void NewTab(long window) { CreatePage(window, null); }
        private BrowserTab CreatePage(long window, BrowserSavedPage saved) {
            if (closed || desktop.FindWindow(window) == null || desktop.FindWindow(window).Tabs.Count >= 32) return null;
            BrowserTab result = null;
            bool wasChanging = changing; changing = true;
            try {
                var tab = desktop.CreateTab(window); result = tab; var entry = new Entry(pending); entries.Add(tab.Id, entry); pending = null;
                entry.TextZoom = saved?.textZoom ?? 100; entry.RestorePage = saved;
                if (saved != null) { entry.LastAddress = saved.address; entry.Draft.Edit(saved.address); }
                if (entry.Page.Content != null) {
                    entry.FindHandler = value => { var host = desktop.WindowOf(tab.Id); if (host != null && views.TryGetValue(host.Id, out var view)) view.FindUpdated(tab.Id, value); };
                    entry.ContextHandler = value => { var host = desktop.WindowOf(tab.Id); if (host?.ActiveTabId == tab.Id && views.TryGetValue(host.Id, out var view)) view.ShowContext(value); };
                    entry.Page.Content.FindResultChanged += entry.FindHandler; entry.Page.Content.ContextRequested += entry.ContextHandler;
                }
            } catch (Exception error) { pending?.Dispose(); pending = null; Failure(window, error); }
            finally { changing = wasChanging; if (!changing) Refresh(); }
            return result;
        }
        public void Select(long window, long tab) {
            if (closed) return;
            if (views.TryGetValue(window, out var view)) BringToFront(view);
            desktop.Select(window, tab);
        }
        internal void BringToFront(BrowserWindowView view) { view.GetComponent<Canvas>().sortingOrder = ++sortingOrder; }
        public void CloseTab(long tab) {
            if (closed) return; var previous = desktop.WindowOf(tab); if (previous == null) return;
            entries.TryGetValue(tab, out var entry); RememberClosed(tab); Unsubscribe(entry); entry?.Page.Editing?.EndEditing();
            changing = true;
            try { desktop.CloseTab(tab); }
            catch (Exception error) { Failure(previous.Id, error); }
            finally { entries.Remove(tab); changing = false; Refresh(); }
        }
        public void CloseWindow(long window) {
            var model = desktop?.FindWindow(window); if (closed || model == null) return;
            var ids = model.Tabs.ToArray(); foreach (long id in ids) { RememberClosed(id); Unsubscribe(GetEntry(id)); } changing = true;
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
            if (views.TryGetValue(window, out var view)) BringToFront(view);
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
            try {
                // Keep policy validation in Core. Normalization does not permit arbitrary schemes.
                address = BrowserAddressResolver.Resolve(address, searchEndpoint); entry.Draft.Edit(address); entry.Dirty = false;
                if (views.TryGetValue(window, out var view)) view.NavigationStarted();
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
            if (views.TryGetValue(window, out var view)) view.ShowMessage(error is ArgumentException ? "Enter a web address or search terms." : "Page operation failed. Retry or reload.");
            Debug.LogWarning("[VRWebViewSample] BS1 event=page_operation_failed type=" + error.GetType().Name);
        }
        internal void RequestRefresh() { if (!closed) schedule(Refresh); }
        private void Refresh() {
            if (closed || changing || desktop == null) return;
            foreach (var tab in desktop.Tabs) library.Observe(tab.Id, tab.PageState, DateTime.UtcNow.Ticks);
            MarkSave();
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
                    view.Build(this, window.Id, owner); BringToFront(view);
                    node.GetComponent<RectTransform>().anchoredPosition = new Vector2((views.Count - 1) * 1350, 0);
                }
                view.Render(window);
                var active = window.ActiveTabId.HasValue ? desktop.FindTab(window.ActiveTabId.Value) : null;
                var entry = active != null ? GetEntry(active.Id) : null;
                if (active != null && active.State == BrowserState.Created && entry != null && !entry.InitializationScheduled) {
                    entry.InitializationScheduled = true;
                    schedule(() => {
                        if (closed || active.IsClosed) return;
                        if (desktop.WindowOf(active.Id)?.ActiveTabId != active.Id) { entry.InitializationScheduled = false; return; }
                        _ = InitializePage(active, entry, entry.RestorePage, desktop.WindowOf(active.Id).Id);
                    });
                }
            }
        }
        private void Update() {
            if (desktop == null || closed) return;
            foreach (var view in views.Values) view.UpdatePresentation();
            if (savePending && Time.unscaledTime >= saveAfter) SaveState();
            if (!diagnostics || Time.unscaledTime < nextSnapshot) return;
            nextSnapshot = Time.unscaledTime + 1;
            Debug.Log("[VRWebViewSample] BS1 event=snapshot configured=True windows=" + views.Count + " tabs=" + desktop.Tabs.Count
                + " addressFocus=" + views.Values.Any(v => v.AddressFocused) + " frames=" + Time.frameCount + " appFocus=" + Application.isFocused);
        }
        public void BeginTabDrag(long tab, BrowserWindowView from, PointerEventData data) {
            CancelTabDrag(); draggingTab = tab; sourceView = from;
            if (data is BrowserRayPointerEventData ray && ray.HasWorldRay) {
                var plane = new Plane(from.transform.forward, from.transform.position);
                tabGrabDistance = plane.Raycast(ray.WorldRay, out float distance) ? Mathf.Max(.1f, distance) : 1.6f;
            }
            ghost = BrowserSampleWidgets.Label(transform, "Moving tab", "Moving tab", Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"), 24);
            ghost.gameObject.AddComponent<Canvas>().overrideSorting = true; ghost.GetComponent<Canvas>().sortingOrder = 1000;
            ghost.rectTransform.sizeDelta = new Vector2(500, 70); DragTab(data);
        }
        public void DragTab(PointerEventData data) {
            if (draggingTab < 0) return;
            if (data is BrowserRayPointerEventData ray && ray.HasWorldRay) {
                dropWorld = ray.WorldRay.GetPoint(tabGrabDistance); ghost.transform.position = dropWorld;
            } else if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)transform, data.position, worldCamera, out Vector3 world)) {
                dropWorld = world; ghost.transform.position = world;
            }
            BrowserWindowView candidate = null;
            // Prefer the actual ray-hit window so a farther overlapping canvas cannot steal a drop.
            var hit = data.pointerCurrentRaycast.gameObject?.GetComponentInParent<BrowserWindowView>();
            if (hit != null && hit.IsNearTabs(data, hit.WindowId == candidateWindow ? 50 : 25)) candidate = hit;
            if (candidate == null && sourceView != null && sourceView.IsNearTabs(data, 25)) candidate = sourceView;
            candidateWindow = candidate != null ? candidate.WindowId : -1;
            candidate?.AutoScrollTabs(data);
            insertion = candidate != null ? candidate.InsertionAt(data) : 0;
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
        private async Task InitializePage(BrowserTab tab, Entry entry, BrowserSavedPage saved, long window) {
            try {
                await tab.InitializeAsync().ConfigureAwait(false);
                schedule(() => { if (closed || tab.IsClosed) return;
                    if (entry.Page.Content != null && entry.TextZoom != 100) _ = Observe(entry.Page.Content.SetTextZoomAsync(entry.TextZoom), window);
                    if (saved != null && BrowserLibrary.IsWebAddress(saved.address)) _ = Observe(tab.NavigateAsync(saved.address), window);
                    entry.RestorePage = null;
                });
            } catch (Exception error) { try { schedule(() => Failure(window, error)); } catch (ObjectDisposedException) { } }
        }
        private static void Unsubscribe(Entry entry) {
            if (entry?.Page.Content == null) return;
            entry.Page.Content.FindResultChanged -= entry.FindHandler; entry.Page.Content.ContextRequested -= entry.ContextHandler;
        }
        private BrowserSavedPage SavePage(long id) {
            var tab = desktop.FindTab(id); var entry = GetEntry(id); string url = tab?.PageState.Address;
            if (!BrowserLibrary.IsWebAddress(url)) url = entry?.LastAddress;
            if (!BrowserLibrary.IsWebAddress(url)) url = tab?.RequestedAddress?.AbsoluteUri;
            return new BrowserSavedPage(BrowserLibrary.IsWebAddress(url) ? url : "", tab?.PageState.Title, entry?.TextZoom ?? 100);
        }
        private void RememberClosed(long id) { library.RememberClosed(SavePage(id)); library.ForgetTab(id); }
        public void OpenPage(long window, BrowserSavedPage saved) { CreatePage(window, saved); }
        public void ReopenClosed(long window) { if (desktop.FindWindow(window)?.Tabs.Count >= 32) return; var saved = library.TakeClosed(); if (saved != null) CreatePage(window, saved); }
        public void ToggleBookmark(long window) {
            var id = desktop.FindWindow(window)?.ActiveTabId; if (!id.HasValue) return; var saved = SavePage(id.Value);
            library.ToggleBookmark(saved.address, saved.title); Refresh();
        }
        public void RemoveSaved(bool history, int index) {
            var list = history ? library.Data.history : library.Data.bookmarks;
            if (index >= 0 && index < list.Count) list.RemoveAt(index); MarkSave();
        }
        public void ClearHistory() { library.Data.history.Clear(); MarkSave(); }
        public void ForgetSession() {
            try { stateStore.Forget(); persistSession = false; library.Data.windows.Clear(); library.Data.recentlyClosed.Clear(); savePending = false;
                // Disable automatic restoration until the next launch; future explicit edits save a new session.
                foreach (var view in views.Values) view.ShowMessage("Saved session forgotten. Bookmarks and history are retained.");
                stateStore.Save(new BrowserSavedState { bookmarks = library.Data.bookmarks, history = library.Data.history });
            } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { foreach (var view in views.Values) view.ShowMessage("Saved data could not be removed."); }
        }
        public void Zoom(long window, int delta) {
            var id = desktop.FindWindow(window)?.ActiveTabId; if (!id.HasValue) return; var entry = GetEntry(id.Value);
            if (entry?.Page.Content == null || desktop.FindTab(id.Value)?.State != BrowserState.Ready) return;
            entry.TextZoom = Mathf.Clamp(entry.TextZoom + delta, 50, 200);
            _ = Observe(entry.Page.Content.SetTextZoomAsync(entry.TextZoom), window); MarkSave();
            if (views.TryGetValue(window, out var view)) view.ShowMessage("Text size " + entry.TextZoom + "%");
        }
        public Task Find(long window, string query, long revision) {
            var id = desktop.FindWindow(window)?.ActiveTabId; var content = id.HasValue ? GetEntry(id.Value)?.Page.Content : null;
            return content != null ? content.FindAsync(query, revision) : Task.CompletedTask;
        }
        public void FindNext(long window, bool forward) {
            var id = desktop.FindWindow(window)?.ActiveTabId; var content = id.HasValue ? GetEntry(id.Value)?.Page.Content : null;
            if (content != null) _ = Observe(content.FindNextAsync(forward), window);
        }
        public void ClearFindForTab(long tab) {
            if (closed || desktop?.FindTab(tab)?.State != BrowserState.Ready) return;
            var content = GetEntry(tab)?.Page.Content; var host = desktop.WindowOf(tab);
            if (content != null && host != null) { try { _ = Observe(content.ClearFindAsync(), host.Id); } catch (Exception error) { Failure(host.Id, error); } }
        }
        public void RecallWindows() {
            int index = 0;
            foreach (var view in views.Values) {
                var rect = (RectTransform)view.transform;
                if (worldCamera != null) {
                    var viewer = worldCamera.transform; int slot = index++;
                    rect.SetPositionAndRotation(viewer.position + viewer.forward * (1.8f + slot * .05f) + viewer.right * (slot * .08f), viewer.rotation);
                } else { rect.anchoredPosition3D = new Vector3(index++ * 100, 0, 0); rect.localRotation = Quaternion.identity; }
            }
            MarkSave();
        }
        internal void MarkSave() { if (!savePending) { savePending = true; saveAfter = Time.unscaledTime + 2; } }
        private BrowserSavedState Snapshot() {
            library.Data.windows.Clear();
            if (!persistSession) return library.Data;
            foreach (var window in desktop.Windows) {
                var saved = new BrowserSavedWindow(); saved.tabs.AddRange(window.Tabs.Select(SavePage));
                saved.active = window.ActiveTabId.HasValue ? window.Tabs.IndexOf(window.ActiveTabId.Value) : 0;
                if (views.TryGetValue(window.Id, out var view)) {
                    var rect = (RectTransform)view.transform; Vector3 p = rect.localPosition; Quaternion q = rect.localRotation;
                    saved.hasPlacement = true; saved.x = p.x; saved.y = p.y; saved.z = p.z; saved.qx = q.x; saved.qy = q.y; saved.qz = q.z; saved.qw = q.w;
                    saved.width = rect.sizeDelta.x; saved.height = rect.sizeDelta.y;
                }
                library.Data.windows.Add(saved);
            }
            return library.Data;
        }
        private void SaveState() {
            if (desktop == null || closed || stateStore == null) return; savePending = false;
            if (!stateStore.Save(Snapshot()) && diagnostics) Debug.LogWarning("[VRWebViewSample] event=session_save_unavailable");
        }
        private bool RestoreWindows() {
            var savedWindows = library.Data.windows.ToArray(); if (savedWindows.Length == 0) return false;
            changing = true;
            try {
                foreach (var saved in savedWindows) {
                    var window = desktop.CreateWindow(); var restored = new List<long>();
                    foreach (var tab in saved.tabs) { var created = CreatePage(window.Id, tab); if (created != null) restored.Add(created.Id); }
                    if (restored.Count > 0) desktop.Select(window.Id, restored[Mathf.Clamp(saved.active, 0, restored.Count - 1)]);
                }
            } finally { changing = false; Refresh(); }
            for (int i = 0; i < savedWindows.Length && i < desktop.Windows.Count; i++) {
                var saved = savedWindows[i]; if (!saved.hasPlacement || !views.TryGetValue(desktop.Windows[i].Id, out var view)) continue;
                var rect = (RectTransform)view.transform; rect.localPosition = new Vector3(saved.x, saved.y, saved.z);
                rect.localRotation = new Quaternion(saved.qx, saved.qy, saved.qz, saved.qw).normalized; rect.sizeDelta = new Vector2(saved.width, saved.height);
            }
            return true;
        }
        private void OnApplicationPause(bool paused) { if (paused && savePending) SaveState(); }
        private void OnApplicationQuit() { if (savePending) SaveState(); }
        private void OnApplicationFocus(bool focused) { if (!focused) CancelTabDrag(); }
        private void OnDisable() {
            CancelTabDrag(); foreach (var view in views.Values) view.Unbind();
            foreach (var entry in entries.Values) { entry.Page.Editing?.EndEditing(); entry.Page.SetPresentation(false, 0, 0); }
        }
        private void OnEnable() { if (desktop != null) Refresh(); }
        private void OnDestroy() {
            if (closed) return; if (savePending) SaveState(); closed = true; CancelTabDrag();
            foreach (var view in views.Values) if (view != null) view.Unbind();
            if (desktop != null) { desktop.Changed -= Refresh; desktop.Dispose(); }
            foreach (var entry in entries.Values) Unsubscribe(entry); entries.Clear(); views.Clear();
        }
    }
}
