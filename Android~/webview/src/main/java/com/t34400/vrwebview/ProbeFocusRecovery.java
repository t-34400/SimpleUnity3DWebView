package com.t34400.vrwebview;

/** UI-thread ownership and bounded retry state; no Android dependencies. */
final class ProbeFocusRecovery {
    private boolean enabled = true, wanted, restoreOnGain;
    private int remaining;
    private long generation;

    boolean isEnabled() { return enabled; }

    void setEnabled(boolean value) {
        if (enabled == value) return;
        enabled = value;
        invalidate();
    }

    void claimBrowser() {
        wanted = true;
        invalidate();
        remaining = 1; // One recovery per explicit pointer/IME request, not per window cycle.
    }

    void release() {
        wanted = false;
        invalidate();
    }

    private void invalidate() { generation++; remaining = 0; restoreOnGain = false; }

    void windowLost(boolean browserHadFocus) {
        generation++; // Invalidate a posted recovery for the previous window cycle.
        restoreOnGain = enabled && wanted && browserHadFocus;
    }

    long windowGained() {
        boolean restore = restoreOnGain && enabled && wanted && remaining > 0;
        restoreOnGain = false;
        if (!restore) return -1;
        remaining--;
        return generation;
    }

    boolean consume(long ticket) {
        if (ticket < 0 || ticket != generation || !enabled || !wanted) return false;
        generation++; // A duplicate callback cannot use the same ticket twice.
        return true;
    }
}
