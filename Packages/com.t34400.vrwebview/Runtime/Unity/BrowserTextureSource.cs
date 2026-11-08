using System;
using T34400.VRWebView.Presentation;
using UnityEngine;

namespace T34400.VRWebView
{
    /// <summary>Unity-typed publication endpoint for a resource owner. This source
    /// never creates, destroys or releases a texture and is not a WebView backend.</summary>
    public sealed class BrowserTextureSource : IBrowserSurfaceSource<Texture>, IDisposable
    {
        private readonly BrowserDispatcher owner;
        private readonly IDisposable registration;
        private BrowserSurfaceSnapshot<Texture> current;
        private long revision;
        private bool disposed;
        public event Action<BrowserSurfaceSnapshot<Texture>> Changed;
        public Exception LastNotificationError { get; private set; }
        public BrowserSurfaceSnapshot<Texture> Current { get { owner.VerifyAccess(); return current; } }

        public BrowserTextureSource(BrowserDispatcher dispatcher)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            owner = dispatcher; owner.VerifyAccess(); registration = owner.Track(this);
        }

        public void Publish(Texture texture, BrowserSurfaceOrigin origin = BrowserSurfaceOrigin.BottomLeft)
        {
            owner.VerifyAccess();
            if (disposed) throw new ObjectDisposedException(nameof(BrowserTextureSource));
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            int width = texture.width, height = texture.height;
            // Surface publication is allocation/geometry metadata, not a frame
            // heartbeat. Updating pixels must not cancel the active gesture.
            if (current != null && ReferenceEquals(current.Surface, texture) && current.Width == width
                && current.Height == height && current.Origin == origin) return;
            Emit(new BrowserSurfaceSnapshot<Texture>(checked(revision + 1), texture, width, height, origin));
        }

        public void Withdraw()
        {
            owner.VerifyAccess();
            if (disposed) throw new ObjectDisposedException(nameof(BrowserTextureSource));
            Emit(new BrowserSurfaceSnapshot<Texture>(checked(revision + 1), null, 0, 0));
        }

        private void Emit(BrowserSurfaceSnapshot<Texture> value)
        {
            revision = value.Revision; current = value;
            var handlers = Changed; if (handlers == null) return;
            foreach (Action<BrowserSurfaceSnapshot<Texture>> handler in handlers.GetInvocationList()) {
                // A subscriber may dispose/withdraw/publish reentrantly. Never
                // deliver the now-obsolete value to remaining subscribers.
                if (!ReferenceEquals(current, value)) break;
                try { handler(value); }
                catch (Exception error) { LastNotificationError = error; Debug.LogException(error); }
            }
        }

        public void Dispose()
        {
            owner.VerifyAccess(); if (disposed) return;
            var withdrawal = new BrowserSurfaceSnapshot<Texture>(checked(revision + 1), null, 0, 0);
            disposed = true;
            try { Emit(withdrawal); }
            finally { registration.Dispose(); Changed = null; }
        }
    }
}
