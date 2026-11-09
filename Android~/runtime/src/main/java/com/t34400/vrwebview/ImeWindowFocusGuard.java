package com.t34400.vrwebview;

/** UI-thread Window-loss IME compatibility policy. Provider version is diagnostic metadata only. */
final class ImeWindowFocusGuard {
    enum Action { FORWARD, HOLD_LOSS, KEEP_GAIN }
    private static final long REQUEST_WINDOW_MS = 2000;
    private final boolean supported;
    private final String providerVersion;
    private boolean enabled = true, resumed = true, providerFocused, held;
    private long requestedAt = -1;

    ImeWindowFocusGuard(String provider, String version, boolean initialWindowFocused) {
        // Retain the workaround across updates of this provider. The editing,
        // lifecycle, served-connection and recent-interaction predicates below
        // decide whether to hold a loss; version alone must never switch it off.
        supported = "com.android.webview".equals(provider);
        providerVersion = version;
        providerFocused = initialWindowFocused;
    }
    boolean isSupported() { return supported; }
    String providerVersion() { return providerVersion; }
    boolean isEnabled() { return enabled; }
    boolean isHolding() { return held; }

    boolean request(long now) {
        if (!supported || !enabled || !resumed || !providerFocused || held || now < 0) return false;
        requestedAt = now;
        return true;
    }
    void cancelRequest() { requestedAt = -1; }

    Action windowChanged(boolean focused, boolean viewFocused, boolean editor, boolean served, long now) {
        if (focused) {
            requestedAt = -1;
            if (held) {
                held = false;
                // The provider was never told about this loss. Do not synthesize a false/true pair.
                return Action.KEEP_GAIN;
            }
            providerFocused = true;
            return Action.FORWARD;
        }
        if (held) return Action.HOLD_LOSS;
        boolean recent = requestedAt >= 0 && now >= requestedAt && now - requestedAt <= REQUEST_WINDOW_MS;
        requestedAt = -1; // One loss per actual interaction; no retry timer or indefinite arming.
        if (supported && enabled && resumed && providerFocused && recent && viewFocused && editor && served) {
            held = true;
            return Action.HOLD_LOSS;
        }
        providerFocused = false;
        return Action.FORWARD;
    }

    /** Returns true exactly once if the caller must deliver the outstanding real Window loss. */
    boolean release() {
        requestedAt = -1;
        if (!held) return false;
        held = false;
        providerFocused = false;
        return true;
    }
    boolean setEnabled(boolean value) {
        if (enabled == value) return false;
        enabled = value;
        return release();
    }
    boolean setResumed(boolean value) {
        resumed = value;
        return value ? false : release();
    }
}
