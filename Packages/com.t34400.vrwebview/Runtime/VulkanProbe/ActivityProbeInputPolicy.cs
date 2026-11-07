using System;

namespace T34400.VRWebView
{
    // Must match ActivityGpuProbe.Session.updateState. No Unity/SDK dependency.
    [Flags]
    internal enum ActivityProbeState { Alive = 1, WindowFocus = 2, TextureOnly = 4, Editing = 8, Ime = 16, Ready = 32, Resumed = 64 }
    internal static class ActivityProbeInputPolicy
    {
        internal static bool CanUseOwnedHand(bool paused, bool displayRunning, bool? displayFocus, int state)
        {
            const ActivityProbeState required = ActivityProbeState.Alive | ActivityProbeState.WindowFocus
                | ActivityProbeState.TextureOnly | ActivityProbeState.Ready | ActivityProbeState.Resumed;
            var bits = (ActivityProbeState)state;
            return !paused && displayRunning && displayFocus == true && (bits & required) == required
                && (bits & (ActivityProbeState.Editing | ActivityProbeState.Ime)) == 0;
        }
    }
    internal sealed class ProbePinch
    {
        internal bool Pressed { get; private set; }
        internal bool Update(double distance, double age)
        {
            if (double.IsNaN(distance) || double.IsInfinity(distance) || distance < 0
                || double.IsNaN(age) || age < 0 || age > 0.25) { Reset(); return false; }
            Pressed = distance <= (Pressed ? 0.04 : 0.025);
            return true;
        }
        internal void Reset() { Pressed = false; }
    }
}
