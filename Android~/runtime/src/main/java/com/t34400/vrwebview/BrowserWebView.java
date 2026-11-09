package com.t34400.vrwebview;

import android.content.Context;
import android.annotation.SuppressLint;
import android.content.pm.PackageInfo;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.PorterDuff;
import android.graphics.Rect;
import android.os.SystemClock;
import android.view.KeyEvent;
import android.view.Surface;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.InputMethodManager;
import android.webkit.WebView;

/** Production direct-InputConnection WebView. No proxy, borrowed editor or focus retry. */
final class BrowserWebView extends WebView {
    private ImeWindowFocusGuard guard;
    private java.util.function.Consumer<String> diagnostics;
    private void trace(String event) { if (diagnostics != null) diagnostics.accept(event); }
    private boolean connected;
    private int inputType;
    Runnable viewFocusLost;
    BrowserWebView(Context context) { super(context); }

    void configureGuard(boolean enabled, java.util.function.Consumer<String> diagnostics) {
        this.diagnostics = diagnostics;
        PackageInfo provider = WebView.getCurrentWebViewPackage();
        guard = new ImeWindowFocusGuard(provider == null ? null : provider.packageName,
                provider == null ? null : provider.versionName, hasWindowFocus());
        guard.setEnabled(enabled);
        trace("event=guard supported=" + guard.isSupported() + " enabled=" + enabled
                + " providerVersion=" + guard.providerVersion());
    }
    void armGuard() { if (guard != null && guard.request(SystemClock.uptimeMillis())) trace("event=armed"); }
    void cancelArming() { if (guard != null) guard.cancelRequest(); }
    void releaseGuard() { if (guard != null) forwardLoss(guard.release()); }
    void setResumed(boolean resumed) { if (guard != null) forwardLoss(guard.setResumed(resumed)); }
    private void forwardLoss(boolean pending) { if (pending) { trace("event=released"); super.onWindowFocusChanged(false); } }

    @Override public InputConnection onCreateInputConnection(EditorInfo info) {
        InputConnection connection = super.onCreateInputConnection(info);
        connected = connection != null; inputType = info.inputType;
        trace("event=connection connected=" + connected + " inputType=" + inputType);
        return connection; // The provider connection is never wrapped or retained.
    }
    @Override public void onWindowFocusChanged(boolean focused) {
        ImeWindowFocusGuard.Action action = ImeWindowFocusGuard.Action.FORWARD;
        if (guard != null) {
            InputMethodManager imm = (InputMethodManager) getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
            action = guard.windowChanged(focused, hasFocus(), connected && inputType != 0 && isAttachedToWindow(),
                    !focused && imm != null && imm.isActive(this) && imm.isAcceptingText(), SystemClock.uptimeMillis());
        }
        trace("event=window actual=" + focused + " action=" + action + " viewFocus=" + hasFocus() + " connected=" + connected);
        if (action == ImeWindowFocusGuard.Action.FORWARD) super.onWindowFocusChanged(focused);
        if (focused && !hasFocus() && viewFocusLost != null) viewFocusLost.run();
    }
    @Override protected void onFocusChanged(boolean focused, int direction, Rect previous) {
        if (!focused) releaseGuard();
        super.onFocusChanged(focused, direction, previous);
        trace("event=view_focus actual=" + focused);
        if (!focused && viewFocusLost != null) viewFocusLost.run();
    }
    @Override protected void onWindowVisibilityChanged(int visibility) {
        if (visibility != View.VISIBLE) releaseGuard();
        super.onWindowVisibilityChanged(visibility);
    }
    @Override protected void onDetachedFromWindow() { releaseGuard(); super.onDetachedFromWindow(); }
    @Override public boolean onKeyPreIme(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK && event.getAction() == KeyEvent.ACTION_UP) releaseGuard();
        return super.onKeyPreIme(keyCode, event);
    }
    // super.draw is deliberately called only by drawTo on the hardware target Canvas.
    @SuppressLint("MissingSuperCall")
    @Override public void draw(Canvas canvas) { /* Unity owns visible presentation. */ }
    void drawTo(Surface surface) {
        Canvas canvas = surface.lockHardwareCanvas();
        try {
            canvas.drawColor(Color.WHITE, PorterDuff.Mode.SRC);
            computeScroll();
            int checkpoint = canvas.save();
            try { canvas.translate(-getScrollX(), -getScrollY()); super.draw(canvas); }
            finally { canvas.restoreToCount(checkpoint); }
        } finally { surface.unlockCanvasAndPost(canvas); }
    }
}
