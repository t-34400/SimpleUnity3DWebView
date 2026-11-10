using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
namespace T34400.VRWebView.Samples.Browser.Tests
{
    public sealed class RestoreTestFactory : BrowserPageFactory {
        public readonly List<RestoreTestBackend> Pages = new List<RestoreTestBackend>();
        public override BrowserPage CreatePage(BrowserDispatcher dispatcher) {
            var backend = new RestoreTestBackend(); Pages.Add(backend); var surface = new BrowserTextureSource(dispatcher);
            return new BrowserPage(backend, surface, connectionLifetime: surface);
        }
    }
    public sealed class RestoreTestBackend : IBrowserBackend, IBrowserNavigationBackend {
        public int Initializations, Navigations;
        public BrowserPageState PageState { get; private set; } = BrowserPageState.Empty;
        public event Action<BrowserPageState> PageStateChanged;
        public Task InitializeAsync(CancellationToken token) { Initializations++; return Task.CompletedTask; }
        public Task NavigateAsync(Uri uri, CancellationToken token) {
            Navigations++; PageState = new BrowserPageState(uri.AbsoluteUri, "Restored", false, false, false, 100, 0); PageStateChanged?.Invoke(PageState); return Task.CompletedTask;
        }
        public Task ExecuteAsync(BrowserPageCommand command, CancellationToken token) => Task.CompletedTask;
        public void Dispose() { }
    }
    public sealed class BrowserSessionRestoreTests {
        [Test] public void RestoredHiddenTabsStayColdUntilSelectedAndClosedTabsCanReopen() {
            string directory = Path.Combine(Path.GetTempPath(), "vrwebview-unity-session-" + Guid.NewGuid().ToString("N"));
            var store = new BrowserStateStore(Path.Combine(directory, "session.json"), text => JsonUtility.FromJson<BrowserSavedState>(text), state => JsonUtility.ToJson(state));
            var saved = new BrowserSavedState(); var first = new BrowserSavedWindow { active = 0, hasPlacement = true, x = 200 };
            first.tabs.Add(new BrowserSavedPage("https://example.org/one", "One")); first.tabs.Add(new BrowserSavedPage("https://example.org/two", "Two")); saved.windows.Add(first); Assert.IsTrue(store.Save(saved));
            var root = new GameObject("Restore fixture", typeof(RectTransform), typeof(Canvas), typeof(BrowserDispatcher), typeof(BrowserDesktopPanel), typeof(RestoreTestFactory));
            var owner = root.GetComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread();
            var panel = root.GetComponent<BrowserDesktopPanel>(); var factory = root.GetComponent<RestoreTestFactory>();
            try {
                panel.diagnostics = false; panel.SetStorage(store); panel.SetFactory(factory);
                Call(panel, "Start"); Settle(owner, () => factory.Pages[0].Navigations == 1 && panel.Model.Tabs[0].State == BrowserState.Ready);
                Assert.AreEqual(2, factory.Pages.Count); Assert.AreEqual(0, factory.Pages[1].Initializations);
                Assert.AreEqual(200, panel.WindowViews.Single().transform.localPosition.x);
                Settle(owner, () => !panel.Model.Tabs[0].IsBusy);
                panel.OpenSavedInCurrent(panel.Model.Windows[0].Id, new BrowserSavedPage("https://example.org/bookmark", "Bookmark"));
                Settle(owner, () => factory.Pages[0].Navigations == 2 && !panel.Model.Tabs[0].IsBusy);
                Assert.AreEqual(2, panel.Model.Tabs.Count, "Opening a bookmark must reuse the current tab");
                var window = panel.Model.Windows[0]; long second = window.Tabs[1]; panel.Select(window.Id, second);
                Settle(owner, () => factory.Pages[1].Navigations == 1 && !panel.Model.FindTab(second).IsBusy);
                Call(panel, "SaveState"); Assert.AreEqual(1, store.Load().windows[0].active);
                panel.CloseTab(second); panel.ReopenClosed(window.Id);
                Settle(owner, () => factory.Pages.Count == 3 && factory.Pages[2].Navigations == 1);
                Assert.AreEqual("https://example.org/two", panel.Model.FindTab(window.ActiveTabId.Value).RequestedAddress.AbsoluteUri);
            } finally { Object.DestroyImmediate(root); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
        private static void Call(BrowserDesktopPanel panel, string method) => typeof(BrowserDesktopPanel).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(panel, null);
        private static void Settle(BrowserDispatcher owner, Func<bool> done) {
            DateTime end = DateTime.UtcNow.AddSeconds(5);
            do { owner.PumpPending(); if (done()) return; Thread.Yield(); } while (DateTime.UtcNow < end);
            Assert.Fail("Owner queue did not settle");
        }
    }
}
