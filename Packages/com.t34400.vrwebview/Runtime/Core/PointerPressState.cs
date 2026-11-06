namespace T34400.VRWebView
{
    /// <summary>A newly tracked or replacement source must release before starting a press.</summary>
    public sealed class PointerPressState
    {
        private int? source;
        private bool wasPressed, began;
        public int? ActiveSourceId => began ? source : null;

        public PointerPhase? Update(bool tracked, int sourceId, bool pressed)
        {
            if (!tracked || source != sourceId)
            {
                bool cancel = began;
                source = tracked ? sourceId : (int?)null;
                wasPressed = tracked && pressed;
                began = false;
                return cancel ? PointerPhase.Cancel : (PointerPhase?)null;
            }
            bool down = pressed && !wasPressed;
            bool up = !pressed && wasPressed;
            wasPressed = pressed;
            if (down) { began = true; return PointerPhase.Down; }
            if (up && began) { began = false; return PointerPhase.Up; }
            return began && pressed ? PointerPhase.Move : (PointerPhase?)null;
        }
        public void Reset() { source = null; wasPressed = began = false; }
    }
}
