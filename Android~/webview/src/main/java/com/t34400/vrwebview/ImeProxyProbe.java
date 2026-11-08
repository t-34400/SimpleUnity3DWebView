package com.t34400.vrwebview;

import android.app.Activity;
import android.content.Context;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import org.json.JSONObject;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.InputMethodManager;
import android.widget.EditText;
import android.widget.FrameLayout;
import java.lang.ref.WeakReference;
import java.util.function.Consumer;
import java.util.concurrent.atomic.AtomicInteger;

/** IP1: one opt-in same-Window borrowed-connection trial, not a production input bridge. */
final class ImeProxyProbe {
    private static final Handler UI = new Handler(Looper.getMainLooper());
    private final Activity owner;
    private final ProbeWebView web;
    private final FrameLayout panel;
    private final Consumer<String> trace;
    private final Runnable returnUnity;
    private final ProxyTrialState state = new ProxyTrialState();
    private ProxyEditor proxy;
    private volatile BorrowedInputConnection lease;
    private EditorInfo sourceInfo;
    private InputConnection candidate;
    private EditorInfo candidateInfo;
    private long armTicket;
    private boolean showRequested, waitingForWindow;
    private int connects;
    private long nextPoll, pollEpoch;
    private boolean polling;
    private String lastDom;
    private volatile Boolean lastRelation;

    ImeProxyProbe(Activity owner, ProbeWebView web, FrameLayout panel, Consumer<String> trace, Runnable returnUnity) {
        this.owner = owner; this.web = web; this.panel = panel; this.trace = trace; this.returnUnity = returnUnity;
        web.borrowedConnectionListener = this::sourceCreated;
    }
    void setEnabled(boolean enabled) {
        cancel("mode_change"); state.setEnabled(enabled);
        if (enabled && proxy == null) {
            proxy = new ProxyEditor(owner, this);
            proxy.setSingleLine(true); proxy.setFocusableInTouchMode(true);
            proxy.setTextColor(Color.TRANSPARENT); proxy.setBackgroundColor(Color.TRANSPARENT);
            proxy.setVisibility(View.GONE);
            panel.addView(proxy, new FrameLayout.LayoutParams(320, 72, Gravity.RIGHT | Gravity.BOTTOM));
        }
        event("mode", "enabled=" + enabled);
    }
    boolean isEnabled() { return state.isEnabled(); }
    void arm() {
        if (!isEnabled()) return;
        cancel("new_gesture"); armTicket = state.arm();
        event("armed", "generation=" + armTicket);
    }
    void gestureEnded(boolean canceled) {
        if (!isEnabled() || armTicket <= 0) return;
        if (canceled) { cancel("touch_cancel"); return; }
        long ticket = armTicket;
        // Only a completed tap on an actual editable DOM element may transfer focus.
        web.evaluateJavascript("(function(){var e=document.activeElement;return !!e&&(e.tagName==='INPUT'||e.tagName==='TEXTAREA'||e.isContentEditable===true);})()", result -> {
            if (!state.isCurrent(ticket)) return;
            if (!"true".equals(result)) { cancel("non_editor"); return; }
            if (candidate == null) {
                EditorInfo info = new EditorInfo();
                web.onCreateInputConnection(info); // UI thread, same public API used by IMM.
            }
            if (candidate == null || candidateInfo == null) {
                event("source_unavailable", "generation=" + ticket); state.cancel(); armTicket = 0; return;
            }
            long captured = state.capture();
            if (captured != ticket) return;
            InputConnection target = candidate; EditorInfo info = candidateInfo;
            candidate = null; candidateInfo = null; armTicket = 0;
            connect(ticket, target, info);
        });
    }
    void request() {
        arm(); long ticket = armTicket;
        if (ticket <= 0) return;
        // A fixed local test field: real DOM focus, no text/selection copying into JS.
        web.requestFocus();
        web.evaluateJavascript("(function(){var e=document.getElementById('text');if(!e)return false;e.focus();return document.activeElement===e;})()", result -> {
            if (!state.isCurrent(ticket)) return;
            if (!"true".equals(result)) { cancel("field_not_ready"); return; }
            gestureEnded(false);
        });
    }
    private void sourceCreated(InputConnection connection, EditorInfo info) {
        if (!isEnabled()) return;
        event("source_connection", "connected=" + (connection != null) + " sourceFocus=" + web.hasFocus()
                + " sourceWindow=" + web.hasWindowFocus() + " type=0x" + Integer.toHexString(info.inputType));
        if (armTicket <= 0 || !state.isCurrent(armTicket) || connection == null || info.inputType == 0) return;
        candidate = connection;
        // A private snapshot, never emitted. Context remains in the WebView's connection.
        candidateInfo = new EditorInfo(); copyInfo(info, candidateInfo);
    }
    private void connect(long ticket, InputConnection target, EditorInfo info) {
        if (!state.isCurrent(ticket) || proxy == null) return;
        if (lease != null) lease.invalidate();
        lease = new BorrowedInputConnection(ticket, target); sourceInfo = info;
        showRequested = false; waitingForWindow = false; lastRelation = null;
        event("handoff", "generation=" + ticket + " sourceFocus=" + web.hasFocus()
                + " sourceWindow=" + web.hasWindowFocus());
        proxy.setVisibility(View.VISIBLE);
        boolean focused = proxy.requestFocus();
        event("proxy_focus", "generation=" + ticket + " accepted=" + focused
                + " sourceFocus=" + web.hasFocus() + " proxyFocus=" + proxy.hasFocus()
                + " windowFocus=" + proxy.hasWindowFocus());
        UI.post(() -> show(ticket)); // One request, or one deferral until the Window returns.
    }
    private void show(long ticket) {
        if (!state.isCurrent(ticket) || lease == null || !lease.isValid() || proxy == null) return;
        if (showRequested) return;
        if (!proxy.hasWindowFocus()) { waitingForWindow = true; event("await_window", "generation=" + ticket); return; }
        waitingForWindow = false; showRequested = true;
        InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null) imm.restartInput(proxy);
        boolean accepted = imm != null && imm.showSoftInput(proxy, 0);
        event("ime_request", "generation=" + ticket + " accepted=" + accepted + " proxyFocus=" + proxy.hasFocus()
                + " windowFocus=" + proxy.hasWindowFocus() + " immProxy=" + (imm != null && imm.isActive(proxy))
                + " immSource=" + (imm != null && imm.isActive(web)));
    }
    private InputConnection createConnection(EditorInfo info) {
        BorrowedInputConnection captured = lease;
        if (captured == null || !captured.isValid() || sourceInfo == null) return null;
        copyInfo(sourceInfo, info); connects++;
        event("proxy_connection", "generation=" + captured.generation + " count=" + connects
                + " type=0x" + Integer.toHexString(info.inputType) + " imeOptions=0x" + Integer.toHexString(info.imeOptions));
        WeakReference<ImeProxyProbe> weak = new WeakReference<>(this);
        AtomicInteger recorded = new AtomicInteger();
        return captured.create(InputConnection.class, new BorrowedInputConnection.Observer() {
            @Override public void onCall(long generation, String operation, String outcome) {
                ImeProxyProbe current = weak.get();
                if (current != null && recorded.incrementAndGet() <= 128)
                    current.event("operation", "generation=" + generation + " op=" + operation + " outcome=" + outcome);
            }
            @Override public void onEditorAction(long generation, int action) {
                if (action != EditorInfo.IME_ACTION_DONE) return;
                UI.post(() -> { ImeProxyProbe current = weak.get();
                    if (current != null && current.state.isCurrent(generation)) {
                        current.event("done", "generation=" + generation); current.returnUnity.run();
                    }
                });
            }
        });
    }
    void pollMetadata() {
        long now = SystemClock.uptimeMillis();
        if (!isEnabled() || polling || now < nextPoll) return;
        nextPoll = now + 1000; polling = true; long epoch = pollEpoch;
        web.evaluateJavascript("typeof window.vrwebviewProxyMetadata==='function'?window.vrwebviewProxyMetadata():null", result -> {
            if (epoch != pollEpoch || !isEnabled()) return;
            polling = false;
            try {
                JSONObject meta = new JSONObject(result);
                String value = "input=" + meta.getLong("input")
                        + " compositionStart=" + meta.getLong("compositionstart")
                        + " compositionUpdate=" + meta.getLong("compositionupdate")
                        + " compositionEnd=" + meta.getLong("compositionend")
                        + " selectionChanges=" + meta.getLong("selectionchange")
                        + " editable=" + meta.getBoolean("editable");
                if (!value.equals(lastDom)) { event("dom", value); lastDom = value; }
            } catch (Exception ignored) { /* Page not loaded yet. Never log the raw JS result. */ }
        });
    }
    void hideIme() {
        if (proxy == null) return;
        InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null && imm.isActive(proxy)) imm.hideSoftInputFromWindow(proxy.getWindowToken(), 0);
    }
    void cancel(String reason) {
        state.cancel(); armTicket = 0; candidate = null; candidateInfo = null;
        pollEpoch++; polling = false;
        if (lease != null) lease.invalidate(); lease = null; sourceInfo = null;
        waitingForWindow = false; showRequested = false;
        if (proxy != null) { proxy.clearFocus(); proxy.setVisibility(View.GONE); }
        event("cancel", "reason=" + reason);
    }
    void close() {
        hideIme(); cancel("host_close"); state.close(); web.borrowedConnectionListener = null;
        if (proxy != null) { panel.removeView(proxy); proxy = null; }
    }
    private void event(String name, String metadata) { trace.accept("proxy=IP1 event=" + name + " " + metadata); }
    private static void copyInfo(EditorInfo from, EditorInfo to) {
        to.inputType = from.inputType; to.imeOptions = from.imeOptions; to.privateImeOptions = from.privateImeOptions;
        to.actionLabel = from.actionLabel; to.actionId = from.actionId;
        to.initialSelStart = from.initialSelStart; to.initialSelEnd = from.initialSelEnd; to.initialCapsMode = from.initialCapsMode;
        to.hintText = from.hintText; to.label = from.label; to.packageName = from.packageName;
        to.fieldId = from.fieldId; to.fieldName = from.fieldName;
        to.extras = from.extras == null ? null : new Bundle(from.extras); to.hintLocales = from.hintLocales;
        to.contentMimeTypes = from.contentMimeTypes == null ? null : from.contentMimeTypes.clone();
        if (Build.VERSION.SDK_INT >= 30 && from.initialSelStart >= 0 && from.initialSelEnd >= 0) {
            CharSequence before = from.getInitialTextBeforeCursor(1024, 0);
            CharSequence selected = from.getInitialSelectedText(0);
            CharSequence after = from.getInitialTextAfterCursor(1024, 0);
            if (before != null && selected != null && after != null)
                to.setInitialSurroundingSubText(TextUtils.concat(before, selected, after),
                        Math.min(from.initialSelStart, from.initialSelEnd) - before.length());
        }
    }
    private static final class ProxyEditor extends EditText {
        final ImeProxyProbe owner;
        ProxyEditor(Context context, ImeProxyProbe owner) { super(context); this.owner = owner; }
        @Override public boolean onCheckIsTextEditor() { return owner.lease != null && owner.lease.isValid(); }
        @Override public InputConnection onCreateInputConnection(EditorInfo info) { return owner.createConnection(info); }
        @Override public boolean checkInputConnectionProxy(View view) {
            if (owner.lease == null || !owner.lease.isValid()) return false;
            boolean allowed = view == owner.web;
            if (!allowed) { try { allowed = owner.web.checkInputConnectionProxy(view); } catch (RuntimeException ignored) {} }
            if (owner.lastRelation == null || owner.lastRelation != allowed) {
                owner.lastRelation = allowed; owner.event("proxy_relation", "accepted=" + allowed);
            }
            return allowed;
        }
        @Override public void onWindowFocusChanged(boolean focused) {
            super.onWindowFocusChanged(focused);
            owner.event("proxy_window", "focused=" + focused);
            BorrowedInputConnection current = owner.lease;
            if (focused && owner.waitingForWindow && current != null) UI.post(() -> owner.show(current.generation));
        }
        @Override public boolean onKeyPreIme(int keyCode, KeyEvent event) {
            if (keyCode == KeyEvent.KEYCODE_BACK && event.getAction() == KeyEvent.ACTION_UP) {
                BorrowedInputConnection current = owner.lease;
                if (current != null) UI.post(() -> {
                    if (owner.state.isCurrent(current.generation)) owner.returnUnity.run();
                });
                return true;
            }
            return super.onKeyPreIme(keyCode, event);
        }
    }
}
