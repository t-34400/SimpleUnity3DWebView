using System;
using System.Collections.Generic;
using System.Threading;
using T34400.VRWebView.Presentation;
using UnityEngine;

namespace T34400.VRWebView
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-1000)]
    public sealed class BrowserDispatcher : MonoBehaviour
    {
        [SerializeField] private int maximumPending = 4096;
        [SerializeField] private int actionsPerFrame = 256;
        private OwnerThreadDispatcher queue;
        private readonly List<Registration> owned = new List<Registration>();
        private bool closing, restartRequested, destroying;
        private OwnerThreadDispatcher Queue => Volatile.Read(ref queue)
            ?? throw new InvalidOperationException("BrowserDispatcher must be enabled on its owner thread first");
        public bool IsOwnerThread => Volatile.Read(ref queue)?.IsOwnerThread == true;
        public bool IsRunning => Volatile.Read(ref queue) != null && !Queue.IsDisposed && !closing;

        // Capture once for a lifetime. An old scheduler cannot post into a new
        // queue after this component is disabled and then enabled again.
        public Action<Action> Scheduler { get {
            VerifyAccess();
            if (closing || Queue.IsDisposed) throw new ObjectDisposedException(nameof(BrowserDispatcher));
            return Queue.Post;
        } }
        public void VerifyAccess() { Queue.VerifyAccess(); }
        public int PumpPending() { return Queue.Drain(Math.Max(1, actionsPerFrame)); }
        public void InitializeOnOwnerThread()
        {
            if (queue != null) queue.VerifyAccess();
            if (!isActiveAndEnabled) throw new InvalidOperationException("Enable BrowserDispatcher before initializing");
            StartQueue();
        }
        public void Close() { Shutdown(); }

        public IDisposable Track(IDisposable lifetime)
        {
            VerifyAccess();
            if (closing || Queue.IsDisposed) throw new ObjectDisposedException(nameof(BrowserDispatcher));
            if (lifetime == null) throw new ArgumentNullException(nameof(lifetime));
            foreach (var item in owned)
                if (ReferenceEquals(item.Value, lifetime)) throw new InvalidOperationException("Lifetime is already tracked");
            var registration = new Registration(this, lifetime); owned.Add(registration); return registration;
        }

        private sealed class Registration : IDisposable
        {
            private readonly BrowserDispatcher owner;
            internal IDisposable Value;
            internal Registration(BrowserDispatcher owner, IDisposable value) { this.owner = owner; Value = value; }
            public void Dispose()
            {
                owner.VerifyAccess();
                var value = Value; if (value == null) return;
                Value = null; owner.owned.Remove(this); value.Dispose();
            }
        }

        private void Awake() { StartQueue(); }
        private void OnEnable() { if (closing) restartRequested = true; else StartQueue(); }
        private void StartQueue()
        {
            if (queue != null && !queue.IsDisposed) return;
            closing = false;
            Volatile.Write(ref queue, new OwnerThreadDispatcher(Math.Max(1, maximumPending), error => Debug.LogException(error, this)));
        }
        private void Update() { PumpPending(); }
        private void OnDisable() { Shutdown(); }
        private void OnDestroy() { destroying = true; Shutdown(); }
        private void Shutdown()
        {
            if (queue == null || queue.IsDisposed || closing) return;
            VerifyAccess(); closing = true;
            var lifetimes = owned.ToArray();
            for (int i = lifetimes.Length - 1; i >= 0; i--) {
                try { lifetimes[i].Dispose(); }
                catch (Exception error) { Debug.LogException(error, this); }
            }
            queue.Dispose(); // Settle the finite backlog after logical lifetimes end.
            closing = false;
            if (restartRequested && !destroying && isActiveAndEnabled) { restartRequested = false; StartQueue(); }
        }
    }
}
