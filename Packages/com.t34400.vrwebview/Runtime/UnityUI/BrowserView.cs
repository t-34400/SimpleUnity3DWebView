using System;
using T34400.VRWebView.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace T34400.VRWebView
{
    [DisallowMultipleComponent, RequireComponent(typeof(RawImage), typeof(BrowserPointerSource))]
    public sealed class BrowserView : MonoBehaviour
    {
        [SerializeField] private bool autoOrientTexture = true;
        [SerializeField] private bool interactionEnabled = true;
        private RawImage image;
        private BrowserPointerSource pointer;
        private readonly BrowserPointerRouter router = new BrowserPointerRouter();
        private IBrowserSurfaceSource<Texture> source;
        private IBrowserPointerSink sink;
        private BrowserDispatcher owner;
        private BrowserSurfaceBinding<Texture> binding;
        private BrowserSurfaceSnapshot<Texture> current;
        private IDisposable registration;
        private long version;
        private bool listening;
        private Exception reportedInputError;
        public BrowserSurfaceSnapshot<Texture> CurrentSurface => current;
        public Exception LastInputError => router.LastError;
        public Exception LastPresentationError { get; private set; }

        public void SetInteractionEnabled(bool allowed)
        {
            if (!ReferenceEquals(owner, null)) owner.VerifyAccess();
            Components(); interactionEnabled = allowed;
            long before = version;
            if (!allowed) {
                DisablePointer();
                if (version == before && !interactionEnabled) router.Cancel();
            } else if (pointer != null)
                pointer.InputEnabled = isActiveAndEnabled && current != null && current.Surface != null && sink != null;
            ReportInputError();
        }

        public void Bind(IBrowserSurfaceSource<Texture> next, IBrowserPointerSink input, BrowserDispatcher dispatcher)
        {
            if (dispatcher == null) throw new ArgumentNullException(nameof(dispatcher));
            dispatcher.VerifyAccess();
            if (!dispatcher.IsRunning) throw new ObjectDisposedException(nameof(BrowserDispatcher));
            if (next == null) throw new ArgumentNullException(nameof(next));
            Components();
            if (isActiveAndEnabled) Listen();
            long cleared = Clear(true);
            if (version != cleared) return; // Reentrant newer binding wins.
            source = next; sink = input; owner = dispatcher;
            if (isActiveAndEnabled) Attach();
        }

        public void Unbind()
        {
            if (!ReferenceEquals(owner, null)) owner.VerifyAccess();
            Clear(true);
        }

        private sealed class Connection : IDisposable
        {
            private readonly BrowserView view;
            private readonly long version;
            internal Connection(BrowserView view, long version) { this.view = view; this.version = version; }
            public void Dispose() { if (view != null && view.version == version) view.Unbind(); }
        }

        private void Components()
        {
            if (image == null) image = GetComponent<RawImage>();
            if (pointer == null) pointer = GetComponent<BrowserPointerSource>();
        }
        private void Awake() { Components(); pointer.InputEnabled = false; image.texture = null; image.enabled = false; }
        private void OnEnable()
        {
            Components();
            Listen();
            if (source != null && owner != null && owner.IsRunning) Attach();
        }
        private void Listen()
        {
            if (!listening) {
                pointer.Capture.Received += PointerReceived;
                pointer.ScrollReceived += ScrollReceived;
                listening = true;
            }
        }

        private void Attach()
        {
            long attached = ++version;
            var dispatcher = owner;
            var schedule = dispatcher.Scheduler;
            registration = dispatcher.Track(new Connection(this, attached));
            // Owner-thread withdrawals clear the RawImage synchronously before the
            // resource owner can retire its target. Worker events stay queued.
            binding = new BrowserSurfaceBinding<Texture>(action => {
                if (dispatcher.IsOwnerThread) action(); else schedule(action);
            }, snapshot => {
                if (version != attached || !isActiveAndEnabled) return;
                try { Present(snapshot, attached); }
                catch (Exception error) { LastPresentationError = error; DisablePointer(); Debug.LogException(error, this); }
            });
            var created = binding;
            try { created.Bind(source); }
            catch {
                if (version == attached) Unbind(); else created.Dispose();
                throw;
            }
        }

        private void Present(BrowserSurfaceSnapshot<Texture> snapshot, long attached)
        {
            DisablePointer();
            if (version != attached) return;
            router.Bind(null);
            if (version != attached) return;
            current = snapshot;
            bool visible = snapshot != null && snapshot.Surface != null;
            image.texture = visible ? snapshot.Surface : null; image.enabled = visible;
            if (visible && autoOrientTexture)
                image.uvRect = snapshot.Origin == BrowserSurfaceOrigin.TopLeft ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            router.Bind(visible ? sink : null);
            if (version == attached) pointer.InputEnabled = visible && sink != null && interactionEnabled;
        }

        private void DisablePointer()
        {
            if (pointer == null) return;
            try { pointer.InputEnabled = false; }
            catch (Exception error) { Debug.LogException(error, this); }
        }

        private long Clear(bool forget)
        {
            Components();
            long cleared = ++version;
            var previous = binding; binding = null;
            var lifetime = registration; registration = null;
            DisablePointer();
            if (version == cleared) router.Bind(null);
            previous?.Dispose(); lifetime?.Dispose();
            if (version == cleared) {
                current = null;
                if (image != null) { image.texture = null; image.enabled = false; }
                if (forget) { source = null; sink = null; owner = null; }
            }
            ReportInputError();
            return cleared;
        }

        private void OnDisable()
        {
            Clear(false);
            if (pointer != null && listening) {
                pointer.Capture.Received -= PointerReceived;
                pointer.ScrollReceived -= ScrollReceived;
            }
            listening = false;
        }
        private void OnDestroy() { Clear(true); }

        private bool Map(float x, float y, out float browserX, out float browserY)
        {
            browserX = browserY = 0;
            if (current == null || current.Surface == null || image == null) return false;
            Rect uv = image.uvRect;
            return BrowserSurfaceCoordinates.TryMap(x, y, uv.x, uv.y, uv.width, uv.height,
                current.Origin, out browserX, out browserY);
        }

        private void PointerReceived(BrowserPointerEvent value)
        {
            if (value.Phase == PointerPhase.Cancel) router.Cancel();
            else if (Map(value.X, value.Y, out float x, out float y)) {
                if (!router.Send(new BrowserPointerEvent(value.Id, value.Phase, x, y))) pointer.Capture.Cancel(value.Id);
            } else { router.Cancel(); pointer.Capture.Cancel(value.Id); }
            ReportInputError();
        }

        private void ScrollReceived(BrowserScrollEvent value)
        {
            if (Map(value.X, value.Y, out float x, out float y))
                router.SendScroll(new BrowserScrollEvent(value.Id, x, y, value.DeltaX, value.DeltaY));
            ReportInputError();
        }

        private void ReportInputError()
        {
            var error = router.LastError;
            if (error == null || ReferenceEquals(error, reportedInputError)) return;
            reportedInputError = error; Debug.LogException(error, this);
        }
    }
}
