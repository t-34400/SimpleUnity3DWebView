package com.t34400.vrwebview;

/** UI-thread lifetime guard for comparison windows and their delayed callbacks. */
final class HostTrialState {
    static final int DURATION_MS = 20000;
    private long generation;
    private String kind = "none";
    long begin(String value) {
        if (!"native".equals(value) && !"native-webinfo".equals(value) && !"webview".equals(value) && !"activity".equals(value) && !"transparent".equals(value))
            throw new IllegalArgumentException("Unknown native trial");
        kind = value; return ++generation;
    }
    boolean isCurrent(long ticket) { return !kind.equals("none") && ticket == generation; }
    boolean finish(long ticket) {
        if (!isCurrent(ticket)) return false;
        clear(); return true;
    }
    void clear() { kind = "none"; generation++; }
    String kind() { return kind; }
    static String startupCase(String value) {
        if ("keyboard".equals(value) || "native".equals(value) || "native-webinfo".equals(value) || "webview".equals(value) || "activity".equals(value)
                || "transparent".equals(value)) return value;
        return "menu";
    }
}
