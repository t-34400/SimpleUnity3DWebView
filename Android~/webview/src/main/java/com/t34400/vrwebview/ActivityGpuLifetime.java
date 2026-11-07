package com.t34400.vrwebview;

/** UI-thread generation guard; detached Activities cannot close a replacement. */
final class ActivityGpuLifetime {
    private long id;
    boolean open(long next) {
        if (next <= 0) throw new IllegalArgumentException("Invalid session");
        if (id != 0) return false;
        id = next; return true;
    }
    boolean current(long candidate) { return id != 0 && id == candidate; }
    boolean close(long candidate) { if (!current(candidate)) return false; id = 0; return true; }
    static String mode(String value) {
        return "native".equals(value) || "video".equals(value) || "ime".equals(value) || "passthrough".equals(value) || "head".equals(value) ? value : "texture";
    }
}
