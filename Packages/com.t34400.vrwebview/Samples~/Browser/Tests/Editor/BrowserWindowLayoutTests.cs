using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace T34400.VRWebView.Samples.Browser.Tests
{
    public sealed class BrowserWindowLayoutTests
    {
        [Test] public void OnlyPageExpandsBeforeAndAfterTextureAssignmentAndResize() {
            var root = new GameObject("Application", typeof(RectTransform), typeof(Canvas), typeof(BrowserDispatcher), typeof(BrowserDesktopPanel));
            var window = new GameObject("Window", typeof(RectTransform), typeof(Canvas), typeof(BrowserWindowView)); window.transform.SetParent(root.transform, false);
            var owner = root.GetComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread();
            var texture = new Texture2D(128, 128);
            try {
                window.GetComponent<BrowserWindowView>().Build(root.GetComponent<BrowserDesktopPanel>(), 1, owner);
                AssertSizes(window, 808);
                window.GetComponentInChildren<RawImage>().texture = texture; AssertSizes(window, 808);
                window.GetComponent<RectTransform>().sizeDelta = new Vector2(1280, 700); AssertSizes(window, 508);
            } finally { Object.DestroyImmediate(window); owner.Close(); Object.DestroyImmediate(root); Object.DestroyImmediate(texture); }
        }
        [Test] public void FindAndMenusDoNotResizeThePage() {
            var root = new GameObject("Application", typeof(RectTransform), typeof(Canvas), typeof(BrowserDispatcher), typeof(BrowserDesktopPanel));
            var window = new GameObject("Window", typeof(RectTransform), typeof(Canvas), typeof(BrowserWindowView)); window.transform.SetParent(root.transform, false);
            var owner = root.GetComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread();
            try {
                var view = window.GetComponent<BrowserWindowView>(); view.Build(root.GetComponent<BrowserDesktopPanel>(), 1, owner);
                AssertSizes(window, 808); view.Tools.ShowFind(); AssertSizes(window, 808);
                window.GetComponentsInChildren<Button>(true).Single(b => b.name == "Close find").onClick.Invoke(); AssertSizes(window, 808);
                view.Tools.ShowMenu(); AssertSizes(window, 808);
                Assert.IsTrue(view.Tools.MenuOpen);
                Assert.Less(view.Tools.Popups.Card.rect.width, window.GetComponent<RectTransform>().rect.width * .5f);
                Assert.IsNotNull(view.Tools.Popups.Card.GetComponent<RectMask2D>());
                view.CloseMenu(); Assert.IsFalse(view.Tools.MenuOpen);
            } finally { Object.DestroyImmediate(window); owner.Close(); Object.DestroyImmediate(root); }
        }
        [TestCase(-1, -1)] [TestCase(-1, 1)] [TestCase(1, -1)] [TestCase(1, 1)]
        public void CornerResizeKeepsTheOppositeWorldCornerAndCancelRestores(int x, int y) {
            var root = new GameObject("Application", typeof(RectTransform), typeof(Canvas), typeof(BrowserDispatcher), typeof(BrowserDesktopPanel));
            var window = new GameObject("Window", typeof(RectTransform), typeof(Canvas), typeof(BrowserWindowView)); window.transform.SetParent(root.transform, false);
            var owner = root.GetComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread();
            try {
                var view = window.GetComponent<BrowserWindowView>(); view.Build(root.GetComponent<BrowserDesktopPanel>(), 1, owner);
                var rect = window.GetComponent<RectTransform>(); rect.position = new Vector3(.3f, .1f, 2); rect.rotation = Quaternion.Euler(5, 20, 0); rect.localScale = Vector3.one * .0012f;
                var corner = window.GetComponentsInChildren<BrowserWindowDrag>().Single(d => d.name == "Resize corner " + x + " " + y);
                Vector3 original = rect.position, opposite = rect.TransformPoint(new Vector3(-x * 640, -y * 500, 0));
                Vector3 point = rect.TransformPoint(new Vector3(x * 640, y * 500, 0));
                var data = new BrowserRayPointerEventData(null) { HasWorldRay = true, WorldRay = new Ray(point - rect.forward * 2, rect.forward) };
                corner.OnBeginDrag(data);
                point += rect.TransformVector(new Vector3(x * 100, y * 80, 0)); data.WorldRay = new Ray(point - rect.forward * 2, rect.forward); corner.OnDrag(data);
                Assert.AreEqual(1380, rect.rect.width, .1f); Assert.AreEqual(1080, rect.rect.height, .1f);
                Assert.Less(Vector3.Distance(opposite, rect.TransformPoint(new Vector3(-x * 690, -y * 540, 0))), .0001f);
                corner.OnCancel(null); Assert.Less(Vector3.Distance(original, rect.position), .0001f); Assert.AreEqual(new Vector2(1280, 1000), rect.sizeDelta);
            } finally { Object.DestroyImmediate(window); owner.Close(); Object.DestroyImmediate(root); }
        }
        [Test] public void BlankHeaderGrabMovesInDepthAndCancelRestores() {
            var root = new GameObject("Application", typeof(RectTransform), typeof(Canvas), typeof(BrowserDispatcher), typeof(BrowserDesktopPanel));
            var window = new GameObject("Window", typeof(RectTransform), typeof(Canvas), typeof(BrowserWindowView)); window.transform.SetParent(root.transform, false);
            var owner = root.GetComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread();
            try {
                var view = window.GetComponent<BrowserWindowView>(); view.Build(root.GetComponent<BrowserDesktopPanel>(), 1, owner);
                window.transform.position = new Vector3(0, 0, 2);
                var grip = window.GetComponentsInChildren<BrowserWindowDrag>().Single(d => d.name == "Reserved title drag space");
                var data = new BrowserRayPointerEventData(null) { HasWorldRay = true, WorldRay = new Ray(Vector3.zero, Vector3.forward) };
                grip.OnBeginDrag(data); data.WorldRay = new Ray(new Vector3(0, 0, .3f), Vector3.forward); grip.OnDrag(data);
                Assert.AreEqual(2.3f, window.transform.position.z, .0001f);
                grip.OnCancel(null); Assert.AreEqual(2, window.transform.position.z, .0001f);
            } finally { Object.DestroyImmediate(window); owner.Close(); Object.DestroyImmediate(root); }
        }
        [Test] public void CornerHitAreasGrowOutwardWithoutCoveringHeaderButtonCenters() {
            var root = new GameObject("Application", typeof(RectTransform), typeof(Canvas), typeof(BrowserDispatcher), typeof(BrowserDesktopPanel));
            var window = new GameObject("Window", typeof(RectTransform), typeof(Canvas), typeof(BrowserWindowView)); window.transform.SetParent(root.transform, false);
            var owner = root.GetComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread();
            try {
                window.GetComponent<BrowserWindowView>().Build(root.GetComponent<BrowserDesktopPanel>(), 1, owner);
                LayoutRebuilder.ForceRebuildLayoutImmediate(window.GetComponent<RectTransform>());
                var corners = window.GetComponentsInChildren<RectTransform>().Where(r => r.name.StartsWith("Resize corner ")).ToArray();
                foreach (var corner in corners) {
                    Assert.AreEqual(new Vector2(96, 96), corner.sizeDelta);
                    Assert.AreEqual(16, corner.rect.width * .5f - Mathf.Abs(corner.anchoredPosition.x), .001f);
                    Assert.AreEqual(16, corner.rect.height * .5f - Mathf.Abs(corner.anchoredPosition.y), .001f);
                    foreach (var button in window.GetComponentsInChildren<Button>()) {
                        var center = ((RectTransform)button.transform).TransformPoint(((RectTransform)button.transform).rect.center);
                        Assert.IsFalse(corner.rect.Contains(corner.InverseTransformPoint(center)), button.name);
                    }
                }
            } finally { Object.DestroyImmediate(window); owner.Close(); Object.DestroyImmediate(root); }
        }
        [TestCase(BrowserWindowPlacementMode.FaceViewer)]
        [TestCase(BrowserWindowPlacementMode.FollowHand)]
        [TestCase(BrowserWindowPlacementMode.TranslateOnly)]
        [TestCase(BrowserWindowPlacementMode.YawOnly)]
        public void PlacementKeepsGrabPointAndCancelRestoresPose(BrowserWindowPlacementMode mode) {
            var root = new GameObject("Application", typeof(RectTransform), typeof(Canvas), typeof(BrowserDispatcher), typeof(BrowserDesktopPanel));
            var window = new GameObject("Window", typeof(RectTransform), typeof(Canvas), typeof(BrowserWindowView)); window.transform.SetParent(root.transform, false);
            var camera = new GameObject("Viewer", typeof(Camera));
            var owner = root.GetComponent<BrowserDispatcher>(); owner.InitializeOnOwnerThread();
            try {
                var app = root.GetComponent<BrowserDesktopPanel>(); app.worldCamera = camera.GetComponent<Camera>(); app.placementMode = mode;
                var view = window.GetComponent<BrowserWindowView>(); view.Build(app, 1, owner);
                var rect = window.GetComponent<RectTransform>(); rect.position = new Vector3(0, 0, 2); rect.localScale = Vector3.one * .0012f;
                if (mode == BrowserWindowPlacementMode.YawOnly) rect.rotation = Quaternion.Euler(app.fixedTiltDegrees, 0, 0);
                var original = rect.position; var originalRotation = rect.rotation;
                var localGrab = new Vector3(200, 450, 0); var point = rect.TransformPoint(localGrab);
                var grip = window.GetComponentsInChildren<BrowserWindowDrag>().Single(d => d.name == "Reserved title drag space");
                var data = new BrowserRayPointerEventData(null) { HasWorldRay = true, HasWorldRotation = true, WorldRotation = Quaternion.identity,
                    WorldRay = new Ray(point - Vector3.forward * 2, Vector3.forward) };
                grip.OnBeginDrag(data); grip.OnDrag(data);
                Assert.Less(Vector3.Distance(original, rect.position), .0001f); Assert.Less(Quaternion.Angle(originalRotation, rect.rotation), .001f);
                data.WorldRay = new Ray(data.WorldRay.origin + new Vector3(.5f, .2f, .3f), Vector3.forward);
                data.WorldRotation = Quaternion.Euler(10, 20, 30); grip.OnDrag(data);
                Assert.Less(Vector3.Distance(data.WorldRay.GetPoint(2), rect.TransformPoint(localGrab)), .0001f);
                if (mode == BrowserWindowPlacementMode.TranslateOnly) Assert.Less(Quaternion.Angle(originalRotation, rect.rotation), .001f);
                else Assert.Greater(Quaternion.Angle(originalRotation, rect.rotation), 1);
                if (mode == BrowserWindowPlacementMode.YawOnly) Assert.Less(Quaternion.Angle(BrowserWindowPose.Normalize(rect.rotation, app.fixedTiltDegrees), rect.rotation), .001f);
                if (mode == BrowserWindowPlacementMode.FollowHand) Assert.Less(Quaternion.Angle(data.WorldRotation, rect.rotation), .001f);
                grip.OnCancel(null); Assert.Less(Vector3.Distance(original, rect.position), .0001f);
                Assert.Less(Quaternion.Angle(originalRotation, rect.rotation), .001f);
            } finally { Object.DestroyImmediate(window); owner.Close(); Object.DestroyImmediate(root); Object.DestroyImmediate(camera); }
        }
        private static void AssertSizes(GameObject window, float pageHeight) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(window.GetComponent<RectTransform>());
            var rects = window.GetComponentsInChildren<RectTransform>();
            Assert.AreEqual(68, rects.Single(r => r.name == "Tabs").rect.height, .1f);
            Assert.AreEqual(52, rects.Single(r => r.name == "Navigation").rect.height, .1f);
            Assert.AreEqual(24, rects.Single(r => r.name == "Status bar").rect.height, .1f);
            Assert.AreEqual(pageHeight, rects.Single(r => r.name == "Page area").rect.height, .1f);
            Assert.GreaterOrEqual(rects.Single(r => r.name == "Reserved title drag space").rect.width, 144);
            Assert.AreEqual("Tab strip", rects.Single(r => r.name == "New tab" && r.parent.name == "Tab strip").parent.name);
            Assert.AreEqual(4, rects.Count(r => r.name.StartsWith("Resize corner ")));
        }
    }
}
