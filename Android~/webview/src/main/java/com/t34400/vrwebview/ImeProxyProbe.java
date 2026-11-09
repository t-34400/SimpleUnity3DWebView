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
import android.text.Editable;
import android.text.TextWatcher;
import android.graphics.Rect;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.InputMethodManager;
import android.view.inputmethod.BaseInputConnection;
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
    private volatile boolean nativeControl;
    private volatile long activeTicket;
    private final AtomicInteger queryTraces = new AtomicInteger();
    private int nativeChanges;
    private int[] controlFlags;
    private String controlPrivateOptions;

    ImeProxyProbe(Activity owner, ProbeWebView web, FrameLayout panel, Consumer<String> trace, Runnable returnUnity) {
        this.owner = owner; this.web = web; this.panel = panel; this.trace = trace; this.returnUnity = returnUnity;
        web.borrowedConnectionListener = this::sourceCreated;
    }
    void setEnabled(boolean enabled) {
        cancel("mode_change"); state.setEnabled(enabled);
        if (enabled && proxy == null) {
            event("initializing", "stage=proxy_constructor");
            proxy = new ProxyEditor(owner, this);
            event("initializing", "stage=proxy_configuration");
            proxy.setSingleLine(true); proxy.setFocusableInTouchMode(true);
            proxy.setImeOptions(EditorInfo.IME_ACTION_DONE);
            proxy.setOnEditorActionListener((view, action, key) -> {
                if (!nativeControl || action != EditorInfo.IME_ACTION_DONE) return false;
                long ticket = activeTicket;
                UI.post(() -> { if (nativeControl && state.isCurrent(ticket)) {
                    event("native_done", "generation=" + ticket); returnUnity.run();
                } });
                return true;
            });
            proxy.addTextChangedListener(new TextWatcher() {
                @Override public void beforeTextChanged(CharSequence text, int start, int count, int after) {}
                @Override public void onTextChanged(CharSequence text, int start, int before, int count) {}
                @Override public void afterTextChanged(Editable text) {
                    if (!nativeControl || !state.isCurrent(activeTicket)) return;
                    event("native_text", "generation=" + activeTicket + " changes=" + (++nativeChanges)
                            + " composing=" + (BaseInputConnection.getComposingSpanStart(text) >= 0));
                }
            });
            proxy.setTextColor(Color.TRANSPARENT); proxy.setBackgroundColor(Color.TRANSPARENT);
            proxy.setVisibility(View.GONE);
            event("initializing", "stage=proxy_attach");
            panel.addView(proxy, new FrameLayout.LayoutParams(320, 72, Gravity.END | Gravity.BOTTOM));
        }
        event("mode", "enabled=" + enabled + " variant=isolated sourceProxy=false");
    }
    boolean isEnabled() { return state.isEnabled(); }
    void arm() {
        if (!isEnabled()) return;
        cancel("new_gesture"); armTicket = state.arm();
        queryTraces.set(0);
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
    /** Control only: the same real EditText owns its normal connection, with no DOM text synchronization. */
    void requestNativeControl() {
        if (!isEnabled() || proxy == null) { event("native_rejected", "reason=proxy_disabled"); return; }
        if (controlFlags == null) { event("native_rejected", "reason=source_flags_unavailable"); return; }
        arm();
        long ticket = state.capture();
        if (ticket <= 0) return;
        armTicket = 0; activeTicket = ticket;
        proxy.setText(""); // Discard prior synthetic control text before enabling metadata observation.
        proxy.setInputType(controlFlags[0]); proxy.setImeOptions(controlFlags[1]);
        proxy.setPrivateImeOptions(controlPrivateOptions);
        nativeChanges = 0; nativeControl = true;
        showRequested = false; waitingForWindow = false;
        proxy.setVisibility(View.VISIBLE);
        boolean focused = proxy.requestFocus();
        event("native_start", "generation=" + ticket + " accepted=" + focused + " variant=isolated matchedFlags=true");
        UI.post(() -> show(ticket));
    }
    private void sourceCreated(InputConnection connection, EditorInfo info) {
        // Allow the existing direct-WebView route (Proxy OFF) to calibrate the native control.
        // Only routing flags are retained; no connection, text or surrounding context is borrowed.
        if (connection != null && (info.inputType & 0xf) == 1) {
            controlFlags = new int[]{info.inputType, info.imeOptions}; controlPrivateOptions = info.privateImeOptions;
        }
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
        controlFlags = new int[]{info.inputType, info.imeOptions}; controlPrivateOptions = info.privateImeOptions;
        activeTicket = ticket; nativeControl = false;
        showRequested = false; waitingForWindow = false;
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
        if (!state.isCurrent(ticket) || proxy == null || (!nativeControl && (lease == null || !lease.isValid()))) return;
        if (showRequested) return;
        if (!proxy.hasWindowFocus()) { waitingForWindow = true; event("await_window", "generation=" + ticket); return; }
        waitingForWindow = false; showRequested = true;
        InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null) imm.restartInput(proxy);
        boolean accepted = imm != null && imm.showSoftInput(proxy, 0);
        event("ime_request", "generation=" + ticket + " variant=isolated editor=" + (nativeControl ? "native" : "borrowed")
                + " accepted=" + accepted + " proxyFocus=" + proxy.hasFocus()
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
        if (imm != null && imm.isActive(proxy)) {
            event("app_hide", "target=proxy"); imm.hideSoftInputFromWindow(proxy.getWindowToken(), 0);
        }
    }
    void cancel(String reason) {
        state.cancel(); armTicket = 0; candidate = null; candidateInfo = null;
        activeTicket = 0; nativeControl = false;
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
        // Keep TextView's native editor check. The superclass constructor can invoke
        // virtual methods before this subclass has assigned owner.
        @Override public InputConnection onCreateInputConnection(EditorInfo info) {
            if (owner == null) return null;
            if (!owner.nativeControl) return owner.createConnection(info);
            if (!owner.state.isCurrent(owner.activeTicket)) return null;
            InputConnection connection = super.onCreateInputConnection(info);
            // Keep TextView's own context/selection and connection; match only the source routing flags.
            info.inputType = owner.controlFlags[0]; info.imeOptions = owner.controlFlags[1];
            info.privateImeOptions = owner.controlPrivateOptions;
            owner.event("native_connection", "generation=" + owner.activeTicket + " connected=" + (connection != null)
                    + " type=0x" + Integer.toHexString(info.inputType) + " imeOptions=0x" + Integer.toHexString(info.imeOptions));
            return connection;
        }
        @Override public boolean checkInputConnectionProxy(View view) {
            if (owner != null && owner.activeTicket > 0 && owner.queryTraces.get() < 16) {
                java.util.List<String> rows = ImeCallMetadata.describe(new Throwable().getStackTrace());
                if (!rows.isEmpty()) {
                    int query = owner.queryTraces.incrementAndGet();
                    if (query > 16) return false;
                    owner.event("proxy_query", "query=" + query + " accepted=false target=" + (view == owner.web ? "web" : "other"));
                    for (String row : rows) owner.event("ime_call_frame", "query=" + query + " " + row);
                }
            }
            // Only this EditText is served. Granting the source/provider proxy also authorizes
            // Chromium's source-view isActive/hide/restart paths after focus has moved away.
            // Isolation also blocks source selection/session updates; viability is still experimental.
            return false;
        }
        @Override protected void onFocusChanged(boolean focused, int direction, Rect previous) {
            super.onFocusChanged(focused, direction, previous);
            if (owner != null) owner.event("proxy_focus_changed", "focused=" + focused);
        }
        @Override public void onWindowFocusChanged(boolean focused) {
            super.onWindowFocusChanged(focused);
            if (owner == null) return;
            owner.event("proxy_window", "focused=" + focused);
            long ticket = owner.activeTicket;
            if (focused && owner.waitingForWindow && ticket > 0) UI.post(() -> owner.show(ticket));
        }
        @Override public boolean onKeyPreIme(int keyCode, KeyEvent event) {
            if (owner != null && keyCode == KeyEvent.KEYCODE_BACK && event.getAction() == KeyEvent.ACTION_UP) {
                long ticket = owner.activeTicket;
                if (ticket > 0) UI.post(() -> {
                    if (owner.state.isCurrent(ticket)) owner.returnUnity.run();
                });
                return true;
            }
            return super.onKeyPreIme(keyCode, event);
        }
    }
}
