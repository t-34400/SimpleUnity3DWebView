using System;

namespace T34400.VRWebView.Presentation
{
    /// <summary>A borrowed surface observation. Revision is local to its source,
    /// not a native ID, GPU generation or proof that a frame was presented.</summary>
    public sealed class BrowserSurfaceSnapshot<TSurface> where TSurface : class
    {
        public long Revision { get; }
        public TSurface Surface { get; }
        public int Width { get; }
        public int Height { get; }
        public bool HasSurface => Surface != null;

        public BrowserSurfaceSnapshot(long revision, TSurface surface, int width, int height)
        {
            if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
            if (surface == null ? width != 0 || height != 0 : width < 1 || height < 1)
                throw new ArgumentOutOfRangeException(nameof(width), "A withdrawal has zero extent; a surface has positive extent");
            Revision = revision; Surface = surface; Width = width; Height = height;
        }
    }

    /// <summary>The resource owner publishes monotonically increasing revisions,
    /// including withdrawals (Surface=null). It retains all destruction authority.
    /// Current is read on the binding's owner thread; events may arrive elsewhere.</summary>
    public interface IBrowserSurfaceSource<TSurface> where TSurface : class
    {
        BrowserSurfaceSnapshot<TSurface> Current { get; }
        event Action<BrowserSurfaceSnapshot<TSurface>> Changed;
    }

    /// <summary>Non-owning presentation binding. Bind/Dispose run on the owner thread.
    /// The dispatcher must serialize callbacks there and remain usable until disposal.</summary>
    public sealed class BrowserSurfaceBinding<TSurface> : IDisposable where TSurface : class
    {
        private readonly Action<Action> dispatch;
        private readonly Action<BrowserSurfaceSnapshot<TSurface>> present;
        private IBrowserSurfaceSource<TSurface> source;
        private Action<BrowserSurfaceSnapshot<TSurface>> listener;
        private long bindingVersion, revision;
        private bool disposed;
        public BrowserSurfaceSnapshot<TSurface> Current { get; private set; }
        public Exception LastPresentationError { get; private set; }

        public BrowserSurfaceBinding(Action<Action> dispatcher, Action<BrowserSurfaceSnapshot<TSurface>> presenter)
        {
            dispatch = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            present = presenter ?? throw new ArgumentNullException(nameof(presenter));
        }

        public void Bind(IBrowserSurfaceSource<TSurface> next)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BrowserSurfaceBinding<TSurface>));
            if (ReferenceEquals(source, next)) return;
            Unsubscribe();
            long version = ++bindingVersion;
            source = next; revision = 0; Current = null;
            // Clear the old borrow before reading the replacement source.
            Present(null);
            if (disposed || version != bindingVersion || next == null) return;
            listener = snapshot => dispatch(() => {
                if (!disposed && version == bindingVersion && ReferenceEquals(source, next)) Accept(snapshot);
            });
            next.Changed += listener;
            var current = next.Current;
            if (!disposed && version == bindingVersion && ReferenceEquals(source, next)) Accept(current);
        }

        private void Accept(BrowserSurfaceSnapshot<TSurface> snapshot)
        {
            if (snapshot == null || snapshot.Revision <= revision) return;
            revision = snapshot.Revision; Current = snapshot;
            Present(snapshot);
        }

        private void Present(BrowserSurfaceSnapshot<TSurface> snapshot)
        {
            try { present(snapshot); }
            catch (Exception error) { LastPresentationError = error; }
        }

        private void Unsubscribe()
        {
            if (source != null && listener != null) source.Changed -= listener;
            source = null; listener = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true; ++bindingVersion;
            Unsubscribe(); Current = null;
            Present(null); // No resource/provider Dispose or Texture destruction.
        }
    }
}
