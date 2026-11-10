using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    public sealed class BrowserWindowView : MonoBehaviour
    {
        public long WindowId { get; private set; }
        private BrowserDesktopPanel application;
        private BrowserDispatcher owner;
        private BrowserView page;
        private BrowserWindowTools tools;
        private Button bookmark;
        private BrowserAddressInput address;
        private Text message, placeholder;
        private Button back, forward, reload, go, newTabButton;
        private RectTransform freeHeader;
        private RectTransform tabs, viewport, pageRect, tabsRowRect, navigationRect;
        private Font font;
        private readonly Dictionary<long, Button> tabButtons = new Dictionary<long, Button>();
        private long bound = -1;
        private int lastWidth, lastHeight;
        private float resizeAfter;
        private Vector2 previousSize;
        private string notice;
        private RectTransform insertionMarker;
        private bool dropPreview;
        private long selected = -1;
        public Camera Viewer => application.worldCamera;
        public BrowserWindowPlacementMode PlacementMode => application.placementMode;
        public bool AddressFocused => address != null && address.isFocused;
        public void Build(BrowserDesktopPanel app, long id, BrowserDispatcher dispatcher) {
            application = app; WindowId = id; owner = dispatcher;
            var rect = (RectTransform)transform; rect.sizeDelta = new Vector2(1280, 1000);
            previousSize = rect.sizeDelta;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = BrowserSampleWidgets.Node(transform, "Browser frame", typeof(Image), typeof(VerticalLayoutGroup));
            BrowserSampleWidgets.Stretch((RectTransform)root.transform); root.GetComponent<Image>().color = BrowserSampleWidgets.Background;
            var column = root.GetComponent<VerticalLayoutGroup>(); column.padding = new RectOffset(12, 12, 12, 12); column.spacing = 8;
            column.childControlWidth = column.childControlHeight = true; column.childForceExpandWidth = true; column.childForceExpandHeight = false;
            var tabRow = Row(root.transform, "Tabs", 68); tabsRowRect = tabRow;
            var stripColumn = BrowserSampleWidgets.Node(tabRow, "Tab area", typeof(VerticalLayoutGroup), typeof(LayoutElement));
            stripColumn.GetComponent<LayoutElement>().flexibleWidth = 1;
            var stripLayout = stripColumn.GetComponent<VerticalLayoutGroup>(); stripLayout.spacing = 4;
            stripLayout.childControlWidth = stripLayout.childControlHeight = true; stripLayout.childForceExpandWidth = true; stripLayout.childForceExpandHeight = false;
            var strip = BrowserSampleWidgets.Node(stripColumn.transform, "Tab viewport", typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            strip.GetComponent<Image>().color = BrowserSampleWidgets.Background;
            var stripSize = strip.GetComponent<LayoutElement>(); stripSize.minHeight = stripSize.preferredHeight = 52; stripSize.flexibleHeight = 0;
            viewport = (RectTransform)strip.transform;
            freeHeader = (RectTransform)BrowserSampleWidgets.Node(strip.transform, "Unused tab space", typeof(Image), typeof(BrowserWindowDrag)).transform;
            freeHeader.GetComponent<Image>().color = BrowserSampleWidgets.Background;
            freeHeader.GetComponent<BrowserWindowDrag>().Configure(this);
            freeHeader.anchorMin = Vector2.zero; freeHeader.anchorMax = new Vector2(0, 1); freeHeader.pivot = new Vector2(0, .5f);
            tabs = (RectTransform)BrowserSampleWidgets.Node(strip.transform, "Tab strip", typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter)).transform;
            tabs.anchorMin = Vector2.zero; tabs.anchorMax = new Vector2(0, 1); tabs.pivot = new Vector2(0, .5f); tabs.sizeDelta = Vector2.zero;
            var layout = tabs.GetComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = true; layout.childForceExpandWidth = false;
            tabs.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var tabScroll = strip.GetComponent<ScrollRect>(); tabScroll.content = tabs; tabScroll.viewport = viewport;
            tabScroll.horizontal = true; tabScroll.vertical = false; tabScroll.movementType = ScrollRect.MovementType.Clamped;
            tabScroll.horizontalScrollbar = BuildTabScrollbar(stripColumn.transform);
            tabScroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            insertionMarker = (RectTransform)BrowserSampleWidgets.Node(strip.transform, "Insertion marker", typeof(Image)).transform;
            insertionMarker.GetComponent<Image>().color = Color.cyan; insertionMarker.GetComponent<Image>().raycastTarget = false;
            insertionMarker.anchorMin = insertionMarker.anchorMax = new Vector2(0, .5f); insertionMarker.sizeDelta = new Vector2(4, 48); insertionMarker.gameObject.SetActive(false);
            newTabButton = AddButton(tabs, "New tab", "+", 52, () => application.NewTab(WindowId));
            var grip = BrowserSampleWidgets.Node(tabRow, "Reserved title drag space", typeof(Image), typeof(LayoutElement), typeof(BrowserWindowDrag));
            grip.GetComponent<Image>().color = BrowserSampleWidgets.Background;
            var gripSize = grip.GetComponent<LayoutElement>(); gripSize.minWidth = gripSize.preferredWidth = 144; gripSize.flexibleWidth = 0;
            grip.GetComponent<BrowserWindowDrag>().Configure(this);
            AddButton(tabRow, "New window", "□+", 52, application.CreateWindow);
            AddButton(tabRow, "Close window", "×", 52, () => application.CloseWindow(WindowId));
            var nav = Row(root.transform, "Navigation", 52); navigationRect = nav;
            back = AddButton(nav, "Back", "←", 52, () => application.Command(WindowId, BrowserPageCommand.Back));
            forward = AddButton(nav, "Forward", "→", 52, () => application.Command(WindowId, BrowserPageCommand.Forward));
            reload = AddButton(nav, "Reload", "↻", 52, () => {
                var tab = CurrentTab(); if (tab != null) application.Command(WindowId, tab.PageState.IsLoading ? BrowserPageCommand.Stop : BrowserPageCommand.Reload);
            });
            var input = BrowserSampleWidgets.Node(nav, "Address", typeof(Image), typeof(BrowserAddressInput), typeof(LayoutElement));
            input.GetComponent<Image>().color = new Color(.11f, .14f, .2f, 1); input.GetComponent<LayoutElement>().flexibleWidth = 1; input.GetComponent<LayoutElement>().minWidth = 160;
            address = input.GetComponent<BrowserAddressInput>(); address.targetGraphic = input.GetComponent<Image>();
            address.textComponent = BrowserSampleWidgets.Label(input.transform, "Text", "", font); BrowserSampleWidgets.Stretch(address.textComponent.rectTransform, 10);
            var hint = BrowserSampleWidgets.Label(input.transform, "Hint", "Search or enter address", font); hint.color = Color.gray; BrowserSampleWidgets.Stretch(hint.rectTransform, 10); address.placeholder = hint;
            address.lineType = InputField.LineType.SingleLine; address.keyboardType = TouchScreenKeyboardType.Default; address.shouldHideMobileInput = false;
            address.PrepareEditing = () => application.PrepareAddress(WindowId); address.EditingFailed += ShowMessage;
            address.onValueChanged.AddListener(value => { notice = null; application.EditAddress(WindowId, value); });
            address.onEndEdit.AddListener(_ => application.RequestRefresh());
            address.Submitted += () => { address.DeactivateInputField(); application.Navigate(WindowId); };
            go = AddButton(nav, "Go", "Go", 65, () => { address.DeactivateInputField(); application.Navigate(WindowId); });
            bookmark = AddButton(nav, "Bookmark page", "☆", 52, () => application.ToggleBookmark(WindowId));
            AddButton(nav, "Browser menu", "☰", 52, () => tools.Toggle());
            var toolContainer = BrowserSampleWidgets.Node(root.transform, "Tool bars", typeof(VerticalLayoutGroup));
            var toolLayout = toolContainer.GetComponent<VerticalLayoutGroup>(); toolLayout.childControlHeight = toolLayout.childControlWidth = true; toolLayout.childForceExpandHeight = false;
            var content = BrowserSampleWidgets.Node(root.transform, "Page area", typeof(Image), typeof(LayoutElement));
            content.GetComponent<Image>().color = new Color(.025f, .03f, .045f); var contentSize = content.GetComponent<LayoutElement>(); contentSize.minHeight = contentSize.preferredHeight = 120; contentSize.flexibleHeight = 1;
            pageRect = (RectTransform)content.transform;
            var display = BrowserSampleWidgets.Node(content.transform, "Web page", typeof(RawImage), typeof(BrowserPointerSource), typeof(BrowserView));
            BrowserSampleWidgets.Stretch((RectTransform)display.transform); page = display.GetComponent<BrowserView>();
            display.AddComponent<BrowserWindowActivation>().Window = this;
            placeholder = BrowserSampleWidgets.Label(content.transform, "New tab page", "New tab\nEnter an address above to begin.", font, 28); placeholder.alignment = TextAnchor.MiddleCenter; BrowserSampleWidgets.Stretch(placeholder.rectTransform);
            tools = gameObject.AddComponent<BrowserWindowTools>(); tools.Build(application, this, content.transform, toolContainer.transform, font);
            var footer = Row(root.transform, "Status bar", 24);
            message = BrowserSampleWidgets.Label(footer, "Status", "", font, 18); message.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) BuildCorner(x, y);
        }
        public void NavigationStarted() { tools?.NavigationStarted(); }
        public void FindUpdated(long tab, BrowserFindResult result) { tools?.Found(tab, result); }
        public void ShowContext(BrowserContextTarget target) { tools?.ShowContext(target); }
        public void PlacementChanged() { application.MarkSave(); }
        public void BringToFront() { application.BringToFront(this); }
        private Scrollbar BuildTabScrollbar(Transform parent) {
            var node = BrowserSampleWidgets.Node(parent, "Tab scrollbar", typeof(Image), typeof(Scrollbar), typeof(LayoutElement));
            var size = node.GetComponent<LayoutElement>(); size.minHeight = size.preferredHeight = 12; size.flexibleHeight = 0;
            node.GetComponent<Image>().color = new Color(.08f, .1f, .14f);
            var area = (RectTransform)BrowserSampleWidgets.Node(node.transform, "Sliding area").transform; BrowserSampleWidgets.Stretch(area);
            var handle = BrowserSampleWidgets.Node(area, "Handle", typeof(Image)).GetComponent<Image>();
            handle.color = new Color(.4f, .5f, .6f); BrowserSampleWidgets.Stretch(handle.rectTransform);
            var bar = node.GetComponent<Scrollbar>(); bar.handleRect = handle.rectTransform; bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.LeftToRight; return bar;
        }
        private void BuildCorner(int x, int y) {
            var node = BrowserSampleWidgets.Node(transform, "Resize corner " + x + " " + y, typeof(Image), typeof(BrowserWindowDrag));
            var rect = (RectTransform)node.transform; rect.anchorMin = rect.anchorMax = new Vector2(x > 0 ? 1 : 0, y > 0 ? 1 : 0);
            rect.pivot = new Vector2(.5f, .5f); float size = Mathf.Clamp(application.cornerHitSize, 64, 160), outward = size * .5f - 16;
            rect.sizeDelta = new Vector2(size, size); rect.anchoredPosition = new Vector2(x * outward, y * outward);
            node.GetComponent<Image>().color = Color.clear;
            var horizontal = BrowserSampleWidgets.Node(node.transform, "Horizontal grip", typeof(Image)).GetComponent<Image>();
            horizontal.raycastTarget = false; horizontal.rectTransform.sizeDelta = new Vector2(32, 5); horizontal.rectTransform.anchoredPosition = new Vector2(-x * (outward + 12), -y * outward);
            var vertical = BrowserSampleWidgets.Node(node.transform, "Vertical grip", typeof(Image)).GetComponent<Image>();
            vertical.raycastTarget = false; vertical.rectTransform.sizeDelta = new Vector2(5, 32); vertical.rectTransform.anchoredPosition = new Vector2(-x * outward, -y * (outward + 12));
            node.GetComponent<BrowserWindowDrag>().Configure(this, x, y, new[] { horizontal, vertical });
        }
        private RectTransform Row(Transform parent, string name, float height) {
            var value = BrowserSampleWidgets.Node(parent, name, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var size = value.GetComponent<LayoutElement>(); size.minHeight = size.preferredHeight = height; size.flexibleHeight = 0;
            var layout = value.GetComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
            return (RectTransform)value.transform;
        }
        private Button AddButton(Transform parent, string name, string label, float width, Action action) {
            var button = BrowserSampleWidgets.Button(parent, name, label, font, width);
            button.gameObject.AddComponent<BrowserWindowActivation>().Window = this;
            if (action != null) button.onClick.AddListener(() => action());
            return button;
        }
        private BrowserTab CurrentTab() {
            var id = application.Model.FindWindow(WindowId)?.ActiveTabId; return id.HasValue ? application.Model.FindTab(id.Value) : null;
        }
        public void WithdrawIfChanged(long? active) {
            if (bound >= 0 && bound != (active ?? -1)) {
                application.GetEntry(bound)?.Page.Editing?.EndEditing(); page.Unbind(); address.DeactivateInputField(); bound = -1;
            }
        }
        public void Unbind() {
            if (page != null) page.Unbind();
            if (address != null) address.DeactivateInputField(); bound = -1;
        }
        public void Render(BrowserWindow window) {
            if (page == null) return;
            WithdrawIfChanged(window.ActiveTabId);
            foreach (var pair in tabButtons.ToArray()) if (!window.Tabs.Contains(pair.Key)) { BrowserSampleWidgets.Remove(pair.Value.gameObject); tabButtons.Remove(pair.Key); }
            int order = 0;
            foreach (long id in window.Tabs) {
                if (!tabButtons.TryGetValue(id, out var button)) {
                    button = AddButton(tabs, "Tab " + id, "New tab", 220, () => application.Select(WindowId, id));
                    var inner = button.gameObject.AddComponent<HorizontalLayoutGroup>(); inner.padding = new RectOffset(10, 4, 4, 4); inner.spacing = 4;
                    inner.childControlWidth = inner.childControlHeight = true; inner.childForceExpandHeight = true; inner.childForceExpandWidth = false;
                    var title = button.GetComponentInChildren<Text>(); title.alignment = TextAnchor.MiddleLeft; title.horizontalOverflow = HorizontalWrapMode.Wrap;
                    var titleSize = title.gameObject.AddComponent<LayoutElement>(); titleSize.minWidth = titleSize.preferredWidth = 0; titleSize.flexibleWidth = 1;
                    var close = AddButton(button.transform, "Close tab", "×", 40, () => application.CloseTab(id));
                    close.GetComponent<LayoutElement>().minHeight = 40; close.GetComponent<LayoutElement>().minWidth = 40;
                    close.GetComponent<Image>().color = Color.white;
                    var colors = close.colors; colors.normalColor = new Color(1, 1, 1, 0); colors.highlightedColor = new Color(.7f, .2f, .2f, 1);
                    colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.9f, .15f, .15f, 1); colors.disabledColor = colors.normalColor; close.colors = colors;
                    button.gameObject.AddComponent<BrowserTabDrag>().Configure(application, this, id); tabButtons.Add(id, button);
                }
                var tab = application.Model.FindTab(id); var state = tab.PageState;
                string label = state.Title;
                var savedEntry = application.GetEntry(id);
                if (string.IsNullOrWhiteSpace(label)) label = savedEntry?.RestorePage?.title;
                if (string.IsNullOrWhiteSpace(label) && Uri.TryCreate(string.IsNullOrEmpty(state.Address) ? savedEntry?.LastAddress : state.Address, UriKind.Absolute, out var uri)) label = uri.Host;
                if (string.IsNullOrWhiteSpace(label)) label = "New tab";
                if (label.Length > 22) label = label.Substring(0, 21) + "…";
                button.GetComponentInChildren<Text>().text = (state.IsLoading ? "… " : "") + label;
                button.GetComponent<Image>().color = window.ActiveTabId == id ? BrowserSampleWidgets.SelectedColor : BrowserSampleWidgets.ButtonColor;
                button.transform.SetSiblingIndex(order++);
            }
            newTabButton.transform.SetAsLastSibling();
            var active = CurrentTab(); var entry = active != null ? application.GetEntry(active.Id) : null;
            address.interactable = active != null;
            var observed = active?.PageState ?? BrowserPageState.Empty;
            if (entry != null) {
                if (!string.IsNullOrEmpty(observed.Address) && observed.Address != "about:blank") {
                    entry.LastAddress = observed.Address;
                    if (!entry.Dirty && !address.isFocused) entry.Draft.Edit(observed.Address);
                }
                if (!address.isFocused && address.text != entry.Draft.Text) address.SetTextWithoutNotify(entry.Draft.Text);
                if (bound != active.Id) { page.Bind(entry.Page.Surface, entry.Page.Input, owner); bound = active.Id; lastWidth = lastHeight = 0; }
            } else address.SetTextWithoutNotify("");
            if ((window.ActiveTabId ?? -1) != selected) {
                selected = window.ActiveTabId ?? -1; notice = null;
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
                RevealSelected();
            }
            bool ready = active != null && active.State == BrowserState.Ready;
            back.interactable = ready && observed.CanGoBack; forward.interactable = ready && observed.CanGoForward;
            reload.interactable = ready && !string.IsNullOrEmpty(observed.Address); reload.GetComponentInChildren<Text>().text = observed.IsLoading ? "×" : "↻";
            go.interactable = ready && !active.IsBusy;
            bool blank = active == null || string.IsNullOrEmpty(observed.Address) || observed.Address == "about:blank";
            placeholder.gameObject.SetActive(blank);
            tools.Render(active?.Id ?? -1, blank);
            bookmark.interactable = ready && BrowserLibrary.IsWebAddress(observed.Address);
            bookmark.GetComponentInChildren<Text>().text = application.Library.IsBookmarked(observed.Address) ? "★" : "☆";
            message.text = notice ?? (observed.ErrorCode != 0 ? "Page failed to load. Reload to retry."
                : observed.IsLoading ? "Loading " + observed.Progress + "%" : active != null && active.State == BrowserState.Faulted ? "Browser failed." : "");
        }
        private void RevealSelected() {
            if (selected < 0 || !tabButtons.TryGetValue(selected, out var button)) return;
            var rect = (RectTransform)button.transform;
            float left = rect.anchoredPosition.x + rect.rect.xMin, right = left + rect.rect.width;
            float offset = -tabs.anchoredPosition.x;
            if (left < offset) offset = left; else if (right > offset + viewport.rect.width) offset = right - viewport.rect.width;
            tabs.anchoredPosition = new Vector2(-Mathf.Max(0, offset), tabs.anchoredPosition.y);
        }
        public void UpdatePresentation() {
            if (pageRect != null && lastWidth == 0) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);
            UpdateHeaderSpace();
            if (pageRect == null || !isActiveAndEnabled || !application.isActiveAndEnabled) return;
            Vector2 size = ((RectTransform)transform).rect.size;
            if (size != previousSize) { previousSize = size; resizeAfter = Time.unscaledTime + .15f; }
            if (Time.unscaledTime < resizeAfter || bound < 0) return;
            int width = Mathf.Clamp(Mathf.RoundToInt(pageRect.rect.width), 128, 2048), height = Mathf.Clamp(Mathf.RoundToInt(pageRect.rect.height), 128, 2048);
            if (width == lastWidth && height == lastHeight) return;
            lastWidth = width; lastHeight = height;
            application.GetEntry(bound)?.Page.SetPresentation(true, width, height);
            if (application.diagnostics) Debug.Log("[VRWebViewSample] BS1 event=window_layout window=" + WindowId + " width=" + width + " height=" + height
                + " tabHeight=" + tabsRowRect.rect.height + " navigationHeight=" + navigationRect.rect.height + " windowHeight=" + ((RectTransform)transform).rect.height);
        }
        private void UpdateHeaderSpace() {
            if (freeHeader == null) return;
            float left = Mathf.Clamp(tabs.anchoredPosition.x + tabs.rect.width, 0, viewport.rect.width);
            float width = Mathf.Max(0, viewport.rect.width - left);
            freeHeader.gameObject.SetActive(width > 1);
            freeHeader.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, left, width);
        }
        private static bool LocalPoint(RectTransform rect, PointerEventData data, out Vector2 local) {
            if (data is BrowserRayPointerEventData ray && ray.HasWorldRay) {
                var plane = new Plane(rect.forward, rect.position);
                if (plane.Raycast(ray.WorldRay, out float distance) && distance >= 0) {
                    Vector3 point = rect.InverseTransformPoint(ray.WorldRay.GetPoint(distance)); local = new Vector2(point.x, point.y); return true;
                }
                local = default; return false;
            }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, data.position, data.enterEventCamera ?? data.pressEventCamera, out local);
        }
        public void ShowMessage(string value) { notice = value; if (message != null) message.text = value; }
        public bool IsNearTabs(PointerEventData data, float margin) {
            if (!LocalPoint(viewport, data, out var local)) return false;
            Rect rect = viewport.rect; rect.xMin -= margin; rect.xMax += margin; rect.yMin -= margin; rect.yMax += margin; return rect.Contains(local);
        }
        public void AutoScrollTabs(PointerEventData data) {
            if (!LocalPoint(viewport, data, out var point)) return;
            float direction = point.x < viewport.rect.xMin + 50 ? 1 : point.x > viewport.rect.xMax - 50 ? -1 : 0;
            float x = Mathf.Clamp(tabs.anchoredPosition.x + direction * 650 * Time.unscaledDeltaTime, -Mathf.Max(0, tabs.rect.width - viewport.rect.width), 0);
            tabs.anchoredPosition = new Vector2(x, tabs.anchoredPosition.y);
        }
        public int InsertionAt(PointerEventData data) {
            if (!LocalPoint(tabs, data, out var local)) return 0;
            int index = 0;
            foreach (Transform child in tabs) {
                if (child.GetComponent<BrowserTabDrag>() == null) break;
                var rect = (RectTransform)child;
                if (local.x < rect.anchoredPosition.x + rect.rect.center.x) break; index++;
            }
            return index;
        }
        public void SetDropPreview(bool active, int slot) {
            if (tabs == null) return;
            foreach (var pair in tabButtons) {
                bool selectedTab = pair.Key == selected;
                pair.Value.GetComponent<Image>().color = active && pair.Value.transform.GetSiblingIndex() == slot
                    ? new Color(.2f, .65f, .7f) : selectedTab ? BrowserSampleWidgets.SelectedColor : BrowserSampleWidgets.ButtonColor;
            }
            if (insertionMarker != null) {
                insertionMarker.gameObject.SetActive(active);
                insertionMarker.anchoredPosition = new Vector2(Mathf.Clamp(tabs.anchoredPosition.x + slot * 228 - 4, 2, viewport.rect.width - 2), 0);
            }
            if (active) message.text = "Insert tab at " + (slot + 1);
            else if (dropPreview) message.text = notice ?? "";
            dropPreview = active;
        }
        private void OnDisable() { Unbind(); }
    }
}
