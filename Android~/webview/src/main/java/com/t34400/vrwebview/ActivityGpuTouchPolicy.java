package com.t34400.vrwebview;

/** Same-app Window touch experiment; never changes focusability or IME ownership. */
final class ActivityGpuTouchPolicy {
    static boolean passThrough(String mode, boolean sourceVisible, boolean editing, boolean awaitingEditCheck) {
        return "passthrough".equals(mode) && !sourceVisible && !editing && !awaitingEditCheck;
    }
}
