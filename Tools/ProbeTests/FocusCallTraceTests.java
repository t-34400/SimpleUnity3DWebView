package com.t34400.vrwebview;

import java.util.ArrayList;
import java.util.List;

/** Exercise trace bounds and non-interference without mocking Android focus dispatch. */
public final class FocusCallTraceTests {
    private static int checks;
    private static void check(boolean value) {
        if (!value) throw new AssertionError("Focus trace check " + (checks + 1));
        checks++;
    }
    public static void main(String[] args) {
        StackTraceElement[] stack = {
            new StackTraceElement("android.view.View", "requestFocus", "/private/DO_NOT_LOG", 1),
            new StackTraceElement("com.unity3d.player.Example", "onWindowFocusChanged", "DO_NOT_LOG", 2),
            new StackTraceElement("Caller", "run", "DO_NOT_LOG", 3)};
        List<String> rows = new ArrayList<>();
        FocusCallTrace trace = new FocusCallTrace(rows::add, 2, 2);
        trace.record("view_change", "current=Surface", stack);
        check(rows.size() == 3);
        check(rows.get(0).contains("event=focus_call seq=1 kind=view_change"));
        check(rows.get(1).contains("class=android.view.View method=requestFocus"));
        check(rows.get(2).contains("class=com.unity3d.player.Example method=onWindowFocusChanged"));
        check(rows.stream().noneMatch(s -> s.contains("DO_NOT_LOG") || s.contains("/private/")));
        trace.record("window_entry", "focused=true", stack);
        check(rows.size() == 6 && rows.get(3).contains("seq=2"));
        trace.record("window_exit", "focused=true", stack);
        check(rows.size() == 6); // New callbacks after the cap do not produce more rows.
        new FocusCallTrace(s -> { throw new IllegalStateException("DO_NOT_LOG"); }, 1, 2)
                .record("view_change", "current=Surface", stack);
        check(true); // A failed diagnostic sink never throws into the observed callback.
        rows.clear();
        new FocusCallTrace(rows::add, 0, 0).record("view_change", "", stack);
        check(rows.isEmpty());
        System.out.println("PASS " + checks + " focus trace checks (caller identity, event/frame bounds, path exclusion and diagnostic isolation)");
    }
}
