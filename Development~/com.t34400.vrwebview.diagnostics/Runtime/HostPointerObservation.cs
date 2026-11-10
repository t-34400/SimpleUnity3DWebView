using System;

namespace T34400.VRWebView
{
    [Flags]
    internal enum HostPointerBlockReason
    {
        None = 0,
        Paused = 1,
        ApplicationUnfocused = 2,
        NativeTrial = 4,
        UnityKeyboardVisible = 8,
        UnityKeyboardActive = 16
    }

    // Immutable observations for HC2 only. No tracking/focus changes or lifetime ownership.
    internal readonly struct HostPointerObservation
    {
        internal HostPointerBlockReason BlockReasons { get; }
        internal bool Tracked { get; }
        internal bool GazeAllowed { get; }
        internal bool Blocked => BlockReasons != HostPointerBlockReason.None;
        internal bool CanReadTrackedPointer => Tracked && !Blocked;
        internal bool RayPolicyPermitted => !Blocked && (Tracked || GazeAllowed);
        internal string RaySuppressionReason => Blocked ? "InputSuppressed" : RayPolicyPermitted ? "None" : "NoInputSource";

        internal HostPointerObservation(bool paused, bool applicationFocused, bool nativeTrial,
            bool keyboardVisible, bool keyboardActive, bool tracked, bool gazeFallback, bool gazeSuspended)
        {
            HostPointerBlockReason reasons = HostPointerBlockReason.None;
            if (paused) reasons |= HostPointerBlockReason.Paused;
            if (!applicationFocused) reasons |= HostPointerBlockReason.ApplicationUnfocused;
            if (nativeTrial) reasons |= HostPointerBlockReason.NativeTrial;
            if (keyboardVisible) reasons |= HostPointerBlockReason.UnityKeyboardVisible;
            if (keyboardActive) reasons |= HostPointerBlockReason.UnityKeyboardActive;
            BlockReasons = reasons;
            Tracked = tracked;
            GazeAllowed = gazeFallback && !gazeSuspended;
        }

        // Only evaluated at the existing log cadence. This does not report HMD visibility.
        internal string LogFields => "pointerObservation=Ready pointerBlockReasons="
            + BlockReasons.ToString().Replace(", ", "|") + " rayPolicyPermitted=" + RayPolicyPermitted
            + " raySuppressionReason=" + RaySuppressionReason;
    }
}
