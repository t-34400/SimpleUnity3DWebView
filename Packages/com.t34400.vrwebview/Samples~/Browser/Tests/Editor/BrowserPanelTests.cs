using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace T34400.VRWebView.Samples.Browser.Tests
{
    // Imported sample tests only. The fake backend is never a default factory
    // and this Editor-only test assembly is not part of a normal player.
    public sealed class BrowserPanelTests
    {
        private GameObject canvas;
        private BrowserPanel panel;
        private BrowserDispatcher owner;
        private readonly List<Backend> backends = new List<Backend>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private sealed class Backend : IBrowserBackend, IBrowserPointerSink, IBrowserEditingSink
        {
            internal int Navigations, Disposals, EditingEnds;
            internal Task Navigation = Task.CompletedTask;
            public Task InitializeAsync(CancellationToken token) { return Task.CompletedTask; }
            public Task NavigateAsync(Uri address, CancellationToken token) { Navigations++; return Navigation; }
            public void SendPointer(BrowserPointerEvent value) { }
            public void EndEditing() { EditingEnds++; }
            public void Dispose() { Disposals++; }
        }
        private sealed class PointerAdapter : IBrowserPointerSink
        {
            private readonly Backend backend;
            internal PointerAdapter(Backend backend) { this.backend = backend; }
            public void SendPointer(BrowserPointerEvent value) { backend.SendPointer(value); }
        }
        [SetUp] public void Setup()
        {
            canvas = new GameObject("Canvas", typeof(Canvas)); canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var host = new GameObject("Browser", typeof(RectTransform), typeof(BrowserPanelBuilder)); host.transform.SetParent(canvas.transform, false);
            host.GetComponent<RectTransform>().sizeDelta = new Vector2(1280, 850);
            panel = host.GetComponent<BrowserPanelBuilder>().Build();
            owner = canvas.GetComponentInChildren<BrowserDispatcher>();
        }
        [TearDown] public void Cleanup()
        {
            if (panel != null) panel.Configure(null);
            if (owner != null) owner.Close();
            if (canvas != null) Object.DestroyImmediate(canvas);
            foreach (var texture in textures) if (texture != null) Object.DestroyImmediate(texture);
            textures.Clear(); backends.Clear();
        }
        private BrowserPage Create(BrowserDispatcher dispatcher)
        {
            var backend = new Backend(); var source = new BrowserTextureSource(dispatcher); var texture = new Texture2D(2, 2);
            backends.Add(backend); textures.Add(texture); source.Publish(texture);
            return new BrowserPage(backend, source, new PointerAdapter(backend), source);
        }
        private InputField Address => canvas.GetComponentInChildren<InputField>();
        private Button Button(string name) { return canvas.GetComponentsInChildren<Button>().Single(value => value.name == name); }
        private void Settle(Func<bool> finished)
        {
            var end = DateTime.UtcNow.AddSeconds(5);
            do { owner.PumpPending(); if (finished()) return; Thread.Yield(); } while (DateTime.UtcNow < end);
            Assert.Fail("Sample operation did not settle");
        }
        private void NewReadyTab()
        {
            panel.NewTab(); Settle(() => panel.ActiveTab != null && panel.ActiveTab.State == BrowserState.Ready && !panel.ActiveTab.IsBusy);
        }

        [Test] public void BuilderIsIdempotentAndUnconfiguredUIHasNoPage()
        {
            var builder = canvas.GetComponentInChildren<BrowserPanelBuilder>(); Assert.AreSame(panel, builder.Build());
            Assert.AreEqual(0, panel.TabCount); Assert.IsFalse(Button("New tab").interactable); Assert.IsFalse(Button("Navigate").interactable);
            Assert.IsFalse(canvas.GetComponentInChildren<BrowserView>().IsConfigured); StringAssert.Contains("not configured", panel.StatusText);
        }
        [Test] public void TabSwitchRetainsBackgroundPageAndCloseDisposesOnlyOne()
        {
            panel.Configure(Create); NewReadyTab(); long first = panel.ActiveTab.Id; NewReadyTab(); long second = panel.ActiveTab.Id;
            Button("Tab-" + first).onClick.Invoke(); Assert.AreEqual(first, panel.ActiveTab.Id);
            Assert.AreSame(textures[0], canvas.GetComponentInChildren<RawImage>().texture); Assert.IsTrue(backends.All(value => value.Disposals == 0));
            panel.CloseActiveTab(); Assert.AreEqual(second, panel.ActiveTab.Id); Assert.AreEqual(1, backends[0].Disposals); Assert.AreEqual(0, backends[1].Disposals);
        }
        [Test] public void URLDraftSurvivesTabSwitchAndBlurDoesNotSubmit()
        {
            panel.Configure(Create); NewReadyTab(); long first = panel.ActiveTab.Id;
            Address.text = "https://example.invalid/typed"; Address.onEndEdit.Invoke(Address.text);
            Assert.AreEqual(0, backends[0].Navigations); NewReadyTab(); panel.SelectTab(first);
            Assert.AreEqual("https://example.invalid/typed", Address.text); Button("Navigate").onClick.Invoke();
            Assert.AreEqual(1, backends[0].Navigations);
        }
        [Test] public void PendingNavigationDoesNotOverwriteMoreRecentTyping()
        {
            panel.Configure(Create); NewReadyTab(); var finish = new TaskCompletionSource<bool>(); backends[0].Navigation = finish.Task;
            Address.text = "https://example.invalid/submitted"; panel.NavigateActive(); Address.text = "https://example.invalid/new-draft";
            finish.SetResult(true); Settle(() => !panel.ActiveTab.IsBusy);
            owner.PumpPending(); Assert.AreEqual("https://example.invalid/new-draft", Address.text);
        }
        [Test] public void OldScopeReplyCannotUpdateANewTabWithTheSameId()
        {
            panel.Configure(Create); NewReadyTab(); long oldId = panel.ActiveTab.Id; var finish = new TaskCompletionSource<bool>(); backends[0].Navigation = finish.Task;
            Address.text = "https://example.invalid/old"; panel.NavigateActive(); panel.Configure(Create); NewReadyTab();
            Assert.AreEqual(oldId, panel.ActiveTab.Id); Address.text = "https://example.invalid/current"; finish.SetResult(true); owner.PumpPending();
            Assert.AreEqual("https://example.invalid/current", Address.text); Assert.AreEqual(1, backends[0].Disposals);
        }
        [Test] public void NewTabPrefillDoesNotNavigateOrOverwriteAnotherDraft()
        {
            panel.SetNewTabAddress("https://example.com/"); panel.Configure(Create); NewReadyTab(); long first = panel.ActiveTab.Id;
            Assert.AreEqual("https://example.com/", Address.text); Assert.AreEqual(0, backends[0].Navigations);
            Address.text = "https://example.org/draft"; NewReadyTab();
            Assert.AreEqual("https://example.com/", Address.text); panel.SelectTab(first);
            Assert.AreEqual("https://example.org/draft", Address.text);
        }
        [Test] public void TabSwitchAndAddressSelectionEndThePreviousWebEditor()
        {
            panel.Configure(Create); NewReadyTab(); int before = backends[0].EditingEnds;
            NewReadyTab(); Assert.AreEqual(before + 1, backends[0].EditingEnds);
            int secondBefore = backends[1].EditingEnds;
            Address.GetComponent<BrowserAddressFocus>().OnSelect(null);
            Assert.AreEqual(secondBefore + 1, backends[1].EditingEnds);
            panel.enabled = false; Assert.AreEqual(secondBefore + 2, backends[1].EditingEnds);
        }
        [Test] public void PageFactoryFailureShowsFailureWithoutCreatingAFakeTab()
        {
            panel.Configure(_ => throw new InvalidOperationException("synthetic page creation failure")); panel.NewTab();
            Assert.AreEqual(0, panel.TabCount); Assert.IsNotNull(panel.LastError); StringAssert.Contains("Could not create", panel.StatusText);
        }
    }
}
