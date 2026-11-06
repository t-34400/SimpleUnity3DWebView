package com.t34400.vrwebview;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.PorterDuff;
import android.view.Surface;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.webkit.WebView;

/** Records connection metadata only; never wraps, retains, or logs input text. */
final class ProbeWebView extends WebView {
    int connectionRequests, nonNullConnections, windowFocusChanges, inputType, imeOptions;
    long draws;
    boolean targetHardware, suppressWindowDraw;

    ProbeWebView(Context context) { super(context); }

    @Override public InputConnection onCreateInputConnection(EditorInfo info) {
        InputConnection connection = super.onCreateInputConnection(info);
        connectionRequests++;
        if (connection != null) nonNullConnections++;
        inputType = info.inputType; imeOptions = info.imeOptions;
        return connection;
    }

    @Override public void onWindowFocusChanged(boolean focused) {
        windowFocusChanges++; super.onWindowFocusChanged(focused);
    }

    @Override public void draw(Canvas canvas) {
        if (!suppressWindowDraw) super.draw(canvas);
    }

    void drawTo(Surface surface) {
        Canvas canvas = surface.lockHardwareCanvas();
        try {
            targetHardware = canvas.isHardwareAccelerated();
            canvas.drawColor(Color.WHITE, PorterDuff.Mode.SRC);
            super.draw(canvas);
            draws++;
        } finally { surface.unlockCanvasAndPost(canvas); }
    }
}
