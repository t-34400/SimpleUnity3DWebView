package com.t34400.vrwebview;

/** Executable tests for seeking boundaries and GPU-preview touch coordinates. No Android mocks. */
public final class ProbeTests {
    private static int checks;
    private static void check(boolean result) {
        checks++;
        if (!result) throw new AssertionError("Probe check failed: " + checks);
    }
    private static void range(String header, long size, int status, long start, long length) {
        ProbeRange result = ProbeRange.parse(header, size);
        check(result.status == status && result.start == start && result.length == length);
    }
    public static void main(String[] args) {
        range(null, 100, 200, 0, 100);
        range("bytes=0-", 100, 206, 0, 100);
        range("bytes=40-49", 100, 206, 40, 10);
        range("bytes=40-999", 100, 206, 40, 60);
        range("bytes=99-", 100, 206, 99, 1);
        range("bytes=-10", 100, 206, 90, 10);
        range("bytes=-1000", 100, 206, 0, 100);
        range("bytes=100-", 100, 416, 0, 0);
        range("bytes=50-40", 100, 416, 0, 0);
        range("bytes=-0", 100, 416, 0, 0);
        range("bytes=0-1,4-5", 100, 416, 0, 0);
        range("bytes=999999999999999999999-", 100, 416, 0, 0);
        range("bytes=0-", 0, 416, 0, 0);
        range("garbage", 100, 416, 0, 0);
        range(null, 0, 200, 0, 0);
        ProbeGeometry wide = new ProbeGeometry(1000, 1000, 1280, 720);
        check(wide.left == 0 && wide.top == 218.75f);
        check(!wide.contains(500, 200) && wide.contains(500, 500));
        check(wide.sourceX(500) == 640 && wide.sourceY(500) == 360);
        check(!wide.contains(1000, 500));
        ProbeGeometry tall = new ProbeGeometry(1000, 500, 500, 1000);
        check(tall.left == 375 && tall.top == 0);
        check(!tall.contains(374, 250) && tall.contains(375, 0));
        check(tall.sourceX(500) == 250 && tall.sourceY(250) == 500);
        check(tall.sourceX(300) < 0); // Drags outside the image are not clamped into an edge tap.
        boolean refused = false;
        try { new ProbeGeometry(0, 500, 500, 1000); } catch (IllegalArgumentException expected) { refused = true; }
        check(refused);
        ProbeFocusRecovery focus = new ProbeFocusRecovery();
        check(focus.windowGained() == -1); // No browser ownership: never steal focus.
        focus.claimBrowser();
        focus.windowLost(false);
        check(focus.windowGained() == -1); // The browser did not own focus before loss.
        focus.windowLost(true);
        long first = focus.windowGained();
        check(first >= 0 && focus.consume(first));
        check(!focus.consume(first));
        focus.windowLost(true);
        check(focus.windowGained() == -1); // A second window cycle cannot form an IME loop.
        focus.claimBrowser(); focus.windowLost(true);
        long released = focus.windowGained();
        focus.release();
        check(!focus.consume(released)); // Explicit Unity return/host close cancels posted work.
        focus.claimBrowser(); focus.windowLost(true);
        long stale = focus.windowGained();
        focus.claimBrowser();
        check(!focus.consume(stale)); // A new gesture invalidates an older queued callback.
        focus.windowLost(true);
        long disabled = focus.windowGained();
        focus.setEnabled(false);
        check(!focus.consume(disabled));
        focus.claimBrowser(); focus.windowLost(true);
        check(focus.windowGained() == -1);
        focus.setEnabled(true);
        check(focus.windowGained() == -1); // Toggling on does not immediately steal focus.
        focus.claimBrowser(); focus.windowLost(true);
        long oldWindow = focus.windowGained();
        focus.windowLost(true);
        check(!focus.consume(oldWindow));
        check(focus.windowGained() == -1); // An interrupted attempt still consumes the bound.
        focus.claimBrowser(); focus.windowLost(true);
        check(focus.consume(focus.windowGained()));
        long ime = focus.currentToken();
        check(focus.isCurrent(ime));
        focus.release();
        check(!focus.isCurrent(ime)); // Delayed IME must not reopen after a Unity return.
        focus.claimBrowser();
        long newIme = focus.currentToken();
        focus.windowLost(true);
        check(!focus.isCurrent(newIme)); // A new window loss also cancels the delayed request.
        HostTrialState trial = new HostTrialState();
        check(HostTrialState.startupCase("keyboard").equals("keyboard"));
        check(HostTrialState.startupCase("webview").equals("webview"));
        check(HostTrialState.startupCase(null).equals("menu"));
        check(HostTrialState.startupCase("unknown payload").equals("menu"));
        long oldTrial = trial.begin("activity");
        long newTrial = trial.begin("transparent");
        check(!trial.finish(oldTrial)); // A late timer/destroy cannot finish a newer activity.
        check(trial.isCurrent(newTrial) && trial.kind().equals("transparent"));
        check(trial.finish(newTrial));
        check(!trial.finish(newTrial));
        long editTrial = trial.begin("native"); trial.clear();
        check(!trial.isCurrent(editTrial)); // Teardown cancels pending IME requests.
        boolean invalidTrial = false;
        try { trial.begin("keyboard"); } catch (IllegalArgumentException expected) { invalidTrial = true; }
        check(invalidTrial && trial.kind().equals("none"));
        check(HostTrialState.startupCase("native-webinfo").equals("native-webinfo"));
        long matchedTrial = trial.begin("native-webinfo"); trial.clear();
        check(!trial.isCurrent(matchedTrial));
        HostEditorProfile profile = new HostEditorProfile("provider/154/123", 0xc0a1, 0x12000005);
        check(profile.matches("provider/154/123") && !profile.matches("provider/155/124") && !profile.matches(null));
        check(profile.matches(0xc0a1, 0x12000005));
        check(!profile.matches(1, 0x12000005) && !profile.matches(0xc0a1, 0x12000006));
        boolean invalidProfile = false;
        try { new HostEditorProfile(null, 1, 0); } catch (IllegalArgumentException expected) { invalidProfile = true; }
        check(invalidProfile);
        invalidProfile = false;
        try { new HostEditorProfile("provider", 0, 0); } catch (IllegalArgumentException expected) { invalidProfile = true; }
        check(invalidProfile); // A null WebView connection must never become a calibration fallback.
        invalidProfile = false;
        try { new HostEditorProfile("provider", 2, 0); } catch (IllegalArgumentException expected) { invalidProfile = true; }
        check(invalidProfile); // The comparison is only for the bundled text field.
        ActivityGpuLifetime lifetime = new ActivityGpuLifetime();
        check(ActivityGpuLifetime.mode(null).equals("texture") && ActivityGpuLifetime.mode("untrusted").equals("texture"));
        check(ActivityGpuLifetime.mode("video").equals("video") && ActivityGpuLifetime.mode("ime").equals("ime"));
        check(lifetime.open(31) && lifetime.current(31));
        check(!lifetime.open(32)); // Cannot replace a producer before stopping it.
        check(!lifetime.close(30) && lifetime.current(31));
        check(lifetime.close(31) && !lifetime.close(31));
        check(lifetime.open(32) && !lifetime.close(31) && lifetime.current(32)); // Old Activity destroy cannot close replacement.
        boolean invalidSession = false;
        try { lifetime.open(0); } catch (IllegalArgumentException expected) { invalidSession = true; }
        check(invalidSession && lifetime.current(32));
        check(ActivityGpuLifetime.mode("passthrough").equals("passthrough"));
        check(ActivityGpuLifetime.mode("head").equals("head"));
        check(ActivityGpuTouchPolicy.passThrough("passthrough", false, false, false));
        check(!ActivityGpuTouchPolicy.passThrough("texture", false, false, false));
        check(!ActivityGpuTouchPolicy.passThrough("passthrough", true, false, false));
        check(!ActivityGpuTouchPolicy.passThrough("passthrough", false, true, false));
        check(!ActivityGpuTouchPolicy.passThrough("passthrough", false, false, true));
        check(ActivityGpuTouchPolicy.passThrough("passthrough", false, false, false)); // Restores after editing/query ends.
        System.out.println("PASS " + checks + " probe range/geometry/focus/host checks");
    }
}
