package com.t34400.vrwebview;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.PorterDuff;
import android.graphics.Rect;
import android.content.pm.PackageInfo;
import android.view.KeyEvent;
import android.view.Surface;
import android.view.View;
import android.view.inputmethod.InputMethodManager;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.webkit.WebView;

/** Metadata diagnostics and an opt-in experimental connection borrower; never logs input text. */
final class ProbeWebView extends WebView {
    int connectionRequests, nonNullConnections, windowFocusChanges, inputType, imeOptions;
    int capturedScrollX, capturedScrollY;
    long draws;
    boolean targetHardware, suppressWindowDraw;
    boolean lastConnectionNonNull;
    // Diagnostic events contain metadata only. Do not pass EditorInfo or connection objects.
    interface MetadataListener { void onEvent(String event); }
    MetadataListener metadataListener;
    interface ConnectionMetadataListener { void onCreated(boolean connected, int inputType, int imeOptions); }
    ConnectionMetadataListener connectionMetadataListener;
    interface WindowFocusListener { void onChanged(boolean focused); }
    WindowFocusListener windowFocusListener;

    interface BorrowedConnectionListener { void onCreated(InputConnection connection, EditorInfo info); }
    BorrowedConnectionListener borrowedConnectionListener;
    private ImeWindowFocusGuard imeWindowGuard;

    ProbeWebView(Context context) { super(context); }

    void enableImeWindowGuard() {
        PackageInfo provider = WebView.getCurrentWebViewPackage();
        imeWindowGuard = new ImeWindowFocusGuard(provider == null ? null : provider.packageName,
                provider == null ? null : provider.versionName, hasWindowFocus());
        metadata("probe=WF1 event=installed supported=" + imeWindowGuard.isSupported() + " enabled=true route=direct");
    }
    void setImeWindowGuardEnabled(boolean enabled) {
        if (imeWindowGuard == null) return;
        forwardHeldLoss(imeWindowGuard.setEnabled(enabled), "mode_change");
        metadata("probe=WF1 event=mode enabled=" + enabled + " supported=" + imeWindowGuard.isSupported());
    }
    void setImeWindowGuardResumed(boolean resumed) {
        if (imeWindowGuard == null) return;
        forwardHeldLoss(imeWindowGuard.setResumed(resumed), "activity_pause");
        metadata("probe=WF1 event=lifecycle resumed=" + resumed);
    }
    void armImeWindowGuard() {
        if (imeWindowGuard != null && imeWindowGuard.request(android.os.SystemClock.uptimeMillis()))
            metadata("probe=WF1 event=armed");
    }
    void cancelImeWindowGuardRequest() {
        if (imeWindowGuard != null) imeWindowGuard.cancelRequest();
    }
    void releaseImeWindowGuard(String reason) {
        if (imeWindowGuard != null) forwardHeldLoss(imeWindowGuard.release(), reason);
    }
    String imeWindowGuardStatus() {
        return imeWindowGuard == null ? "" : "\nWF1 enabled=" + imeWindowGuard.isEnabled()
                + " supported=" + imeWindowGuard.isSupported() + " held=" + imeWindowGuard.isHolding();
    }
    private void forwardHeldLoss(boolean pending, String reason) {
        if (!pending) return;
        metadata("probe=WF1 event=released reason=" + reason + " actualWindow=" + hasWindowFocus());
        super.onWindowFocusChanged(false);
    }

    @Override public InputConnection onCreateInputConnection(EditorInfo info) {
        InputConnection connection = super.onCreateInputConnection(info);
        connectionRequests++;
        lastConnectionNonNull = connection != null;
        if (connection != null) nonNullConnections++;
        inputType = info.inputType; imeOptions = info.imeOptions;
        metadata("connection nonNull=" + (connection != null) + " count=" + connectionRequests
                + "/" + nonNullConnections + " type=0x" + Integer.toHexString(inputType)
                + " imeOptions=0x" + Integer.toHexString(imeOptions)
                + " connectionClass=" + (connection == null ? "none" : connection.getClass().getSimpleName()));
        if (connectionMetadataListener != null)
            connectionMetadataListener.onCreated(connection != null, inputType, imeOptions);
        if (borrowedConnectionListener != null) borrowedConnectionListener.onCreated(connection, info);
        return connection;
    }

    @Override protected void onFocusChanged(boolean focused, int direction, Rect previous) {
        metadata("webFocusEnter=" + focused);
        if (!focused) releaseImeWindowGuard("view_focus_lost");
        super.onFocusChanged(focused, direction, previous);
        metadata("webFocus=" + focused);
    }

    @Override public void onWindowFocusChanged(boolean focused) {
        metadata("windowFocusEnter=" + focused);
        windowFocusChanges++;
        ImeWindowFocusGuard.Action action = ImeWindowFocusGuard.Action.FORWARD;
        if (imeWindowGuard != null) {
            InputMethodManager imm = (InputMethodManager) getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
            action = imeWindowGuard.windowChanged(focused, hasFocus(),
                    lastConnectionNonNull && inputType != 0 && isAttachedToWindow(),
                    !focused && imm != null && imm.isActive(this) && imm.isAcceptingText(),
                    android.os.SystemClock.uptimeMillis());
            metadata("probe=WF1 event=window actual=" + focused + " action=" + action
                    + " viewFocus=" + hasFocus() + " editor=" + lastConnectionNonNull);
        }
        if (action == ImeWindowFocusGuard.Action.FORWARD) super.onWindowFocusChanged(focused);
        metadata("windowFocus=" + focused);
        if (windowFocusListener != null) windowFocusListener.onChanged(focused);
    }

    @Override protected void onWindowVisibilityChanged(int visibility) {
        if (visibility != View.VISIBLE) releaseImeWindowGuard("window_hidden");
        super.onWindowVisibilityChanged(visibility);
    }
    @Override protected void onDetachedFromWindow() {
        releaseImeWindowGuard("detached");
        super.onDetachedFromWindow();
    }
    @Override public boolean onKeyPreIme(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK && event.getAction() == KeyEvent.ACTION_UP)
            releaseImeWindowGuard("back");
        return super.onKeyPreIme(keyCode, event);
    }

    private void metadata(String event) {
        if (metadataListener != null) metadataListener.onEvent(event);
    }

    @Override public void draw(Canvas canvas) {
        if (!suppressWindowDraw) super.draw(canvas);
    }

    void drawTo(Surface surface) {
        Canvas canvas = surface.lockHardwareCanvas();
        try {
            targetHardware = canvas.isHardwareAccelerated();
            canvas.drawColor(Color.WHITE, PorterDuff.Mode.SRC);
            // View.updateDisplayListIfDirty normally supplies this transform.
            // Calling draw directly skips it, while WebView's GPU compositor
            // still adds the view scroll offset to the incoming Canvas matrix.
            computeScroll();
            capturedScrollX = getScrollX(); capturedScrollY = getScrollY();
            int checkpoint = canvas.save();
            try {
                canvas.translate(-capturedScrollX, -capturedScrollY);
                super.draw(canvas);
            } finally { canvas.restoreToCount(checkpoint); }
            draws++;
        } finally { surface.unlockCanvasAndPost(canvas); }
    }
}
