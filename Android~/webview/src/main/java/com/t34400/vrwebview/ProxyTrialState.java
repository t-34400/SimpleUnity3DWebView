package com.t34400.vrwebview;

/** UI-thread trial tickets: one handoff per explicit gesture, never a focus-reclaim loop. */
final class ProxyTrialState {
    private boolean enabled, armed, closed;
    private long generation;
    void setEnabled(boolean value) { if (closed) return; enabled = value; cancel(); }
    boolean isEnabled() { return enabled && !closed; }
    long arm() { if (!isEnabled()) return -1; generation++; armed = true; return generation; }
    long capture() { if (!isEnabled() || !armed) return -1; armed = false; return generation; }
    boolean isCurrent(long ticket) { return isEnabled() && ticket > 0 && ticket == generation; }
    void cancel() { generation++; armed = false; }
    void close() { cancel(); closed = true; enabled = false; }
}
