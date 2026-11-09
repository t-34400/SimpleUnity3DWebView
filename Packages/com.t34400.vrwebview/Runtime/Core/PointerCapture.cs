using System;

namespace T34400.VRWebView
{
    public enum PointerPhase { Down, Move, Up, Cancel }
    public readonly struct BrowserPointerEvent
    {
        public readonly int Id;
        public readonly PointerPhase Phase;
        /// <summary>Normalized browser coordinates, origin at top left. Captured drags may go outside [0,1].</summary>
        public readonly float X, Y;
        public BrowserPointerEvent(int id, PointerPhase phase, float x, float y)
        { Id = id; Phase = phase; X = x; Y = y; }
    }

    /// <summary>One active gesture per browser. A second controller cannot steal an active press.</summary>
    public sealed class PointerCapture
    {
        private int? owner;
        private float lastX, lastY;
        public bool IsCaptured => owner.HasValue;
        public event Action<BrowserPointerEvent> Received;
        public bool Send(int id, PointerPhase phase, float x, float y)
        {
            if (phase < PointerPhase.Down || phase > PointerPhase.Cancel) return false;
            if (float.IsNaN(x) || float.IsNaN(y) || float.IsInfinity(x) || float.IsInfinity(y)) return false;
            if (phase == PointerPhase.Down)
            {
                if (owner.HasValue) return false;
                owner = id;
            }
            else if (owner != id) return false;
            lastX = x; lastY = y;
            if (phase == PointerPhase.Up || phase == PointerPhase.Cancel) owner = null;
            Received?.Invoke(new BrowserPointerEvent(id, phase, x, y));
            return true;
        }
        public void Cancel()
        {
            if (owner.HasValue) Send(owner.Value, PointerPhase.Cancel, lastX, lastY);
        }
        public void Cancel(int id) { if (owner == id) Cancel(); }
    }
}
