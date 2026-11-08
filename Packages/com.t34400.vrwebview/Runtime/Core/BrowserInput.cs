namespace T34400.VRWebView
{
    /// <summary>Requests normalized top-left browser coordinates. Returning from
    /// these calls is not proof that the page accepted or displayed the input.</summary>
    public interface IBrowserPointerSink
    {
        void SendPointer(BrowserPointerEvent value);
    }

    public readonly struct BrowserScrollEvent
    {
        public readonly int Id;
        public readonly float X, Y, DeltaX, DeltaY;
        /// <summary>Deltas are logical scroll steps, not CSS pixels. The backend
        /// maps these to its platform's scroll units. Positive X is right,
        /// positive Y is up.</summary>
        public BrowserScrollEvent(int id, float x, float y, float deltaX, float deltaY)
        { Id = id; X = x; Y = y; DeltaX = deltaX; DeltaY = deltaY; }
    }

    public interface IBrowserScrollSink
    {
        void SendScroll(BrowserScrollEvent value);
    }
}
