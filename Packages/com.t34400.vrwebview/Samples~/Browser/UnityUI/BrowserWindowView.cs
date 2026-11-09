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
        private BrowserAddressInput address;
        private Text message, placeholder;
        private Button back, forward, reload, go;
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
            var tabRow = Row(root.transform, "Tabs", 52); tabsRowRect = tabRow;
            var strip = BrowserSampleWidgets.Node(tabRow, "Tab viewport", typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            strip.GetComponent<Image>().color = BrowserSampleWidgets.Background; strip.GetComponent<LayoutElement>().flexibleWidth = 1;
            viewport = (RectTransform)strip.transform;
            tabs = (RectTransform)BrowserSampleWidgets.Node(strip.transform, "Tab strip", typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter)).transform;
            tabs.anchorMin = Vector2.zero; tabs.anchorMax = new Vector2(0, 1); tabs.pivot = new Vector2(0, .5f); tabs.sizeDelta = Vector2.zero;
            var layout = tabs.GetComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true;
            layout.childForceExpandHeight = true; layout.childForceExpandWidth = false;
            tabs.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var tabScroll = strip.GetComponent<ScrollRect>(); tabScroll.content = tabs; tabScroll.viewport = viewport; tabScroll.horizontal = true; tabScroll.vertical = false; tabScroll.movementType = ScrollRect.MovementType.Clamped;
            insertionMarker = (RectTransform)BrowserSampleWidgets.Node(strip.transform, "Insertion marker", typeof(Image)).transform;
            insertionMarker.GetComponent<Image>().color = Color.cyan; insertionMarker.GetComponent<Image>().raycastTarget = false;
            insertionMarker.anchorMin = insertionMarker.anchorMax = new Vector2(0, .5f); insertionMarker.sizeDelta = new Vector2(4, 48); insertionMarker.gameObject.SetActive(false);
            AddButton(tabRow, "New tab", "+", 52, () => application.NewTab(WindowId));
            AddButton(tabRow, "New window", "New window", 145, application.CreateWindow);
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
            var hint = BrowserSampleWidgets.Label(input.transform, "Hint", "Enter a web address", font); hint.color = Color.gray; BrowserSampleWidgets.Stretch(hint.rectTransform, 10); address.placeholder = hint;
            address.lineType = InputField.LineType.SingleLine; address.keyboardType = TouchScreenKeyboardType.Default; address.shouldHideMobileInput = false;
            address.PrepareEditing = () => application.PrepareAddress(WindowId); address.EditingFailed += ShowMessage;
            address.onValueChanged.AddListener(value => { notice = null; application.EditAddress(WindowId, value); });
            address.onEndEdit.AddListener(_ => application.RequestRefresh());
            address.Submitted += () => { address.DeactivateInputField(); application.Navigate(WindowId); };
            go = AddButton(nav, "Go", "Go", 65, () => { address.DeactivateInputField(); application.Navigate(WindowId); });
            var content = BrowserSampleWidgets.Node(root.transform, "Page area", typeof(Image), typeof(LayoutElement));
            content.GetComponent<Image>().color = new Color(.025f, .03f, .045f); var contentSize = content.GetComponent<LayoutElement>(); contentSize.minHeight = contentSize.preferredHeight = 120; contentSize.flexibleHeight = 1;
            pageRect = (RectTransform)content.transform;
            var display = BrowserSampleWidgets.Node(content.transform, "Web page", typeof(RawImage), typeof(BrowserPointerSource), typeof(BrowserView));
            BrowserSampleWidgets.Stretch((RectTransform)display.transform); page = display.GetComponent<BrowserView>();
            placeholder = BrowserSampleWidgets.Label(content.transform, "New tab page", "New tab\nEnter an address above to begin.", font, 28); placeholder.alignment = TextAnchor.MiddleCenter; BrowserSampleWidgets.Stretch(placeholder.rectTransform);
            var footer = Row(root.transform, "Window handle", 40);
            message = BrowserSampleWidgets.Label(footer, "Status", "", font, 18); message.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            var handle = AddButton(footer, "Move window", "Move", 100, null).gameObject.AddComponent<BrowserWindowDrag>(); handle.Configure(this, false);
            var resize = AddButton(footer, "Resize window", "Resize", 100, null).gameObject.AddComponent<BrowserWindowDrag>(); resize.Configure(this, true);
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
                    var title = button.GetComponentInChildren<Text>(); title.rectTransform.offsetMax = new Vector2(-48, -5); title.alignment = TextAnchor.MiddleLeft; title.horizontalOverflow = HorizontalWrapMode.Wrap;
                    var close = AddButton(button.transform, "Close tab", "×", 44, () => application.CloseTab(id));
                    var closeRect = (RectTransform)close.transform; closeRect.anchorMin = new Vector2(1, 0); closeRect.anchorMax = Vector2.one; closeRect.pivot = new Vector2(1, .5f); closeRect.offsetMin = new Vector2(-44, 0); closeRect.offsetMax = Vector2.zero;
                    button.gameObject.AddComponent<BrowserTabDrag>().Configure(application, this, id); tabButtons.Add(id, button);
                }
                var tab = application.Model.FindTab(id); var state = tab.PageState;
                string label = state.Title;
                if (string.IsNullOrWhiteSpace(label) && Uri.TryCreate(state.Address, UriKind.Absolute, out var uri)) label = uri.Host;
                if (string.IsNullOrWhiteSpace(label)) label = "New tab";
                if (label.Length > 22) label = label.Substring(0, 21) + "…";
                button.GetComponentInChildren<Text>().text = (state.IsLoading ? "… " : "") + label;
                button.GetComponent<Image>().color = window.ActiveTabId == id ? BrowserSampleWidgets.SelectedColor : BrowserSampleWidgets.ButtonColor;
                button.transform.SetSiblingIndex(order++);
            }
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
            placeholder.gameObject.SetActive(active == null || string.IsNullOrEmpty(observed.Address) || observed.Address == "about:blank");
            message.text = notice ?? (observed.ErrorCode != 0 ? "Page failed to load. Reload to retry."
                : observed.IsLoading ? "Loading " + observed.Progress + "%" : active != null && active.State == BrowserState.Faulted ? "Browser failed." : "");
        }
        private void RevealSelected() {
            if (selected < 0 || !tabButtons.TryGetValue(selected, out var button)) return;
            float left = ((RectTransform)button.transform).anchoredPosition.x, right = left + ((RectTransform)button.transform).rect.width;
            float offset = -tabs.anchoredPosition.x;
            if (left < offset) offset = left; else if (right > offset + viewport.rect.width) offset = right - viewport.rect.width;
            tabs.anchoredPosition = new Vector2(-Mathf.Max(0, offset), tabs.anchoredPosition.y);
        }
        public void UpdatePresentation() {
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
        public void ShowMessage(string value) { notice = value; if (message != null) message.text = value; }
        public bool IsNearTabs(Vector2 screen, Camera camera, float margin) {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, camera, out var local)) return false;
            Rect rect = viewport.rect; rect.xMin -= margin; rect.xMax += margin; rect.yMin -= margin; rect.yMax += margin; return rect.Contains(local);
        }
        public void AutoScrollTabs(Vector2 screen, Camera camera) {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, camera, out var point)) return;
            float direction = point.x < viewport.rect.xMin + 50 ? 1 : point.x > viewport.rect.xMax - 50 ? -1 : 0;
            float x = Mathf.Clamp(tabs.anchoredPosition.x + direction * 650 * Time.unscaledDeltaTime, -Mathf.Max(0, tabs.rect.width - viewport.rect.width), 0);
            tabs.anchoredPosition = new Vector2(x, tabs.anchoredPosition.y);
        }
        public int InsertionAt(Vector2 screen, Camera camera) {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(tabs, screen, camera, out var local)) return 0;
            int index = 0;
            foreach (Transform child in tabs) { var rect = (RectTransform)child; if (local.x < rect.anchoredPosition.x + rect.rect.width * .5f) break; index++; }
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
