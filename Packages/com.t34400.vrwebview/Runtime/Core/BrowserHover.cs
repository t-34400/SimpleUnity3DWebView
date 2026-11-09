namespace T34400.VRWebView
{
    public enum HoverPhase { Enter, Move, Exit }
    /// <summary>Unpressed ray/mouse position in normalized top-left browser coordinates.
    /// Hover never requests editing focus or starts a touch gesture.</summary>
    public readonly struct BrowserHoverEvent
    {
        public readonly int Id;
        public readonly HoverPhase Phase;
        public readonly float X, Y;
        public BrowserHoverEvent(int id, HoverPhase phase, float x, float y)
        { Id = id; Phase = phase; X = x; Y = y; }
    }
    public interface IBrowserHoverSink { void SendHover(BrowserHoverEvent value); }

    /// <summary>One hover owner; a second ray cannot move or clear the first ray's hover.</summary>
    public sealed class HoverCapture
    {
        private int? owner;
        private float x, y;
        public bool IsHovering => owner.HasValue;
        public int? ActiveSourceId => owner;
        public event System.Action<BrowserHoverEvent> Received;
        public bool Send(int id, HoverPhase phase, float nextX, float nextY)
        {
            if (phase < HoverPhase.Enter || phase > HoverPhase.Exit ||
                float.IsNaN(nextX) || float.IsNaN(nextY) || float.IsInfinity(nextX) || float.IsInfinity(nextY)) return false;
            if (phase == HoverPhase.Enter) {
                if (owner.HasValue) return false;
                owner = id;
            } else if (owner != id) return false;
            if (phase == HoverPhase.Move && nextX == x && nextY == y) return true;
            x = nextX; y = nextY;
            if (phase == HoverPhase.Exit) owner = null;
            Received?.Invoke(new BrowserHoverEvent(id, phase, x, y));
            return true;
        }
        public void Exit() { if (owner.HasValue) Send(owner.Value, HoverPhase.Exit, x, y); }
        public void Exit(int id) { if (owner == id) Exit(); }
    }
}
