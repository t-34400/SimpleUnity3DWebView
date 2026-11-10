using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
namespace T34400.VRWebView.Samples.Browser.Tests
{
    public sealed class BrowserChromeUiTests {
        private GameObject root;
        private BrowserDesktopPanel panel;
        private BrowserDispatcher owner;
        private BrowserWindowView view;
        private string directory;
        [SetUp] public void SetUp() {
            directory = Path.Combine(Path.GetTempPath(), "vrwebview-chrome-ui-" + Guid.NewGuid().ToString("N"));
            root = new GameObject("Chrome fixture", typeof(RectTransform), typeof(Canvas), typeof(BrowserDispatcher), typeof(BrowserDesktopPanel), typeof(RestoreTestFactory));
            owner = root.GetComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread(); panel = root.GetComponent<BrowserDesktopPanel>(); panel.diagnostics = false;
            panel.SetStorage(new BrowserStateStore(Path.Combine(directory, "session.json"), text => JsonUtility.FromJson<BrowserSavedState>(text), data => JsonUtility.ToJson(data)));
            panel.SetFactory(root.GetComponent<RestoreTestFactory>());
            Call("Start"); Settle(() => panel.Model.Tabs[0].State == BrowserState.Ready && !panel.Model.Tabs[0].IsBusy);
            view = panel.WindowViews.Single(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)view.transform);
        }
        [TearDown] public void TearDown() { if (root != null) Object.DestroyImmediate(root); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private void Call(string method) => typeof(BrowserDesktopPanel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
        private void Settle(Func<bool> done) {
            var end = DateTime.UtcNow.AddSeconds(5);
            do { owner.PumpPending(); if (done()) return; Thread.Yield(); } while (DateTime.UtcNow < end);
            Assert.Fail("Owner queue did not settle");
        }
        private Button Button(string name) => view.GetComponentsInChildren<Button>(true).Single(b => b.gameObject.activeInHierarchy && b.name == name);
        [TestCase(800)] [TestCase(1280)]
        public void LongAddressesDoNotCompressButtonsOrChangeTheViewport(int width) {
            var rect = (RectTransform)view.transform; rect.sizeDelta = new Vector2(width, 1000);
            var address = view.GetComponentsInChildren<BrowserAddressInput>(true).Single(i => i.name == "Address");
            foreach (string value in new[] { "https://example.org/", "https://example.org/" + new string('a', 7000), new string('日', 1000) }) {
                address.SetTextWithoutNotify(value); LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                foreach (string name in new[] { "Back", "Forward", "Reload", "Go", "Bookmark page", "Browser menu" }) {
                    var button = Button(name); float expected = name == "Go" ? 65 : 52;
                    Assert.AreEqual(expected, ((RectTransform)button.transform).rect.width, .1f, name);
                }
                Assert.GreaterOrEqual(((RectTransform)address.transform).rect.width, 160);
                Assert.IsNotNull(address.textComponent.GetComponentInParent<RectMask2D>());
                Assert.AreEqual(value, address.text, "Full address was truncated");
            }
        }
        [Test] public void BookmarkStarReopensEditorAndNameChangesPersistWithoutRemovingTheBookmark() {
            panel.EditAddress(view.WindowId, "https://example.org/"); panel.Navigate(view.WindowId); Settle(() => !panel.Model.Tabs[0].IsBusy);
            Button("Bookmark page").onClick.Invoke(); Assert.AreEqual(1, panel.Library.Data.bookmarks.Count);
            view.CloseMenu(); Button("Bookmark page").onClick.Invoke(); Assert.AreEqual(1, panel.Library.Data.bookmarks.Count);
            var name = view.GetComponentsInChildren<BrowserAddressInput>(true).Single(i => i.name == "Bookmark name" && i.gameObject.activeInHierarchy);
            name.SetTextWithoutNotify("Renamed bookmark"); Button("Save bookmark").onClick.Invoke();
            Assert.AreEqual("Renamed bookmark", panel.Library.Data.bookmarks.Single().title);
            Assert.IsFalse(view.Tools.MenuOpen);
            Button("Bookmark page").onClick.Invoke(); Button("Delete bookmark").onClick.Invoke(); Assert.AreEqual(0, panel.Library.Data.bookmarks.Count);
        }
        [Test] public void LibraryScrollsWithoutPaginationAndContextMenuOnlyShowsTargetActions() {
            for (int i = 0; i < 15; i++) panel.SaveBookmark("https://example.org/" + i, "Page " + i);
            view.Tools.ShowLibrary("Bookmarks"); Assert.IsTrue(view.Tools.Popups.Card.GetComponent<ScrollRect>().vertical);
            Assert.Greater(view.GetComponentsInChildren<Button>().Count(b => b.name == "Open saved page"), 7);
            Assert.IsFalse(view.GetComponentsInChildren<Button>().Any(b => b.name == "Previous" || b.name == "Next"));
            view.ShowContext(new BrowserContextTarget("https://example.org/link", false));
            var labels = view.Tools.Popups.Card.GetComponentsInChildren<Button>().Select(b => b.name).ToArray();
            CollectionAssert.AreEquivalent(new[] { "Open link in new tab", "Copy link address" }, labels);
            Assert.Less(view.Tools.Popups.Card.rect.height, 300);
        }
        [Test] public void OutsidePressClosesPopupWithoutRemainingEligibleForClick() {
            view.Tools.ShowMenu(); var dismiss = view.GetComponentInChildren<BrowserPopupDismiss>();
            var pointer = new PointerEventData(null) { eligibleForClick = true };
            dismiss.OnPointerDown(pointer); Assert.IsFalse(view.Tools.MenuOpen); Assert.IsFalse(pointer.eligibleForClick); Assert.IsTrue(pointer.used);
        }
        [Test] public void YawPoseNormalizesRestoredAndRecalledWindowsAndSavesFixedTilt() {
            view.transform.rotation = Quaternion.Euler(24, 37, 18); view.NormalizePose();
            Assert.Less(Quaternion.Angle(Quaternion.Euler(5, 37, 0), view.transform.rotation), .01f);
            panel.ChangeTilt(-5); Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 37, 0), view.transform.rotation), .01f);
            Call("SaveState"); var store = new BrowserStateStore(Path.Combine(directory, "session.json"), text => JsonUtility.FromJson<BrowserSavedState>(text), data => JsonUtility.ToJson(data));
            var saved = store.Load(); Assert.AreEqual(1, saved.poseVersion); Assert.AreEqual(0, saved.fixedTiltDegrees);
            var camera = new GameObject("Tilted viewer", typeof(Camera));
            try { panel.worldCamera = camera.GetComponent<Camera>(); camera.transform.rotation = Quaternion.Euler(30, 60, 20); panel.RecallWindows();
                Assert.Less(Quaternion.Angle(Quaternion.Euler(0, 60, 0), view.transform.rotation), .01f);
            } finally { Object.DestroyImmediate(camera); }
        }
        [Test] public void PopupRemainsInsideTheWindowAndFollowsItsPose() {
            view.Tools.ShowMenu(); var popup = view.Tools.Popups.Card; var rect = (RectTransform)view.transform;
            Vector3 before = popup.position; Vector3 delta = new Vector3(1, 2, 3); view.transform.position += delta;
            Assert.Less(Vector3.Distance(before + delta, popup.position), .0001f);
            Vector3[] corners = new Vector3[4]; popup.GetWorldCorners(corners);
            foreach (var corner in corners) Assert.IsTrue(rect.rect.Contains(rect.InverseTransformPoint(corner)));
        }
    }
}
