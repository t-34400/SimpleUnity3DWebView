using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Sample chrome: anchored menus, inline find and a separate blank-tab home.</summary>
    public sealed class BrowserWindowTools : MonoBehaviour {
        private BrowserDesktopPanel app;
        private BrowserWindowView window;
        private BrowserPopupHost popup;
        private RectTransform menuAnchor, bookmarkAnchor, pageRect;
        private BrowserPagePressAnchor pagePress;
        private Font font;
        private GameObject home, findRow;
        private Transform homeContent;
        private Text count, zoomLabel, tiltLabel;
        private BrowserAddressInput findInput;
        private readonly List<Button> homeButtons = new List<Button>();
        private long tab = -1, revision;
        private string homeSignature, lastAddress;
        private bool wasLoading;
        public bool MenuOpen => popup != null && popup.IsOpen;
        public BrowserPopupHost Popups => popup;
        private bool Ready => app.Model?.FindTab(tab)?.State == BrowserState.Ready && !app.Model.FindTab(tab).IsBusy;
        private bool HasContent => Ready && app.GetEntry(tab)?.Page.Content != null;
        public void Build(BrowserDesktopPanel application, BrowserWindowView view, RectTransform page, RectTransform menu, RectTransform star, BrowserPagePressAnchor press, Font typeface) {
            app = application; window = view; pageRect = page; menuAnchor = menu; bookmarkAnchor = star; pagePress = press; font = typeface;
            popup = gameObject.AddComponent<BrowserPopupHost>();
            BuildHome(); BuildFind();
        }
        private Transform Row(Transform parent, string name, float height) {
            var node = BrowserSampleWidgets.Node(parent, name, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var size = node.GetComponent<LayoutElement>(); size.minHeight = size.preferredHeight = height; size.flexibleHeight = 0;
            var layout = node.GetComponent<HorizontalLayoutGroup>(); layout.spacing = 6; layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = false; layout.childForceExpandHeight = true;
            return node.transform;
        }
        private Text Label(Transform parent, string name, string text, float height, int size = 20) {
            var value = BrowserSampleWidgets.Label(parent, name, text, font, size);
            var layout = BrowserSampleWidgets.FlexibleWidth(value.gameObject); layout.minHeight = layout.preferredHeight = height;
            return value;
        }
        private Button Button(Transform parent, string name, string label, Action action, float width = 0) {
            var value = BrowserSampleWidgets.Button(parent, name, label, font, width);
            if (width == 0) BrowserSampleWidgets.FlexibleWidth(value.gameObject);
            value.gameObject.AddComponent<BrowserWindowActivation>().Window = window;
            value.onClick.AddListener(() => action()); return value;
        }
        private Button Item(Transform parent, string label, Action action, bool enabled = true) {
            var button = Button(parent, label, label, action); var size = button.GetComponent<LayoutElement>(); size.minHeight = size.preferredHeight = 52;
            var text = button.GetComponentInChildren<Text>(); text.alignment = TextAnchor.MiddleLeft; text.rectTransform.offsetMin = new Vector2(12, 4); text.rectTransform.offsetMax = new Vector2(-12, -4);
            button.interactable = enabled; return button;
        }
        private void Separator(Transform parent) {
            var node = BrowserSampleWidgets.Node(parent, "Separator", typeof(Image), typeof(LayoutElement)); node.GetComponent<Image>().raycastTarget = false;
            node.GetComponent<Image>().color = new Color(.2f, .26f, .34f); var size = node.GetComponent<LayoutElement>(); size.minHeight = size.preferredHeight = 2;
        }
        private void Heading(Transform parent, string text) {
            var row = Row(parent, "Menu heading", 44); Label(row, "Heading", text, 32, 22);
            Button(row, "Close menu", "×", popup.Close, 44);
        }
        private void CloseThen(Action action) { popup.Close(); action(); }
        public void Toggle() { if (popup.IsOpen) popup.Close(); else ShowMenu(); }
        public void ShowMenu() {
            var body = popup.Open("Browser menu", menuAnchor, 350); zoomLabel = tiltLabel = null;
            Item(body, "New tab", () => CloseThen(() => app.NewTab(window.WindowId)));
            Item(body, "New window", () => CloseThen(app.CreateWindow)); Separator(body);
            Item(body, "Bookmarks  ›", () => ShowLibrary("Bookmarks")); Item(body, "History  ›", () => ShowLibrary("History")); Separator(body);
            var zoom = Row(body, "Text size", 52); Label(zoom, "Text size label", "Text size", 32);
            Button(zoom, "Smaller text", "−", () => app.Zoom(window.WindowId, -10), 44).interactable = HasContent;
            zoomLabel = Label(zoom, "Text size value", app.TextZoom(window.WindowId) + "%", 32); zoomLabel.alignment = TextAnchor.MiddleCenter;
            Button(zoom, "Larger text", "+", () => app.Zoom(window.WindowId, 10), 44).interactable = HasContent;
            Item(body, "Find in page", ShowFind, HasContent); Separator(body);
            Item(body, "Recall windows", () => CloseThen(app.RecallWindows)); Item(body, "Settings  ›", ShowSettings);
            popup.Reflow();
        }
        private void ShowSettings() {
            var body = popup.Open("Settings", menuAnchor, 370); Heading(body, "Settings");
            var tilt = Row(body, "Window tilt", 52); Label(tilt, "Tilt label", "Window tilt", 32);
            Button(tilt, "Less tilt", "−", () => app.ChangeTilt(-1), 44);
            tiltLabel = Label(tilt, "Tilt value", app.fixedTiltDegrees.ToString("0") + "°", 32); tiltLabel.alignment = TextAnchor.MiddleCenter;
            Button(tilt, "More tilt", "+", () => app.ChangeTilt(1), 44); Separator(body);
            Item(body, "Clear browsing history…", () => Confirm("Clear browsing history?", app.ClearHistory));
            Item(body, "Forget saved session…", () => Confirm("Forget saved tabs and placement?", app.ForgetSession));
            if (!string.IsNullOrEmpty(app.StorageWarning)) Label(body, "Storage warning", app.StorageWarning, 84, 18);
            popup.Reflow();
        }
        private void Confirm(string question, Action action) {
            var body = popup.Open("Confirm data action", menuAnchor, 370); Label(body, "Question", question, 70, 22);
            Label(body, "Data action explanation", "Bookmarks and site cookies are retained.", 52, 18);
            var row = Row(body, "Confirm actions", 52); Button(row, "Cancel", "Cancel", popup.Close); Button(row, "Confirm", "Confirm", () => CloseThen(action)); popup.Reflow();
        }
        private Button SavedItem(Transform parent, BrowserSavedPage saved, Action open) {
            var row = Row(parent, "Saved page", 66); var button = Button(row, "Open saved page", "", open);
            BrowserSampleWidgets.Remove(button.GetComponentInChildren<Text>().gameObject);
            var layout = button.gameObject.AddComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(12, 12, 5, 5);
            layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
            string host = Uri.TryCreate(saved.address, UriKind.Absolute, out var uri) ? uri.Host : "";
            string title = string.IsNullOrWhiteSpace(saved.title) ? host : saved.title;
            if (title.Length > 60) title = title.Substring(0, 59) + "…";
            Label(button.transform, "Saved title", title, 28, 20);
            Label(button.transform, "Saved domain", host, 22, 16).color = new Color(.65f, .74f, .84f);
            button.interactable = Ready;
            return Button(row, "Saved page actions", "⋯", () => ShowSavedActions(saved, "Bookmarks"), 48);
        }
        public void ShowLibrary(string kind) {
            var body = popup.Open(kind, menuAnchor, 400); Heading(body, kind);
            if (kind == "History") { Item(body, "Recently closed tabs  ›", () => ShowLibrary("Closed tabs")); Separator(body); }
            IList<BrowserSavedPage> entries = kind == "History" ? app.Library.Data.history : kind == "Closed tabs" ? app.Library.Data.recentlyClosed : app.Library.Data.bookmarks;
            if (entries.Count == 0) Label(body, "Empty library", "No entries yet.", 54);
            foreach (var saved in entries.ToArray()) {
                var more = SavedItem(body, saved, () => CloseThen(() => { if (kind == "Closed tabs") app.ReopenSavedClosed(window.WindowId, saved); else app.OpenSavedInCurrent(window.WindowId, saved); }));
                more.onClick.RemoveAllListeners(); more.onClick.AddListener(() => ShowSavedActions(saved, kind));
            }
            popup.Reflow();
        }
        private void ShowSavedActions(BrowserSavedPage saved, string kind) {
            var body = popup.Open("Saved page actions", menuAnchor, 350);
            Label(body, "Saved action heading", string.IsNullOrWhiteSpace(saved.title) ? new Uri(saved.address).Host : saved.title, 54);
            Item(body, kind == "Closed tabs" ? "Restore tab" : "Open in current tab", () => CloseThen(() => { if (kind == "Closed tabs") app.ReopenSavedClosed(window.WindowId, saved); else app.OpenSavedInCurrent(window.WindowId, saved); }), Ready);
            if (kind == "Bookmarks") {
                Item(body, "Edit bookmark", () => EditBookmark(saved.address, menuAnchor));
                Item(body, "Remove bookmark", () => { app.RemoveBookmark(saved.address); ShowLibrary("Bookmarks"); });
            } else if (kind == "History") Item(body, "Remove from history", () => { app.RemoveHistory(saved); ShowLibrary("History"); });
            popup.Reflow();
        }
        public void BookmarkCurrent() {
            var saved = app.CurrentSavedPage(window.WindowId); if (saved == null || !BrowserLibrary.IsWebAddress(saved.address)) return;
            if (!app.Library.IsBookmarked(saved.address)) app.SaveBookmark(saved.address, saved.title);
            EditBookmark(saved.address, bookmarkAnchor);
        }
        private void EditBookmark(string address, RectTransform anchor) {
            var saved = app.Library.FindBookmark(address); if (saved == null) return;
            var body = popup.Open("Bookmark editor", anchor, 390); Heading(body, "Bookmark");
            Label(body, "Bookmark domain", new Uri(saved.address).Host, 30, 18).color = new Color(.65f, .74f, .84f);
            var row = Row(body, "Bookmark name row", 52);
            var name = BrowserSampleWidgets.Input(row, "Bookmark name", font, "Name", 160); name.characterLimit = 256; name.SetTextWithoutNotify(saved.title);
            name.PrepareEditing = () => app.PrepareAddress(window.WindowId); name.EditingFailed += window.ShowMessage;
            name.Submitted += () => { name.DeactivateInputField(); app.SaveBookmark(address, name.text); };
            var buttons = Row(body, "Bookmark editor actions", 52);
            Button(buttons, "Delete bookmark", "Delete", () => { app.RemoveBookmark(address); popup.Close(); });
            Button(buttons, "Save bookmark", "Done", () => { app.SaveBookmark(address, name.text); popup.Close(); }); popup.Reflow();
        }
        public void ShowContext(BrowserContextTarget target) {
            var body = popup.OpenAt("Link actions", pagePress.WorldPoint, 350);
            Label(body, "Context target", new Uri(target.Address).Host, 30, 20);
            Label(body, "Context address", target.Address.Length > 90 ? target.Address.Substring(0, 89) + "…" : target.Address, 50, 16).color = new Color(.65f, .74f, .84f);
            Separator(body);
            Item(body, target.IsImage ? "Open image in new tab" : "Open link in new tab", () => CloseThen(() => app.OpenPage(window.WindowId, new BrowserSavedPage(target.Address, ""))));
            Item(body, target.IsImage ? "Copy image address" : "Copy link address", () => CloseThen(() => { GUIUtility.systemCopyBuffer = target.Address; window.ShowMessage("Address copied"); })); popup.Reflow();
        }
        private void BuildHome() {
            home = BrowserSampleWidgets.Node(pageRect, "New tab home", typeof(Image), typeof(RectMask2D), typeof(ScrollRect)); BrowserSampleWidgets.Stretch((RectTransform)home.transform);
            home.GetComponent<Image>().color = BrowserSampleWidgets.Background;
            var body = BrowserSampleWidgets.Node(home.transform, "New tab content", typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var rect = (RectTransform)body.transform; rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one; rect.pivot = new Vector2(.5f, 1); rect.sizeDelta = new Vector2(-18, 0); rect.anchoredPosition = new Vector2(-9, 0);
            var layout = body.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(32, 32, 36, 24); layout.spacing = 12;
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = home.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)home.transform; scroll.content = rect; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.verticalScrollbar = BrowserSampleWidgets.VerticalScrollbar(home.transform); scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            homeContent = body.transform;
        }
        private void RenderHome() {
            string signature = string.Join("\n", app.Library.Data.bookmarks.Take(6).Select(p => p.address + "|" + p.title));
            if (signature != homeSignature) {
                homeSignature = signature; homeButtons.Clear();
                for (int i = homeContent.childCount - 1; i >= 0; i--) BrowserSampleWidgets.Remove(homeContent.GetChild(i).gameObject);
                Label(homeContent, "New tab heading", "New tab", 52, 34);
                Label(homeContent, "New tab hint", "Search or enter an address in the bar above.", 44, 23);
                if (app.Library.Data.bookmarks.Count == 0) Label(homeContent, "Bookmark hint", "Use ☆ to save a page here.", 44, 20);
                else {
                    Label(homeContent, "Home bookmarks heading", "Bookmarks", 34, 23);
                    var pages = app.Library.Data.bookmarks.Take(6).ToArray();
                    for (int i = 0; i < pages.Length; i += 2) {
                        var row = Row(homeContent, "Bookmark shortcuts", 64);
                        foreach (var saved in pages.Skip(i).Take(2)) {
                            string title = string.IsNullOrWhiteSpace(saved.title) ? new Uri(saved.address).Host : saved.title;
                            if (title.Length > 38) title = title.Substring(0, 37) + "…";
                            homeButtons.Add(Button(row, "Bookmark shortcut", title, () => app.OpenSavedInCurrent(window.WindowId, saved)));
                        }
                    }
                }
            }
            foreach (var button in homeButtons) button.interactable = Ready;
        }
        private void BuildFind() {
            findRow = Row(pageRect, "Find in page", 56).gameObject; findRow.AddComponent<Image>().color = BrowserSampleWidgets.Background;
            var rect = (RectTransform)findRow.transform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one; rect.anchoredPosition = new Vector2(-12, -12); rect.sizeDelta = new Vector2(540, 56);
            var layout = findRow.GetComponent<HorizontalLayoutGroup>(); layout.padding = new RectOffset(6, 6, 6, 6);
            findInput = BrowserSampleWidgets.Input(findRow.transform, "Find text", font, "Find in page", 120); findInput.characterLimit = 4096;
            findInput.PrepareEditing = () => app.PrepareAddress(window.WindowId); findInput.EditingFailed += window.ShowMessage; findInput.Submitted += SubmitFind;
            Button(findRow.transform, "Find", "Find", SubmitFind, 60);
            count = Label(findRow.transform, "Matches", "", 32, 18); var countSize = count.GetComponent<LayoutElement>(); countSize.flexibleWidth = 0; countSize.minWidth = countSize.preferredWidth = 64;
            Button(findRow.transform, "Previous match", "↑", () => app.FindNext(window.WindowId, false), 40);
            Button(findRow.transform, "Next match", "↓", () => app.FindNext(window.WindowId, true), 40);
            Button(findRow.transform, "Close find", "×", CloseFind, 40); findRow.SetActive(false);
        }
        public void ShowFind() { popup.Close(); findRow.SetActive(true); findRow.transform.SetAsLastSibling(); }
        private async void SubmitFind() {
            findInput.DeactivateInputField(); count.text = "…"; long version = ++revision;
            try { await app.Find(window.WindowId, findInput.text, version); }
            catch (Exception) { if (version == revision && this != null) window.ShowMessage("Find could not be started."); }
        }
        public void Found(long source, BrowserFindResult result) { if (source == tab && result.Revision == revision && findRow.activeSelf) count.text = result.ActiveMatch + "/" + result.MatchCount; }
        private void CloseFind() {
            ++revision; if (findInput != null) findInput.DeactivateInputField();
            if (findRow != null && findRow.activeSelf) { findRow.SetActive(false); app.ClearFindForTab(tab); }
        }
        public void CloseMenu() { popup?.Close(); }
        public void NavigationStarted() { popup?.Close(); CloseFind(); pagePress?.Clear(); }
        public void Render(long current, bool blank, bool loading, string address) {
            if (tab != current) { NavigationStarted(); tab = current; wasLoading = false; lastAddress = address; }
            if (!string.IsNullOrEmpty(lastAddress) && lastAddress != address) NavigationStarted(); lastAddress = address;
            if (loading && !wasLoading) NavigationStarted(); wasLoading = loading;
            home.SetActive(blank); if (blank) RenderHome();
            if (zoomLabel != null) zoomLabel.text = app.TextZoom(window.WindowId) + "%";
            if (tiltLabel != null) tiltLabel.text = app.fixedTiltDegrees.ToString("0") + "°";
            ((RectTransform)findRow.transform).sizeDelta = new Vector2(Mathf.Min(540, pageRect.rect.width - 24), 56);
        }
        private void OnDisable() { popup?.Close(); CloseFind(); }
    }
}
