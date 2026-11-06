using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace T34400.VRWebView
{
    /// <summary>Thread-safe response correlation. Native events must carry both generation and ID.</summary>
    public sealed class RequestLedger : IDisposable
    {
        private readonly object gate = new object();
        private readonly Dictionary<long, TaskCompletionSource<string>> pending = new Dictionary<long, TaskCompletionSource<string>>();
        private long next;
        private bool disposed;
        public int Generation { get; private set; }

        public Task<string> Begin(out long requestId, out int generation)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException(nameof(RequestLedger));
                requestId = checked(++next); generation = Generation;
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                pending.Add(requestId, completion);
                return completion.Task;
            }
        }
        public bool Complete(int generation, long requestId, string result)
        {
            TaskCompletionSource<string> completion;
            lock (gate)
            {
                if (disposed || generation != Generation || !pending.TryGetValue(requestId, out completion)) return false;
                pending.Remove(requestId);
            }
            return completion.TrySetResult(result);
        }
        public void Detach() { EndPending(false); }
        public void Dispose() { EndPending(true); }
        private void EndPending(bool final)
        {
            TaskCompletionSource<string>[] values;
            lock (gate)
            {
                if (disposed) return;
                disposed = final; Generation++;
                values = new TaskCompletionSource<string>[pending.Count];
                pending.Values.CopyTo(values, 0); pending.Clear();
            }
            foreach (var value in values) value.TrySetCanceled();
        }
    }
}
