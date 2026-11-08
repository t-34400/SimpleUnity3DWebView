package com.t34400.vrwebview;

import java.util.ArrayList;
import java.util.List;

/** Executable delegation/lifetime tests. This does not emulate Android or a WebView. */
public final class ProxyConnectionTests {
    public interface Connection {
        boolean commitText(CharSequence text, int cursor);
        String getText(); Object getHandler(); void closeConnection();
        boolean fail(); boolean performEditorAction(int action);
    }
    public static final class Source implements Connection {
        int writes, closes; final Object handler = new Object();
        public boolean commitText(CharSequence text, int cursor) { writes++; return true; }
        public String getText() { return "synthetic content must never enter diagnostics"; }
        public Object getHandler() { return handler; }
        public void closeConnection() { closes++; }
        public boolean performEditorAction(int action) { return true; }
        public boolean fail() { throw new IllegalStateException("private source contents"); }
    }
    private static int checks;
    private static void check(boolean value) { checks++; if (!value) throw new AssertionError("Proxy check " + checks); }
    public static void main(String[] args) {
        Source source = new Source(); List<String> log = new ArrayList<>();
        BorrowedInputConnection lease = new BorrowedInputConnection(1, source);
        Connection first = lease.create(Connection.class, (g, op, outcome) -> log.add(g + ":" + op + ":" + outcome));
        check(first.commitText("secret synthetic text", 1)); check(source.writes == 1);
        check(first.getHandler() == source.handler); check(first.getText().equals(source.getText()));
        check(!first.fail());
        check(log.stream().noneMatch(s -> s.contains("secret") || s.contains("contents") || s.contains("synthetic")));
        check(first.equals(first)); check(!first.equals(source)); check(first.toString().equals("VRWebView borrowed input connection"));
        first.closeConnection(); check(source.closes == 0); check(!first.commitText("late", 1)); check(source.writes == 1);
        Connection second = lease.create(Connection.class, null); check(second.commitText("current", 1));
        lease.invalidate(); check(!second.commitText("stale", 1)); check(second.getText() == null); check(second.getHandler() == null);
        Source next = new Source(); BorrowedInputConnection newer = new BorrowedInputConnection(2, next);
        Connection current = newer.create(Connection.class, null);
        check(!second.commitText("old generation", 1)); check(next.writes == 0); check(current.commitText("next generation", 1));
        Connection noisy = newer.create(Connection.class, (g, op, outcome) -> { throw new IllegalStateException(); });
        check(noisy.commitText("observer failure", 1)); check(next.writes == 2);
        final int[] actions = {0};
        Connection editor = newer.create(Connection.class, new BorrowedInputConnection.Observer() {
            public void onCall(long generation, String operation, String outcome) {}
            public void onEditorAction(long generation, int action) { check(generation == 2 && action == 6); actions[0]++; }
        });
        check(editor.performEditorAction(6)); check(actions[0] == 1);
        editor.closeConnection(); check(!editor.performEditorAction(6)); check(actions[0] == 1);
        ProxyTrialState state = new ProxyTrialState(); check(state.capture() == -1);
        state.setEnabled(true); check(state.capture() == -1); state.arm(); long ticket = state.capture();
        check(ticket > 0 && state.isCurrent(ticket)); check(state.capture() == -1);
        state.arm(); check(!state.isCurrent(ticket)); long replacement = state.capture(); check(state.isCurrent(replacement));
        state.cancel(); check(!state.isCurrent(replacement)); state.arm(); long modeTicket = state.capture();
        state.setEnabled(false); check(!state.isCurrent(modeTicket)); state.arm(); check(state.capture() == -1);
        state.setEnabled(true); state.arm(); long closing = state.capture(); state.close();
        check(!state.isCurrent(closing)); state.setEnabled(true); state.arm(); check(state.capture() == -1);
        System.out.println("PASS " + checks + " borrowed-connection and proxy trial checks; no Android runtime claim");
    }
}
