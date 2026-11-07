package com.t34400.vrwebview;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.PorterDuff;
import android.graphics.Rect;
import android.view.Surface;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.webkit.WebView;

/** Records connection metadata only; never wraps, retains, or logs input text. */
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

    ProbeWebView(Context context) { super(context); }

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
        return connection;
    }

    @Override protected void onFocusChanged(boolean focused, int direction, Rect previous) {
        super.onFocusChanged(focused, direction, previous);
        metadata("webFocus=" + focused);
    }

    @Override public void onWindowFocusChanged(boolean focused) {
        windowFocusChanges++; super.onWindowFocusChanged(focused);
        metadata("windowFocus=" + focused);
        if (windowFocusListener != null) windowFocusListener.onChanged(focused);
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
