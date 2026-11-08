using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using T34400.VRWebView.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace T34400.VRWebView.Tests
{
    // Real Unity objects. These are authored tests, not executed by CoreTests.
    public sealed class BrowserViewTests
    {
        private GameObject dispatcherObject, canvasObject, viewObject, eventsObject;
        private BrowserDispatcher dispatcher;
        private BrowserView view;
        private BrowserPointerSource pointer;
        private RawImage image;
        private BrowserTextureSource source;
        private Texture2D first, second;
        private sealed class Sink : IBrowserPointerSink, IBrowserScrollSink
        {
            internal readonly List<BrowserPointerEvent> Points = new List<BrowserPointerEvent>();
            internal readonly List<BrowserScrollEvent> Scrolls = new List<BrowserScrollEvent>();
            public void SendPointer(BrowserPointerEvent value) { Points.Add(value); }
            public void SendScroll(BrowserScrollEvent value) { Scrolls.Add(value); }
        }

        [SetUp] public void Setup()
        {
            dispatcherObject = new GameObject("Browser dispatcher", typeof(BrowserDispatcher));
            dispatcher = dispatcherObject.GetComponent<BrowserDispatcher>(); dispatcher.InitializeOnOwnerThread();
            canvasObject = new GameObject("Canvas", typeof(Canvas)); canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            viewObject = new GameObject("Browser view", typeof(RectTransform), typeof(RawImage), typeof(BrowserPointerSource), typeof(BrowserView));
            viewObject.transform.SetParent(canvasObject.transform, false);
            var rect = viewObject.GetComponent<RectTransform>(); rect.pivot = Vector2.zero; rect.sizeDelta = new Vector2(100, 100);
            view = viewObject.GetComponent<BrowserView>(); pointer = viewObject.GetComponent<BrowserPointerSource>(); image = viewObject.GetComponent<RawImage>();
            source = new BrowserTextureSource(dispatcher); first = new Texture2D(2, 2); second = new Texture2D(4, 4);
            eventsObject = new GameObject("Events", typeof(EventSystem));
        }
        [TearDown] public void Cleanup()
        {
            if (view != null) view.Unbind();
            if (dispatcher != null) dispatcher.Close();
            if (canvasObject != null) Object.DestroyImmediate(canvasObject);
            if (dispatcherObject != null) Object.DestroyImmediate(dispatcherObject);
            if (eventsObject != null) Object.DestroyImmediate(eventsObject);
            if (first != null) Object.DestroyImmediate(first);
            if (second != null) Object.DestroyImmediate(second);
        }
        private PointerEventData At(float x, float y)
        {
            var rect = viewObject.GetComponent<RectTransform>();
            return new PointerEventData(eventsObject.GetComponent<EventSystem>()) {
                pointerId = 7,
                position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(new Vector3(100 * x, 100 * (1 - y), 0)))
            };
        }

        [Test] public void WorkerPostWaitsForOwnerPump()
        {
            int calls = 0, threadId = Thread.CurrentThread.ManagedThreadId; var scheduler = dispatcher.Scheduler;
            var thread = new Thread(() => scheduler(() => { Assert.AreEqual(threadId, Thread.CurrentThread.ManagedThreadId); calls++; }));
            thread.Start(); Assert.IsTrue(thread.Join(5000)); Assert.AreEqual(0, calls);
            dispatcher.PumpPending(); Assert.AreEqual(1, calls);
        }
        [Test] public void SourcePublishesRealTextureAndViewOnlyBorrowsIt()
        {
            source.Publish(first); view.Bind(source, null, dispatcher);
            Assert.AreSame(first, image.texture); Assert.IsTrue(image.enabled); Assert.IsFalse(pointer.InputEnabled);
            view.Unbind(); Assert.IsNull(image.texture); Assert.IsFalse(image.enabled);
            Assert.IsTrue(first != null); Assert.AreSame(first, source.Current.Surface);
            source.Publish(second); Assert.IsNull(image.texture);
        }
        [TestCase(BrowserSurfaceOrigin.TopLeft)] [TestCase(BrowserSurfaceOrigin.BottomLeft)]
        public void PointerMatchesSurfaceOrigin(BrowserSurfaceOrigin origin)
        {
            var sink = new Sink(); source.Publish(first, origin); view.Bind(source, sink, dispatcher);
            pointer.OnPointerDown(At(.2f, .25f));
            Assert.AreEqual(1, sink.Points.Count); Assert.AreEqual(.2f, sink.Points[0].X, .0001f); Assert.AreEqual(.25f, sink.Points[0].Y, .0001f);
        }
        [Test] public void SourceReplacementCancelsOldTargetBeforeNewInput()
        {
            var old = new Sink(); var next = new Sink(); source.Publish(first); view.Bind(source, old, dispatcher);
            pointer.OnPointerDown(At(.2f, .3f)); view.Bind(source, next, dispatcher);
            Assert.AreEqual(2, old.Points.Count); Assert.AreEqual(PointerPhase.Cancel, old.Points[1].Phase);
            pointer.OnDrag(At(.4f, .5f)); Assert.AreEqual(0, next.Points.Count);
            pointer.OnPointerDown(At(.4f, .5f)); Assert.AreEqual(PointerPhase.Down, next.Points[0].Phase);
        }
        [Test] public void WithdrawalClearsSynchronouslyAndCancelsExactlyOnce()
        {
            var sink = new Sink(); source.Publish(first); view.Bind(source, sink, dispatcher); pointer.OnPointerDown(At(.2f, .3f));
            source.Withdraw(); Assert.IsNull(image.texture); Assert.IsFalse(pointer.InputEnabled);
            view.Unbind(); Assert.AreEqual(2, sink.Points.Count); Assert.AreEqual(PointerPhase.Cancel, sink.Points[1].Phase);
            Assert.IsTrue(first != null);
        }
        [Test] public void ScrollUsesDisplayedCropAndLogicalDeltas()
        {
            var sink = new Sink(); source.Publish(first); view.Bind(source, sink, dispatcher);
            image.uvRect = new Rect(.1f, .2f, .6f, .5f); var data = At(.5f, .2f); data.scrollDelta = new Vector2(-1, 2); pointer.OnScroll(data);
            Assert.AreEqual(1, sink.Scrolls.Count); Assert.AreEqual(.4f, sink.Scrolls[0].X, .0001f); Assert.AreEqual(.4f, sink.Scrolls[0].Y, .0001f);
            Assert.AreEqual(2, sink.Scrolls[0].DeltaY);
        }
        [Test] public void ClosingDispatcherClearsItsTrackedViewAndSource()
        {
            var sink = new Sink(); source.Publish(first); view.Bind(source, sink, dispatcher); pointer.OnPointerDown(At(.2f, .3f));
            var oldScheduler = dispatcher.Scheduler; dispatcher.Close();
            Assert.IsNull(image.texture); Assert.IsFalse(pointer.InputEnabled); Assert.IsFalse(source.Current.HasSurface);
            Assert.AreEqual(PointerPhase.Cancel, sink.Points[1].Phase); Assert.IsTrue(first != null);
            Assert.Throws<ObjectDisposedException>(() => oldScheduler(() => { }));
            dispatcher.InitializeOnOwnerThread(); int calls = 0; dispatcher.Scheduler(() => calls++); dispatcher.PumpPending(); Assert.AreEqual(1, calls);
        }
        [Test] public void TextureSourceRejectsWorkerPublicationBeforeAccessingTexture()
        {
            Exception failure = null;
            var thread = new Thread(() => { try { source.Publish(first); } catch (Exception error) { failure = error; } });
            thread.Start(); Assert.IsTrue(thread.Join(5000)); Assert.IsInstanceOf<InvalidOperationException>(failure); Assert.IsNull(source.Current);
        }
        [Test] public void InputPermissionSurvivesTextureReplacementUntilExplicitlyRestored()
        {
            var sink = new Sink(); source.Publish(first); view.Bind(source, sink, dispatcher); pointer.OnPointerDown(At(.2f, .3f));
            view.SetInteractionEnabled(false); source.Publish(second); pointer.OnPointerDown(At(.2f, .3f));
            Assert.AreEqual(2, sink.Points.Count); Assert.IsFalse(pointer.InputEnabled);
            view.SetInteractionEnabled(true); pointer.OnPointerDown(At(.2f, .3f)); Assert.AreEqual(3, sink.Points.Count);
        }
        [Test] public void PublishingTheSameSurfaceDoesNotCancelAnOngoingGesture()
        {
            var sink = new Sink(); source.Publish(first); view.Bind(source, sink, dispatcher); pointer.OnPointerDown(At(.2f, .3f));
            long revision = source.Current.Revision; source.Publish(first); Assert.AreEqual(revision, source.Current.Revision);
            pointer.OnDrag(At(.4f, .5f)); Assert.AreEqual(2, sink.Points.Count); Assert.AreEqual(PointerPhase.Move, sink.Points[1].Phase);
        }
        [Test] public void InactiveViewConfigurationStillBelongsToDispatcherScope()
        {
            viewObject.SetActive(false); source.Publish(first); view.Bind(source, null, dispatcher);
            Assert.IsTrue(view.IsConfigured); dispatcher.Close(); Assert.IsFalse(view.IsConfigured);
            dispatcher.InitializeOnOwnerThread(); viewObject.SetActive(true); Assert.IsNull(image.texture);
        }
    }
}
