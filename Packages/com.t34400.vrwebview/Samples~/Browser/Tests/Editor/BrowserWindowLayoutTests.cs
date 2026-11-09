using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
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
        private static void AssertSizes(GameObject window, float pageHeight) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(window.GetComponent<RectTransform>());
            var rects = window.GetComponentsInChildren<RectTransform>();
            Assert.AreEqual(52, rects.Single(r => r.name == "Tabs").rect.height, .1f);
            Assert.AreEqual(52, rects.Single(r => r.name == "Navigation").rect.height, .1f);
            Assert.AreEqual(40, rects.Single(r => r.name == "Window handle").rect.height, .1f);
            Assert.AreEqual(pageHeight, rects.Single(r => r.name == "Page area").rect.height, .1f);
        }
    }
}
