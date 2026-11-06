using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace T34400.VRWebView.Tests
{
    public class PointerTests
    {
        [Test]
        public void OverlayCoordinatesAndDisableCancelCapturedPointer()
        {
            var events = new GameObject("Events", typeof(EventSystem));
            var root = new GameObject("Canvas", typeof(Canvas));
            var view = new GameObject("Surface", typeof(RectTransform), typeof(BrowserPointerSource));
            try
            {
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                view.transform.SetParent(root.transform, false);
                var rect = view.GetComponent<RectTransform>();
                rect.pivot = Vector2.zero; rect.sizeDelta = new Vector2(200, 100);
                var source = view.GetComponent<BrowserPointerSource>();
                int canceled = 0;
                source.Capture.Received += e => { if (e.Phase == PointerPhase.Cancel) canceled++; };
                var data = new PointerEventData(events.GetComponent<EventSystem>())
                { pointerId = 7, position = RectTransformUtility.WorldToScreenPoint(null, rect.position) };
                source.OnInitializePotentialDrag(data);
                Assert.IsFalse(data.useDragThreshold);
                source.OnPointerDown(data);
                view.SetActive(false);
                Assert.AreEqual(1, canceled);
            }
            finally { Object.DestroyImmediate(view); Object.DestroyImmediate(root); Object.DestroyImmediate(events); }
        }

        [Test]
        public void ForeignPointerDoesNotReleaseOwner()
        {
            var capture = new PointerCapture();
            Assert.IsTrue(capture.Send(1, PointerPhase.Down, .5f, .5f));
            Assert.IsFalse(capture.Send(2, PointerPhase.Up, .5f, .5f));
            Assert.IsTrue(capture.Send(1, PointerPhase.Up, .5f, .5f));
        }
    }
}
