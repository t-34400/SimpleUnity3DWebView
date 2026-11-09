using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace T34400.VRWebView.Tests
{
    public sealed class ProbeCanvasInputTests
    {
        private GameObject canvas, camera, events;
        private ProbeCanvasInputModule module;
        private EventSystem previous;
        [SetUp] public void Setup()
        {
            previous = EventSystem.current; if (previous != null) previous.enabled = false;
            camera = new GameObject("Ray test camera", typeof(Camera));
            camera.GetComponent<Camera>().nearClipPlane = .01f;
            canvas = new GameObject("Ray test canvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            canvas.transform.position = new Vector3(0, 0, 2); canvas.transform.localScale = Vector3.one * .01f;
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(200, 200);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvas.GetComponent<Canvas>().worldCamera = camera.GetComponent<Camera>();
            canvas.GetComponent<GraphicRaycaster>().ignoreReversedGraphics = false;
            events = new GameObject("Ray test events", typeof(EventSystem), typeof(ProbeCanvasInputModule));
            module = events.GetComponent<ProbeCanvasInputModule>(); module.enabled = false; // Tests feed explicit rays, no hardware polling.
            module.targetCanvas = canvas.GetComponent<Canvas>(); module.headCamera = camera.GetComponent<Camera>();
        }
        [TearDown] public void Cleanup()
        {
            if (events != null) Object.DestroyImmediate(events);
            if (canvas != null) Object.DestroyImmediate(canvas);
            if (camera != null) Object.DestroyImmediate(camera);
            if (previous != null) previous.enabled = true;
        }
        private GameObject Target(params System.Type[] types)
        {
            var target = new GameObject("Ray target", typeof(RectTransform)); target.transform.SetParent(canvas.transform, false);
            target.GetComponent<RectTransform>().sizeDelta = new Vector2(100, 100);
            foreach (var type in types) target.AddComponent(type);
            return target;
        }
        private void Send(bool tracked, bool pressed, float x = 0)
        {
            module.ProcessRay(tracked, new Ray(Vector3.zero, new Vector3(x, 0, 2)), pressed, 1, Vector2.zero);
        }
        [UnityTest] public IEnumerator RayButtonClicksOnceAndCancelledPinchDoesNotClick()
        {
            var button = Target(typeof(Image), typeof(Button)).GetComponent<Button>(); int clicks = 0; button.onClick.AddListener(() => clicks++);
            Canvas.ForceUpdateCanvases(); yield return null;
            Send(true, false); Send(true, true); Send(true, false); Assert.AreEqual(1, clicks);
            Send(true, true); module.CancelPointer(); Send(true, false); Assert.AreEqual(1, clicks);
        }
        [UnityTest] public IEnumerator RayViewGetsHoverAndCancelInsteadOfAnUpOnFocusLoss()
        {
            var source = Target(typeof(RawImage), typeof(BrowserPointerSource)).GetComponent<BrowserPointerSource>();
            var touches = new List<PointerPhase>(); var hover = new List<HoverPhase>();
            source.Capture.Received += value => touches.Add(value.Phase); source.Hover.Received += value => hover.Add(value.Phase);
            Canvas.ForceUpdateCanvases(); yield return null;
            source.SendMessage("OnApplicationFocus", true); // Explicit test input permission, independent of Editor window focus.
            Send(true, false); Assert.Contains(HoverPhase.Enter, hover);
            Send(true, true); Send(false, false);
            CollectionAssert.AreEqual(new[] {PointerPhase.Down, PointerPhase.Cancel}, touches);
            Assert.AreEqual(HoverPhase.Exit, hover.Last());
            Send(true, true); Assert.AreEqual(2, touches.Count); // Held pinch cannot become a new click on recovery.
            Send(true, false); Send(true, true); Send(true, false);
            CollectionAssert.AreEqual(new[] {PointerPhase.Down, PointerPhase.Cancel, PointerPhase.Down, PointerPhase.Up}, touches);
        }
    }
}
