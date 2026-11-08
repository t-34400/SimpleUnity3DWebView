using System;
using System.Collections.Generic;
using System.Threading;

namespace T34400.VRWebView.Presentation
{
    /// <summary>Bounded FIFO queue. Post is thread-safe; Drain/Dispose belong to
    /// the constructing thread. Disposal rejects new work and settles queued work.</summary>
    public sealed class OwnerThreadDispatcher : IDisposable
    {
        private readonly object gate = new object();
        private readonly Queue<Action> pending = new Queue<Action>();
        private readonly int owner = Thread.CurrentThread.ManagedThreadId;
        private readonly int capacity;
        private readonly Action<Exception> report;
        private bool closed, draining;
        public bool IsOwnerThread => Thread.CurrentThread.ManagedThreadId == owner;
        public bool IsDisposed { get { lock (gate) return closed; } }
        public int PendingCount { get { lock (gate) return pending.Count; } }
        public Exception LastError { get; private set; }

        public OwnerThreadDispatcher(int maximumPending = 4096, Action<Exception> reportError = null)
        {
            if (maximumPending < 1) throw new ArgumentOutOfRangeException(nameof(maximumPending));
            capacity = maximumPending; report = reportError;
        }

        public void VerifyAccess()
        {
            if (!IsOwnerThread) throw new InvalidOperationException("Operation requires the owner thread");
        }

        public void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            lock (gate) {
                if (closed) throw new ObjectDisposedException(nameof(OwnerThreadDispatcher));
                if (pending.Count >= capacity) throw new InvalidOperationException("Owner queue is full");
                pending.Enqueue(action);
            }
        }

        public int Drain(int maximumActions = 256)
        {
            VerifyAccess();
            if (maximumActions < 1) throw new ArgumentOutOfRangeException(nameof(maximumActions));
            if (draining) throw new InvalidOperationException("Owner queue cannot be drained recursively");
            draining = true; int count = 0;
            try {
                while (true) {
                    Action action;
                    lock (gate) {
                        // A closed queue finishes its finite backlog, including when
                        // Dispose is called from an action already being drained.
                        if (pending.Count == 0 || (!closed && count >= maximumActions)) break;
                        action = pending.Dequeue();
                    }
                    try { action(); }
                    catch (Exception error) {
                        LastError = error;
                        try { report?.Invoke(error); } catch (Exception) { }
                    }
                    count++;
                }
            } finally { draining = false; }
            return count;
        }

        public void Dispose()
        {
            VerifyAccess();
            lock (gate) closed = true;
            if (!draining) Drain();
        }
    }
}
