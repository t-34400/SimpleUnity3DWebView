using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace T34400.VRWebView.Tests
{
    public sealed class BrowserLifecycleTests
    {
        private sealed class Sink : IBrowserPointerSink
        {
            internal readonly List<BrowserPointerEvent> Events = new List<BrowserPointerEvent>();
            public void SendPointer(BrowserPointerEvent value) { Events.Add(value); }
        }
        [UnityTest] public IEnumerator DisabledViewCancelsAndResumesCurrentSurfaceWithoutDestroyingIt()
        {
            var root = new GameObject("Owner", typeof(BrowserDispatcher));
            var surface = new GameObject("View", typeof(RectTransform), typeof(RawImage), typeof(BrowserPointerSource), typeof(BrowserView));
            var first = new Texture2D(2, 2); var next = new Texture2D(4, 4);
            try {
                var owner = root.GetComponent<BrowserDispatcher>(); var source = new BrowserTextureSource(owner);
                var view = surface.GetComponent<BrowserView>(); var pointer = surface.GetComponent<BrowserPointerSource>(); var sink = new Sink();
                source.Publish(first); view.Bind(source, sink, owner); pointer.Capture.Send(7, PointerPhase.Down, .2f, .3f);
                view.enabled = false; yield return null;
                Assert.IsNull(surface.GetComponent<RawImage>().texture); Assert.AreEqual(2, sink.Events.Count); Assert.AreEqual(PointerPhase.Cancel, sink.Events[1].Phase);
                source.Publish(next); view.enabled = true; yield return null;
                Assert.AreSame(next, surface.GetComponent<RawImage>().texture); Assert.IsTrue(first != null);
                view.Unbind();
            } finally { Object.Destroy(surface); Object.Destroy(root); Object.Destroy(first); Object.Destroy(next); }
            yield return null;
        }
        [UnityTest] public IEnumerator DisabledDispatcherEndsScopeAndRejectsItsOldScheduler()
        {
            var root = new GameObject("Owner", typeof(BrowserDispatcher));
            var surface = new GameObject("View", typeof(RectTransform), typeof(RawImage), typeof(BrowserPointerSource), typeof(BrowserView));
            var texture = new Texture2D(2, 2);
            try {
                var owner = root.GetComponent<BrowserDispatcher>(); var source = new BrowserTextureSource(owner); var view = surface.GetComponent<BrowserView>();
                source.Publish(texture); view.Bind(source, null, owner); var old = owner.Scheduler;
                owner.enabled = false; yield return null;
                Assert.IsNull(surface.GetComponent<RawImage>().texture); Assert.Throws<ObjectDisposedException>(() => old(() => { }));
                owner.enabled = true; yield return null; int calls = 0; owner.Scheduler(() => calls++); yield return null;
                Assert.AreEqual(1, calls); Assert.IsTrue(texture != null);
            } finally { Object.Destroy(surface); Object.Destroy(root); Object.Destroy(texture); }
            yield return null;
        }
    }
}
