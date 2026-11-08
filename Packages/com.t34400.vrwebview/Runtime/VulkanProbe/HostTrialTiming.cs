namespace T34400.VRWebView
{
    // HC2 diagnostic timing only. Neither deadline is an IME completion signal.
    internal sealed class HostTrialTiming
    {
        internal string StartupCase { get; private set; } = "menu";
        private float startupAt;
        private float keyboardUntil;

        internal void ArmStartup(string mode, float unscaledTime)
        {
            StartupCase = mode;
            startupAt = unscaledTime + 3f;
        }

        internal bool TryTakeStartup(float unscaledTime, bool bridgeAvailable, bool paused,
            bool placed, bool xrRunning, bool applicationFocused, out string mode)
        {
            mode = null;
            if (StartupCase == "menu" || !bridgeAvailable || paused || !placed || !xrRunning
                || !applicationFocused || !(unscaledTime >= startupAt)) return false;
            mode = StartupCase;
            StartupCase = "menu"; // Consume before invoking the existing trial action.
            return true;
        }

        internal void ArmKeyboardTimeout(float unscaledTime)
        {
            keyboardUntil = unscaledTime + 20f;
        }

        internal bool ShouldCloseKeyboard(float unscaledTime, bool instanceExists)
        {
            return instanceExists && unscaledTime >= keyboardUntil;
        }
    }
}
