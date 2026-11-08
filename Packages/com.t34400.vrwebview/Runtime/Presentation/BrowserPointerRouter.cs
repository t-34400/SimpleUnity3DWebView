using System;

namespace T34400.VRWebView.Presentation
{
    /// <summary>Owner-thread routing with one gesture per target. A target switch
    /// cancels the old gesture before accepting a new target; it owns no session.</summary>
    public sealed class BrowserPointerRouter
    {
        private sealed class Route
        {
            internal IBrowserPointerSink Sink;
            internal bool Pressed;
            internal int Id;
            internal float X, Y;
        }
        private Route current;
        private long version;
        public Exception LastError { get; private set; }

        public bool Bind(IBrowserPointerSink sink)
        {
            long next = ++version;
            var previous = current; current = null;
            Cancel(previous);
            if (next != version) return false; // A reentrant newer bind wins.
            if (sink != null) current = new Route { Sink = sink };
            return true;
        }

        public void Cancel() { Cancel(current); }
        private bool Cancel(Route route)
        {
            if (route == null || !route.Pressed) return true;
            route.Pressed = false;
            return Deliver(route, new BrowserPointerEvent(route.Id, PointerPhase.Cancel, route.X, route.Y));
        }

        public bool Send(BrowserPointerEvent value)
        {
            var route = current;
            if (route == null || value.Phase < PointerPhase.Down || value.Phase > PointerPhase.Cancel
                || !Finite(value.X) || !Finite(value.Y)) return false;
            if (value.Phase == PointerPhase.Down) {
                if (route.Pressed) return false;
                route.Pressed = true; route.Id = value.Id;
            } else if (!route.Pressed || route.Id != value.Id) return false;
            if (value.Phase == PointerPhase.Cancel) return Cancel(route);
            route.X = value.X; route.Y = value.Y;
            if (value.Phase == PointerPhase.Up) route.Pressed = false;
            if (Deliver(route, value)) return true;
            Cancel(route); return false;
        }

        public bool SendScroll(BrowserScrollEvent value)
        {
            var route = current;
            if (route == null || !(route.Sink is IBrowserScrollSink scroll)
                || (route.Pressed && route.Id != value.Id) || !Finite(value.X) || !Finite(value.Y)
                || !Finite(value.DeltaX) || !Finite(value.DeltaY)) return false;
            try { scroll.SendScroll(value); return true; }
            catch (Exception error) { LastError = error; return false; }
        }

        private bool Deliver(Route route, BrowserPointerEvent value)
        {
            try { route.Sink.SendPointer(value); return true; }
            catch (Exception error) { LastError = error; return false; }
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
