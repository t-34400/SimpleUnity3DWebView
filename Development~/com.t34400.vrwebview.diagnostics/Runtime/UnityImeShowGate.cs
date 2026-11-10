namespace T34400.VRWebView
{
    // Diagnostic scheduling only. Local EditorInfo readiness is not an IME acknowledgement.
    internal sealed class UnityImeShowGate
    {
        private readonly double deadline;
        private double nextRead;
        private bool pending, previousReady;
        private int view, type, options;
        public bool Finished { get; private set; }
        public bool TimedOut { get; private set; }
        public bool Claimed { get; private set; }
        public bool Ready { get; private set; }
        public UnityImeShowGate(double now) { deadline = now + 2; nextRead = now; }
        public bool Expire(double now) {
            if (!Finished && now >= deadline) { TimedOut = Finished = true; Ready = false; }
            return TimedOut;
        }
        public bool TryRead(double now) {
            if (Expire(now) || Finished || pending || now < nextRead) return false;
            pending = true; nextRead = now + .05; return true;
        }
        public void Observe(double now, bool ready, int viewId, int inputType, int imeOptions) {
            if (!pending) return;
            pending = false;
            if (Expire(now) || Finished) return;
            Ready = ready && previousReady && view == viewId && type == inputType && options == imeOptions;
            previousReady = ready; view = viewId; type = inputType; options = imeOptions;
        }
        public bool ClaimShow() {
            if (!Ready || Finished) return false;
            Claimed = Finished = true; return true;
        }
        public void Cancel() { Finished = true; Ready = previousReady = false; }
    }
}
