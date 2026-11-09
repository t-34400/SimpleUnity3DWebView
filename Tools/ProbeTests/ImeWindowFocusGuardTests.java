package com.t34400.vrwebview;

/** Exercise Window dispatch ordering and cleanup invariants, not an Android/Quest simulation. */
public final class ImeWindowFocusGuardTests {
    private static int checks;
    private static void check(boolean value) {
        if (!value) throw new AssertionError("Window IME guard check " + (checks + 1));
        checks++;
    }
    private static ImeWindowFocusGuard ready() {
        ImeWindowFocusGuard guard = new ImeWindowFocusGuard("com.android.webview", "154.0.0.55.15.1096209767", false);
        guard.windowChanged(true, false, false, false, 0);
        return guard;
    }
    private static ImeWindowFocusGuard.Action loss(ImeWindowFocusGuard guard, long now) {
        return guard.windowChanged(false, true, true, true, now);
    }
    public static void main(String[] args) {
        // FO1: child attached to an already-focused root, without an initial Window=true callback.
        ImeWindowFocusGuard attached = new ImeWindowFocusGuard("com.android.webview", "154.0.0.55.15.1096209767", true);
        check(attached.request(100));
        check(loss(attached, 200) == ImeWindowFocusGuard.Action.HOLD_LOSS);
        ImeWindowFocusGuard guard = ready();
        check(guard.isSupported());
        check(loss(guard, 100) == ImeWindowFocusGuard.Action.FORWARD); // System menu, no editor interaction.
        check(!guard.request(101)); // Cannot arm while provider already has no Window focus.
        guard.windowChanged(true, true, true, true, 200);
        check(guard.request(250));
        check(loss(guard, 400) == ImeWindowFocusGuard.Action.HOLD_LOSS);
        check(guard.isHolding());
        check(loss(guard, 401) == ImeWindowFocusGuard.Action.HOLD_LOSS); // Duplicate loss is idempotent.
        check(!guard.request(402));
        check(guard.windowChanged(true, true, true, true, 10000) == ImeWindowFocusGuard.Action.KEEP_GAIN);
        check(!guard.isHolding()); // No delayed forced close during a long editing session.
        check(loss(guard, 10001) == ImeWindowFocusGuard.Action.FORWARD); // Request was consumed.

        guard = ready(); guard.request(100);
        check(loss(guard, 2100) == ImeWindowFocusGuard.Action.HOLD_LOSS);
        guard = ready(); guard.request(100);
        check(loss(guard, 2101) == ImeWindowFocusGuard.Action.FORWARD);
        guard = ready(); guard.request(100);
        check(loss(guard, 99) == ImeWindowFocusGuard.Action.FORWARD);
        for (int missing = 0; missing < 3; missing++) {
            guard = ready(); guard.request(100);
            check(guard.windowChanged(false, missing != 0, missing != 1, missing != 2, 200)
                    == ImeWindowFocusGuard.Action.FORWARD);
        }
        guard = ready(); guard.request(100); guard.cancelRequest();
        check(loss(guard, 200) == ImeWindowFocusGuard.Action.FORWARD);
        guard = ready(); guard.request(100); loss(guard, 200); guard.cancelRequest();
        check(guard.isHolding()); // Unity pointer reset must not terminate the already active keyboard.

        // GameActivity may take View focus BEFORE the WebView receives Window=true.
        // The pending real loss is released once, then the real gain is delivered normally.
        guard = ready(); guard.request(100); loss(guard, 200);
        check(guard.release());
        check(!guard.release());
        check(guard.windowChanged(true, false, true, false, 300) == ImeWindowFocusGuard.Action.FORWARD);
        check(loss(guard, 301) == ImeWindowFocusGuard.Action.FORWARD);

        guard = ready(); guard.request(100); loss(guard, 200);
        check(guard.setResumed(false));
        check(!guard.setResumed(false));
        check(!guard.request(300));
        check(guard.windowChanged(true, true, true, true, 400) == ImeWindowFocusGuard.Action.FORWARD);
        check(!guard.request(450));
        guard.setResumed(true);
        check(guard.request(500));
        check(loss(guard, 600) == ImeWindowFocusGuard.Action.HOLD_LOSS);
        check(guard.setEnabled(false));
        check(!guard.setEnabled(false));
        guard.windowChanged(true, true, true, true, 700);
        check(!guard.request(750));
        check(loss(guard, 800) == ImeWindowFocusGuard.Action.FORWARD);
        check(!guard.setEnabled(true));
        guard.windowChanged(true, true, true, true, 900);
        check(guard.request(950));

        for (String[] provider : new String[][] {
                {null, null}, {"other.webview", "154.0.0.55.15.1096209767"}}) {
            guard = new ImeWindowFocusGuard(provider[0], provider[1], false);
            guard.windowChanged(true, true, true, true, 0);
            check(!guard.isSupported() && !guard.request(100));
            check(loss(guard, 200) == ImeWindowFocusGuard.Action.FORWARD);
        }
        // Versions are retained for diagnosis; an update is not an opt-out.
        for (String version : new String[] {"154.0.0.55.15.1096209767", "154.0.0.55.15.1096209768", "155.0", "200.0-future", "", null}) {
            guard = new ImeWindowFocusGuard("com.android.webview", version, true);
            check(guard.isSupported());
            check(java.util.Objects.equals(guard.providerVersion(), version));
            check(guard.request(100));
            check(loss(guard, 200) == ImeWindowFocusGuard.Action.HOLD_LOSS);
            check(guard.release());
            check(!guard.release());
            guard.windowChanged(true, true, true, true, 300);
            guard.setEnabled(false);
            check(!guard.request(400));
            check(loss(guard, 500) == ImeWindowFocusGuard.Action.FORWARD);
        }
        System.out.println("PASS " + checks + " Window IME guard checks (version-independent provider policy, request lifetime, balanced dispatch, View/lifecycle cleanup and mode changes)");
    }
}
