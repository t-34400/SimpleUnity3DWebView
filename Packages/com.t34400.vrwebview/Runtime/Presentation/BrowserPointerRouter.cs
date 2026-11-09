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
            internal bool Hovered;
            internal int HoverId;
            internal float HoverX, HoverY;
        }
        private Route current;
        private long version;
        public Exception LastError { get; private set; }

        public bool Bind(IBrowserPointerSink sink)
        {
            long next = ++version;
            var previous = current; current = null;
            Cancel(previous);
            ExitHover(previous);
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
                if (!ExitHover(route) || !ReferenceEquals(current, route)) return false;
                route.Pressed = true; route.Id = value.Id;
            } else if (!route.Pressed || route.Id != value.Id) return false;
            if (value.Phase == PointerPhase.Cancel) return Cancel(route);
            route.X = value.X; route.Y = value.Y;
            if (value.Phase == PointerPhase.Up) route.Pressed = false;
            if (Deliver(route, value)) return true;
            Cancel(route); return false;
        }

        public void ClearHover() { ExitHover(current); }
        public void ClearHover(int id) { if (current != null && current.HoverId == id) ExitHover(current); }
        private bool ExitHover(Route route)
        {
            if (route == null || !route.Hovered) return true;
            route.Hovered = false;
            return DeliverHover(route, new BrowserHoverEvent(route.HoverId, HoverPhase.Exit, route.HoverX, route.HoverY));
        }
        public bool SendHover(BrowserHoverEvent value)
        {
            var route = current;
            if (route == null || !(route.Sink is IBrowserHoverSink) || route.Pressed
                || value.Phase < HoverPhase.Enter || value.Phase > HoverPhase.Exit || !Finite(value.X) || !Finite(value.Y)) return false;
            if (value.Phase == HoverPhase.Enter) {
                if (route.Hovered) return false;
                route.Hovered = true; route.HoverId = value.Id;
            } else if (!route.Hovered || route.HoverId != value.Id) return false;
            if (value.Phase == HoverPhase.Exit) return ExitHover(route);
            route.HoverX = value.X; route.HoverY = value.Y;
            if (DeliverHover(route, value)) return true;
            ExitHover(route); return false;
        }
        private bool DeliverHover(Route route, BrowserHoverEvent value)
        {
            try { ((IBrowserHoverSink)route.Sink).SendHover(value); return true; }
            catch (Exception error) { LastError = error; return false; }
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
