using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace T34400.VRWebView.Samples.Browser
{
    [DisallowMultipleComponent]
    public sealed class BrowserPanel : MonoBehaviour
    {
        [SerializeField] private BrowserDispatcher dispatcher;
        [SerializeField] private BrowserView pageView;
        [SerializeField] private InputField address;
        [SerializeField] private Button go, newTab, closeTab;
        [SerializeField] private RectTransform tabStrip;
        [SerializeField] private Text status;
        [SerializeField] private Font font;
        private Func<BrowserDispatcher, BrowserPage> factory;
        private Scope scope;
        private readonly Dictionary<long, Button> buttons = new Dictionary<long, Button>();
        private Scope displayedScope;
        private long displayedTab = -1, boundTab = -1;
        private long renderVersion;
        private long configurationVersion;
        private bool hooked, interactionAllowed = true, destroying;
        private string notice;
        private string newTabAddress = "";
        private BrowserAddressFocus addressFocus;
        public bool IsAddressFocused => address != null && address.isFocused;
        public void SetNewTabAddress(string value) { newTabAddress = value ?? ""; }
        public Exception LastError { get; private set; }
        public BrowserTab ActiveTab => scope?.Workspace.ActiveTab;
        public int TabCount => scope?.Workspace.Tabs.Count ?? 0;
        public bool IsConfigured => scope != null && !scope.Closed;
        public string StatusText => status != null ? status.text : "";
        private bool HasWidgets => dispatcher != null && pageView != null && address != null && go != null && newTab != null && closeTab != null && tabStrip != null && status != null && font != null;

        private sealed class Scope : IDisposable
        {
            internal readonly BrowserPanel Panel;
            internal readonly BrowserDispatcher Owner;
            internal readonly BrowserWorkspace Workspace;
            internal readonly Dictionary<long, BrowserPage> Pages = new Dictionary<long, BrowserPage>();
            internal readonly Dictionary<long, BrowserAddressDraft> Drafts = new Dictionary<long, BrowserAddressDraft>();
            internal readonly Action<Action> Schedule;
            internal BrowserPage Pending;
            internal bool Creating, Closed;
            internal IDisposable Lifetime;
            private readonly Func<BrowserDispatcher, BrowserPage> factory;
            internal Scope(BrowserPanel panel, BrowserDispatcher owner, Func<BrowserDispatcher, BrowserPage> factory)
            {
                Panel = panel; Owner = owner; this.factory = factory; Schedule = owner.Scheduler;
                Workspace = new BrowserWorkspace(CreateBackend, Schedule);
                Workspace.Changed += Changed;
            }
            private IBrowserBackend CreateBackend()
            {
                var page = factory(Owner) ?? throw new InvalidOperationException("Page factory returned null");
                if (Closed) { page.Dispose(); throw new ObjectDisposedException(nameof(Scope)); }
                Pending = page; return page.Backend;
            }
            private void Changed() { if (!Closed && ReferenceEquals(Panel.scope, this)) Panel.Render(); }
            public void Dispose()
            {
                if (Closed) return;
                Closed = true; Workspace.Changed -= Changed;
                Panel.ScopeEnded(this);
                try { Workspace.Dispose(); }
                finally {
                    try { Pending?.Dispose(); }
                    finally { Pending = null; Pages.Clear(); Drafts.Clear(); Lifetime?.Dispose(); }
                }
            }
        }

        public void SetWidgets(BrowserDispatcher owner, BrowserView view, InputField url, Button navigate,
            Button create, Button close, RectTransform tabs, Text message, Font labelFont)
        {
            EndScope(); Unhook();
            dispatcher = owner; pageView = view; address = url; go = navigate; newTab = create; closeTab = close;
            tabStrip = tabs; status = message; font = labelFont;
            if (isActiveAndEnabled) EnableUI();
        }
        public void Configure(Func<BrowserDispatcher, BrowserPage> pageFactory)
        {
            if (!HasWidgets) throw new InvalidOperationException("Assign browser UI widgets before configuring");
            if (dispatcher.IsRunning) dispatcher.VerifyAccess();
            long configuring = ++configurationVersion;
            factory = null;
            try { EndScope(); }
            catch (Exception error) { if (configurationVersion == configuring) { Fail(error, "Could not finish closing the browser."); Render(); } throw; }
            if (configurationVersion != configuring) return;
            factory = pageFactory; notice = null; LastError = null;
            if (isActiveAndEnabled) EnableUI(); else Render();
        }
        private void OnEnable() { EnableUI(); }
        private void EnableUI()
        {
            if (!HasWidgets) return;
            if (dispatcher.isActiveAndEnabled) dispatcher.InitializeOnOwnerThread();
            if (!hooked) {
                go.onClick.AddListener(NavigateActive); newTab.onClick.AddListener(NewTab); closeTab.onClick.AddListener(CloseActiveTab);
                address.onValueChanged.AddListener(AddressEdited);
                addressFocus = address.GetComponent<BrowserAddressFocus>() ?? address.gameObject.AddComponent<BrowserAddressFocus>();
                addressFocus.Selected += EndActiveEditing; hooked = true;
            }
            if (scope == null && factory != null && dispatcher.IsRunning) {
                var created = new Scope(this, dispatcher, factory);
                try { created.Lifetime = dispatcher.Track(created); scope = created; }
                catch { created.Dispose(); throw; }
            }
            Render();
        }
        private void Update()
        {
            if (HasWidgets && scope == null && factory != null && dispatcher.IsRunning) EnableUI();
        }
        private void Unhook()
        {
            if (!hooked) return;
            if (go != null) go.onClick.RemoveListener(NavigateActive);
            if (newTab != null) newTab.onClick.RemoveListener(NewTab);
            if (closeTab != null) closeTab.onClick.RemoveListener(CloseActiveTab);
            if (address != null) address.onValueChanged.RemoveListener(AddressEdited);
            if (addressFocus != null) addressFocus.Selected -= EndActiveEditing;
            hooked = false;
        }
        private void OnDisable()
        {
            Unhook(); EndActiveEditing(); boundTab = -1;
            if (address != null) { address.DeactivateInputField(); address.interactable = false; }
            if (go != null) go.interactable = false;
            if (newTab != null) newTab.interactable = false;
            if (closeTab != null) closeTab.interactable = false;
            foreach (var button in buttons.Values) if (button != null) button.interactable = false;
            if (pageView != null) pageView.Unbind();
        }
        private void OnDestroy() { destroying = true; Unhook(); EndScope(); }

        private bool Current(Scope value) { return !destroying && value != null && !value.Closed && ReferenceEquals(scope, value); }
        public void NewTab()
        {
            var current = scope; if (!Current(current) || current.Creating || !isActiveAndEnabled) return;
            dispatcher.VerifyAccess(); current.Creating = true; notice = null; LastError = null;
            try {
                var tab = current.Workspace.CreateTab();
                if (!Current(current)) return;
                var page = current.Pending ?? throw new InvalidOperationException("Factory did not create page bindings");
                current.Pending = null; current.Pages.Add(tab.Id, page);
                var draft = new BrowserAddressDraft(); draft.Edit(newTabAddress); current.Drafts.Add(tab.Id, draft);
                _ = Observe(tab.InitializeAsync(), current, tab, null);
            } catch (Exception error) { if (Current(current)) Fail(error, "Could not create the page."); }
            finally { current.Creating = false; if (Current(current)) Render(); }
        }
        public void SelectTab(long id)
        {
            if (!Current(scope) || !isActiveAndEnabled) return; dispatcher.VerifyAccess(); notice = null;
            scope.Workspace.SelectTab(id); Render();
        }
        public void CloseActiveTab()
        {
            var current = scope; var tab = current?.Workspace.ActiveTab;
            if (!Current(current) || tab == null || !isActiveAndEnabled) return;
            dispatcher.VerifyAccess(); notice = null;
            boundTab = -1; if (pageView != null) pageView.Unbind();
            try { current.Workspace.CloseTab(tab.Id); }
            catch (Exception error) { if (Current(current)) Fail(error, "Could not finish closing the page."); }
            finally { current.Pages.Remove(tab.Id); current.Drafts.Remove(tab.Id); if (Current(current)) Render(); }
        }
        public void NavigateActive()
        {
            var current = scope; var tab = current?.Workspace.ActiveTab;
            if (!Current(current) || tab == null || !isActiveAndEnabled || !current.Drafts.TryGetValue(tab.Id, out var draft)) return;
            dispatcher.VerifyAccess(); notice = null; LastError = null;
            var submitted = draft.Capture();
            try { _ = Observe(tab.NavigateAsync(submitted.Text), current, tab, submitted); }
            catch (ArgumentException error) { if (Current(current)) Fail(error, "Enter an absolute HTTP(S) URL."); }
            catch (Exception error) { if (Current(current)) Fail(error, "Could not send the navigation request."); }
            if (Current(current)) Render();
        }
        private async Task Observe(Task operation, Scope current, BrowserTab tab, BrowserAddressDraft.Submission submitted)
        {
            Exception failure = null;
            try { await operation.ConfigureAwait(false); } catch (Exception error) { failure = error; }
            try { current.Schedule(() => {
                if (!Current(current) || tab.IsClosed || !current.Pages.ContainsKey(tab.Id)) return;
                if (failure != null) {
                    LastError = failure;
                    if (current.Workspace.ActiveTab == tab) notice = failure is OperationCanceledException ? "Request canceled." : "Page operation failed.";
                } else if (submitted != null) {
                    current.Drafts[tab.Id].Acknowledge(submitted, tab.RequestedAddress);
                    if (current.Workspace.ActiveTab == tab) notice = "Navigation request sent.";
                }
                Render();
            }); }
            catch (ObjectDisposedException) { } // An ended scope cannot deliver UI work.
            catch (Exception error) { Debug.LogException(error); }
        }
        private void AddressEdited(string value)
        {
            var current = displayedScope;
            if (!Current(current) || !current.Drafts.TryGetValue(displayedTab, out var draft)) return;
            draft.Edit(value); notice = null; Render();
        }
        public void EndActiveEditing()
        {
            var current = scope; var tab = current?.Workspace.ActiveTab;
            if (!Current(current) || tab == null || !current.Pages.TryGetValue(tab.Id, out var page)) return;
            EndPageEditing(page);
        }
        private void EndPageEditing(BrowserPage page)
        {
            var editing = page.Editing; if (editing == null) return;
            try { editing.EndEditing(); }
            catch (Exception error) { LastError = error; Debug.LogException(error); }
        }
        public void SetPageInteractionAllowed(bool allowed)
        {
            if (dispatcher != null && dispatcher.IsRunning) dispatcher.VerifyAccess();
            interactionAllowed = allowed; Render();
        }
        private void ScopeEnded(Scope ended)
        {
            if (!ReferenceEquals(scope, ended)) return;
            scope = null; boundTab = -1;
            if (pageView != null) pageView.Unbind();
            if (!destroying) Render();
        }
        private void EndScope()
        {
            long ending = ++renderVersion;
            var previous = scope; scope = null; boundTab = -1;
            if (address != null) address.DeactivateInputField();
            if (ending == renderVersion && pageView != null) pageView.Unbind();
            try { previous?.Dispose(); }
            finally {
                if (ending == renderVersion) { ClearButtons(); displayedScope = null; displayedTab = -1; }
            }
        }
        private void Fail(Exception error, string message) { LastError = error; notice = message; }

        private void Render()
        {
            if (!HasWidgets || !isActiveAndEnabled) return;
            long rendering = ++renderVersion;
            var current = scope; bool configured = Current(current);
            var tab = configured ? current.Workspace.ActiveTab : null;
            if (!ReferenceEquals(displayedScope, current) || displayedTab != (tab?.Id ?? -1)) {
                if (displayedScope != null && !displayedScope.Closed && displayedScope.Pages.TryGetValue(displayedTab, out var previous))
                    EndPageEditing(previous);
                if (rendering != renderVersion) return;
                address.DeactivateInputField();
                if (rendering != renderVersion) return;
            }
            newTab.interactable = configured && !current.Creating;
            closeTab.interactable = tab != null; address.interactable = tab != null;
            if (!ReferenceEquals(displayedScope, current)) { ClearButtons(); displayedScope = current; displayedTab = -1; }
            var live = configured ? new HashSet<long>(current.Workspace.Tabs.Select(value => value.Id)) : new HashSet<long>();
            foreach (long id in buttons.Keys.Where(id => !live.Contains(id)).ToArray()) { if (buttons[id] != null) BrowserSampleWidgets.Remove(buttons[id].gameObject); buttons.Remove(id); }
            if (configured) foreach (var item in current.Workspace.Tabs) {
                if (!buttons.TryGetValue(item.Id, out var button) || button == null) {
                    long id = item.Id; var expected = current;
                    button = BrowserSampleWidgets.Button(tabStrip, "Tab-" + id, "Tab " + id, font, 140);
                    button.onClick.AddListener(() => { if (Current(expected)) SelectTab(id); }); buttons[id] = button;
                }
                button.interactable = true;
                button.GetComponent<Image>().color = item == tab ? BrowserSampleWidgets.SelectedColor : BrowserSampleWidgets.ButtonColor;
            }
            if (configured) foreach (var entry in current.Pages)
                entry.Value.SetPresentation(tab != null && entry.Key == tab.Id, 0, 0);
            string text = tab != null && current.Drafts.TryGetValue(tab.Id, out var draft) ? draft.Text : "";
            if (address.text != text) address.SetTextWithoutNotify(text);
            displayedTab = tab?.Id ?? -1;
            bool valid = false; try { NavigationPolicy.Parse(text); valid = true; } catch (ArgumentException) { }
            go.interactable = tab != null && tab.State == BrowserState.Ready && !tab.IsBusy && valid;
            if (tab == null || !current.Pages.TryGetValue(tab.Id, out var page)) {
                if (boundTab != -1) pageView.Unbind();
                if (rendering != renderVersion) return;
                boundTab = -1;
            } else {
                if (boundTab != tab.Id || !pageView.IsConfigured) {
                    pageView.Bind(page.Surface, page.Input, dispatcher);
                    if (rendering != renderVersion) return;
                    boundTab = tab.Id;
                }
                pageView.SetInteractionEnabled(interactionAllowed && tab.State == BrowserState.Ready);
                if (rendering != renderVersion) return;
            }
            status.text = notice ?? (!configured ? "Browser not configured." : tab == null ? "Create a tab to begin."
                : tab.LastError != null ? "Page operation failed." : tab.IsBusy ? "Sending page request..."
                : tab.State == BrowserState.Ready ? "Ready for a URL." : "Page is not ready.");
        }
        private void ClearButtons()
        {
            foreach (var value in buttons.Values) if (value != null) BrowserSampleWidgets.Remove(value.gameObject);
            buttons.Clear();
        }
    }
}
