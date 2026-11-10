using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
namespace T34400.VRWebView.Samples.Browser
{
    /// <summary>Transient sample chrome; owns no pages and does not serialize editing state.</summary>
    public sealed class BrowserWindowTools : MonoBehaviour {
        private BrowserDesktopPanel app;
        private BrowserWindowView window;
        private Font font;
        private GameObject overlay, findRow, findContainer;
        private Transform list;
        private Text heading, count;
        private readonly List<Button> contentButtons = new List<Button>();
        private readonly List<Button> savedPageButtons = new List<Button>();
        private BrowserAddressInput findInput;
        private string section;
        private int offset;
        private long tab = -1, revision;
        private bool wasBlank;
        private BrowserContextTarget context;
        private const int PageSize = 7;
        public void Build(BrowserDesktopPanel application, BrowserWindowView view, Transform page, Transform toolbar, Font typeface) {
            app = application; window = view; font = typeface; findContainer = toolbar.gameObject;
            findRow = Row(toolbar, "Find in page", 52).gameObject;
            var node = BrowserSampleWidgets.Node(findRow.transform, "Search text", typeof(Image), typeof(BrowserAddressInput), typeof(LayoutElement));
            node.GetComponent<Image>().color = BrowserSampleWidgets.ButtonColor; node.GetComponent<LayoutElement>().flexibleWidth = 1;
            findInput = node.GetComponent<BrowserAddressInput>(); findInput.targetGraphic = node.GetComponent<Image>();
            findInput.textComponent = BrowserSampleWidgets.Label(node.transform, "Text", "", font); BrowserSampleWidgets.Stretch(findInput.textComponent.rectTransform, 8);
            findInput.keyboardType = TouchScreenKeyboardType.Default; findInput.shouldHideMobileInput = false; findInput.lineType = InputField.LineType.SingleLine;
            findInput.PrepareEditing = () => app.PrepareAddress(window.WindowId); findInput.EditingFailed += window.ShowMessage;
            findInput.Submitted += SubmitFind;
            Button(findRow.transform, "Find", SubmitFind, 70);
            count = BrowserSampleWidgets.Label(findRow.transform, "Matches", "", font); var countSize = count.gameObject.AddComponent<LayoutElement>(); countSize.preferredWidth = 90;
            Button(findRow.transform, "↑", () => app.FindNext(window.WindowId, false), 44);
            Button(findRow.transform, "↓", () => app.FindNext(window.WindowId, true), 44);
            Button(findRow.transform, "×", CloseFind, 44); findRow.SetActive(false); findContainer.SetActive(false);
            overlay = BrowserSampleWidgets.Node(page, "Browser library", typeof(Image), typeof(VerticalLayoutGroup));
            BrowserSampleWidgets.Stretch((RectTransform)overlay.transform); overlay.GetComponent<Image>().color = BrowserSampleWidgets.Background;
            var layout = overlay.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(16, 16, 16, 16); layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            var title = Row(overlay.transform, "Library heading", 44);
            heading = BrowserSampleWidgets.Label(title, "Heading", "Browser", font, 24); heading.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Button(title, "Close", () => overlay.SetActive(false), 80);
            var bodyViewport = BrowserSampleWidgets.Node(overlay.transform, "Library viewport", typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            bodyViewport.GetComponent<Image>().color = Color.clear; bodyViewport.GetComponent<LayoutElement>().flexibleHeight = 1;
            var body = BrowserSampleWidgets.Node(bodyViewport.transform, "Library content", typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var bodyRect = (RectTransform)body.transform; bodyRect.anchorMin = new Vector2(0, 1); bodyRect.anchorMax = Vector2.one; bodyRect.pivot = new Vector2(.5f, 1); bodyRect.sizeDelta = Vector2.zero;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bodyLayout = body.GetComponent<VerticalLayoutGroup>(); bodyLayout.spacing = 8; bodyLayout.childControlHeight = bodyLayout.childControlWidth = true; bodyLayout.childForceExpandHeight = false; bodyLayout.childForceExpandWidth = true;
            var scroll = bodyViewport.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)bodyViewport.transform; scroll.content = bodyRect; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            var first = Row(body.transform, "Library navigation", 48);
            FillButton(first, "Bookmarks", () => Show("Bookmarks")); FillButton(first, "History", () => Show("History")); FillButton(first, "Closed tabs", () => Show("Closed tabs"));
            var second = Row(body.transform, "Page tools", 48);
            ContentButton(second, "Text −", () => app.Zoom(window.WindowId, -10)); ContentButton(second, "Text +", () => app.Zoom(window.WindowId, 10));
            ContentButton(second, "Find", () => { overlay.SetActive(false); findContainer.SetActive(true); findRow.SetActive(true); });
            var third = Row(body.transform, "Workspace tools", 48);
            FillButton(third, "Recall windows", app.RecallWindows); FillButton(third, "Reopen tab", () => { app.ReopenClosed(window.WindowId); Show("Closed tabs"); });
            FillButton(third, "Forget session", () => { app.ForgetSession(); Show(section); });
            var area = BrowserSampleWidgets.Node(body.transform, "Library entries", typeof(VerticalLayoutGroup));
            var column = area.GetComponent<VerticalLayoutGroup>(); column.spacing = 6; column.childControlHeight = column.childControlWidth = true; column.childForceExpandWidth = true; column.childForceExpandHeight = false;
            list = area.transform;
            var footer = Row(body.transform, "Library paging", 44);
            FillButton(footer, "Previous", () => { offset = Math.Max(0, offset - PageSize); RenderList(); });
            FillButton(footer, "Next", () => { offset += PageSize; RenderList(); });
            FillButton(footer, "Clear history", () => { app.ClearHistory(); Show("History"); });
            overlay.SetActive(false);
        }
        private Transform Row(Transform parent, string name, int height) {
            var row = BrowserSampleWidgets.Node(parent, name, typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var size = row.GetComponent<LayoutElement>(); size.minHeight = size.preferredHeight = height; size.flexibleHeight = 0;
            var layout = row.GetComponent<HorizontalLayoutGroup>(); layout.spacing = 8; layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = true; layout.childForceExpandWidth = false;
            return row.transform;
        }
        private Button Button(Transform parent, string label, Action action, float width) {
            var button = BrowserSampleWidgets.Button(parent, label, label, font, width); button.gameObject.AddComponent<BrowserWindowActivation>().Window = window; button.onClick.AddListener(() => action()); return button;
        }
        private Button FillButton(Transform parent, string label, Action action) { var button = Button(parent, label, action, 0); button.GetComponent<LayoutElement>().flexibleWidth = 1; return button; }
        private void ContentButton(Transform parent, string label, Action action) {
            var button = Button(parent, label, action, 0); button.GetComponent<LayoutElement>().flexibleWidth = 1; contentButtons.Add(button);
        }
        public void Show(string name) { section = name ?? "Bookmarks"; offset = 0; context = null; overlay.SetActive(true); RenderList(); }
        public void NavigationStarted() { CloseFind(); overlay.SetActive(false); }
        public void Toggle() { if (overlay.activeSelf) overlay.SetActive(false); else Show("Bookmarks"); }
        public void ShowContext(BrowserContextTarget value) { context = value; section = value.IsImage ? "Image" : "Link"; overlay.SetActive(true); RenderList(); }
        private void RenderList() {
            for (int i = list.childCount - 1; i >= 0; i--) BrowserSampleWidgets.Remove(list.GetChild(i).gameObject);
            heading.text = section; savedPageButtons.Clear();
            if (context != null) {
                var target = context;
                FillButton(Row(list, "Context actions", 52), "Open in new tab", () => { overlay.SetActive(false); app.OpenPage(window.WindowId, new BrowserSavedPage(target.Address, "")); });
                FillButton(Row(list, "Copy target", 52), "Copy address", () => { GUIUtility.systemCopyBuffer = target.Address; window.ShowMessage("Address copied"); overlay.SetActive(false); });
                return;
            }
            var data = app.Library.Data;
            IList<BrowserSavedPage> pages = section == "History" ? data.history : section == "Closed tabs" ? data.recentlyClosed : data.bookmarks;
            offset = pages.Count == 0 ? 0 : Math.Min(offset, (pages.Count - 1) / PageSize * PageSize);
            heading.text = section + " (" + pages.Count + ")";
            if (pages.Count == 0) {
                var label = BrowserSampleWidgets.Label(list, "Empty", section == "Bookmarks" ? "Search or enter an address above. Use ☆ to bookmark a page." : "No entries yet.", font, 24);
                label.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
            }
            foreach (int index in Enumerable.Range(offset, Math.Min(PageSize, pages.Count - offset))) {
                var saved = pages[index]; var row = Row(list, "Saved page", 52);
                string title = string.IsNullOrWhiteSpace(saved.title) ? saved.address : saved.title;
                if (title.Length > 60) title = title.Substring(0, 59) + "…";
                savedPageButtons.Add(FillButton(row, title, () => { overlay.SetActive(false); if (section == "Closed tabs") app.OpenPage(window.WindowId, saved); else app.OpenSavedInCurrent(window.WindowId, saved); }));
                if (section != "Closed tabs") Button(row, "×", () => { app.RemoveSaved(section == "History", index); RenderList(); }, 44);
            }
            if (!string.IsNullOrEmpty(app.StorageWarning)) {
                var warning = BrowserSampleWidgets.Label(list, "Storage warning", app.StorageWarning, font, 18); warning.gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
            }
        }
        public void Render(long current, bool blank) {
            var active = app.Model?.FindTab(current);
            bool ready = active?.State == BrowserState.Ready && !active.IsBusy;
            bool capable = ready && app.GetEntry(current)?.Page.Content != null;
            foreach (var button in contentButtons) button.interactable = capable;
            if (tab != current) { CloseFind(); overlay.SetActive(false); context = null; tab = current; wasBlank = false; }
            if (!blank && wasBlank) overlay.SetActive(false);
            if (blank && !wasBlank) Show("Bookmarks"); wasBlank = blank;
            foreach (var button in savedPageButtons) button.interactable = ready;
        }
        private async void SubmitFind() {
            findInput.DeactivateInputField(); count.text = "…"; long version = ++revision;
            try { await app.Find(window.WindowId, findInput.text, version); }
            catch (Exception) { if (version == revision && this != null) window.ShowMessage("Find could not be started."); }
        }
        public void Found(long source, BrowserFindResult result) { if (tab == source && result.Revision == revision && findRow.activeSelf) count.text = result.ActiveMatch + "/" + result.MatchCount; }
        private void CloseFind() { ++revision; if (findInput != null) findInput.DeactivateInputField(); if (findRow != null && findRow.activeSelf) { findRow.SetActive(false); findContainer.SetActive(false); app.ClearFindForTab(tab); } }
        private void OnDisable() { CloseFind(); }
    }
}
