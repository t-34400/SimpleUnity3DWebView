package com.t34400.vrwebview;

import android.app.Activity;
import android.app.Presentation;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.graphics.PixelFormat;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.media.Image;
import android.media.ImageReader;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.lang.ref.WeakReference;
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;

/** Diagnostic only: native WebView host vs virtual display, without DOM editing proxies.
 * CPU preview is deliberately separate from the future production GPU backend. */
public final class ImeProbe {
    private static WeakReference<Probe> active = new WeakReference<>(null);
    private ImeProbe() {}

    public static void launch(Activity activity, boolean virtualDisplay) {
        activity.runOnUiThread(() -> activity.startActivity(new Intent(activity, ImeProbeActivity.class)
                .putExtra("virtualDisplay", virtualDisplay)));
    }

    /** Returns immediately; creation and errors are reported on the Android UI thread. */
    public static void open(Activity activity, boolean virtualDisplay) {
        activity.runOnUiThread(() -> {
            Probe old = active.get();
            if (old != null) old.close();
            Probe probe = new Probe(activity, virtualDisplay);
            active = new WeakReference<>(probe);
            try { probe.start(); }
            catch (Exception error) {
                Log.e("VRWebViewProbe", "Probe initialization failed: " + error.getClass().getSimpleName());
                probe.showFailure(error.getClass().getSimpleName());
            }
        });
    }

    public static void close(Activity owner) {
        Probe probe = active.get();
        if (probe != null && probe.activity == owner) owner.runOnUiThread(probe::close);
    }

    private static final class Probe {
        private final Activity activity;
        private final boolean virtual;
        private final Handler ui = new Handler(Looper.getMainLooper());
        private LinearLayout panel;
        private TextView status;
        private WebView web;
        private ImageReader reader;
        private VirtualDisplay display;
        private Presentation presentation;
        private ImageView preview;
        private Bitmap previewBitmap;
        private boolean closed;
        private int frames;
        private boolean showAccepted;
        private long downTime;

        Probe(Activity activity, boolean virtual) { this.activity = activity; this.virtual = virtual; }

        void start() throws Exception {
            panel = new LinearLayout(activity);
            panel.setOrientation(LinearLayout.VERTICAL);
            panel.setBackgroundColor(0xff202020);
            LinearLayout controls = new LinearLayout(activity);
            panel.addView(controls);
            button(controls, "Close", this::close);
            button(controls, "Request IME", () -> {
                if (web == null) return;
                web.requestFocus();
                InputMethodManager imm = (InputMethodManager) web.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
                showAccepted = imm != null && imm.showSoftInput(web, 0);
                updateStatus();
            });
            button(controls, "Reload test", () -> { if (web != null) web.reload(); });
            status = new TextView(activity);
            status.setTextColor(0xffffffff);
            panel.addView(status);
            activity.addContentView(panel, new ViewGroup.LayoutParams(-1, -1));

            Context context = activity;
            if (virtual) {
                reader = ImageReader.newInstance(1024, 768, PixelFormat.RGBA_8888, 3);
                DisplayManager manager = (DisplayManager) activity.getSystemService(Context.DISPLAY_SERVICE);
                display = manager.createVirtualDisplay("VRWebViewImeProbe", 1024, 768, 160,
                        reader.getSurface(), DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY);
                if (display == null) throw new IllegalStateException("VirtualDisplay unavailable");
                presentation = new Presentation(activity, display.getDisplay());
                context = presentation.getContext();
            }
            web = new WebView(context);
            web.setFocusable(true);
            web.setFocusableInTouchMode(true);
            web.getSettings().setJavaScriptEnabled(true);
            web.setWebViewClient(new WebViewClient());
            web.setWebChromeClient(new WebChromeClient() {
                @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
            });
            // No JavascriptInterface, no text logging, no WebView global debugging override.
            if (virtual) {
                presentation.setContentView(web, new ViewGroup.LayoutParams(1024, 768));
                presentation.show();
                preview = new ImageView(activity);
                preview.setScaleType(ImageView.ScaleType.FIT_CENTER);
                panel.addView(preview, new LinearLayout.LayoutParams(-1, 0, 1));
                preview.setOnTouchListener((v, event) -> forwardTouch(event));
                ui.post(capture);
            } else {
                panel.addView(web, new LinearLayout.LayoutParams(-1, 0, 1));
            }
            String html;
            try (InputStream input = activity.getAssets().open("vrwebview-ime-probe.html")) {
                ByteArrayOutputStream output = new ByteArrayOutputStream();
                byte[] buffer = new byte[4096]; int count;
                while ((count = input.read(buffer)) != -1) output.write(buffer, 0, count);
                html = new String(output.toByteArray(), StandardCharsets.UTF_8);
            }
            web.loadDataWithBaseURL("https://vrwebview.invalid/", html, "text/html", "UTF-8", null);
            ui.post(statusTick);
        }

        private void button(LinearLayout parent, String label, Runnable action) {
            Button button = new Button(activity); button.setText(label);
            button.setOnClickListener(v -> action.run()); parent.addView(button);
        }

        private boolean forwardTouch(MotionEvent original) {
            if (closed || web == null || preview.getWidth() == 0 || preview.getHeight() == 0) return false;
            float scale = Math.min(preview.getWidth() / 1024f, preview.getHeight() / 768f);
            float left = (preview.getWidth() - 1024f * scale) / 2;
            float top = (preview.getHeight() - 768f * scale) / 2;
            float x = (original.getX() - left) / scale;
            float y = (original.getY() - top) / scale;
            if (original.getActionMasked() == MotionEvent.ACTION_DOWN) {
                if (x < 0 || y < 0 || x >= 1024 || y >= 768) return false;
                downTime = original.getEventTime();
            }
            MotionEvent event = MotionEvent.obtain(downTime, original.getEventTime(), original.getActionMasked(), x, y, 0);
            try { web.dispatchTouchEvent(event); }
            finally { event.recycle(); }
            return true;
        }

        private final Runnable capture = new Runnable() {
            @Override public void run() {
                if (closed || reader == null) return;
                try (Image image = reader.acquireLatestImage()) {
                    if (image != null) {
                        Image.Plane plane = image.getPlanes()[0];
                        ByteBuffer pixels = plane.getBuffer();
                        // Last-row padding is not guaranteed to be present in the buffer.
                        ByteBuffer packed = ByteBuffer.allocate(image.getWidth() * image.getHeight() * 4);
                        for (int y = 0; y < image.getHeight(); y++) {
                            ByteBuffer row = pixels.duplicate();
                            row.position(y * plane.getRowStride());
                            row.limit(row.position() + image.getWidth() * 4);
                            packed.put(row);
                        }
                        packed.flip();
                        Bitmap next = Bitmap.createBitmap(image.getWidth(), image.getHeight(), Bitmap.Config.ARGB_8888);
                        next.copyPixelsFromBuffer(packed);
                        preview.setImageBitmap(next);
                        if (previewBitmap != null) previewBitmap.recycle();
                        previewBitmap = next;
                        frames++;
                    }
                } catch (RuntimeException error) {
                    Log.e("VRWebViewProbe", "Capture failed: " + error.getClass().getSimpleName());
                    showFailure(error.getClass().getSimpleName());
                    return;
                }
                if (!closed) ui.postDelayed(this, 100);
            }
        };

        private final Runnable statusTick = new Runnable() {
            @Override public void run() {
                if (closed) return;
                updateStatus(); ui.postDelayed(this, 500);
            }
        };

        private void updateStatus() {
            if (web == null || status == null) return;
            InputMethodManager imm = (InputMethodManager) web.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
            status.setText((virtual ? "VirtualDisplay (CPU diagnostic preview)" : "Host window")
                    + "\nviewFocus=" + web.hasFocus() + " windowFocus=" + web.hasWindowFocus()
                    + " served=" + (imm != null && imm.isActive(web))
                    + " display=" + (web.getDisplay() == null ? -1 : web.getDisplay().getDisplayId())
                    + " showRequestAccepted=" + showAccepted + " frames=" + frames
                    + "\nTap a field before Request IME. Request acceptance is not proof of visibility.");
        }

        void showFailure(String kind) {
            if (status != null) status.setText("Probe failed: " + kind + ". Close and inspect Logcat.");
            ui.removeCallbacks(capture); ui.removeCallbacks(statusTick);
        }

        void close() {
            if (closed) return;
            closed = true;
            ui.removeCallbacks(capture); ui.removeCallbacks(statusTick);
            if (web != null) {
                InputMethodManager imm = (InputMethodManager) web.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
                if (imm != null) imm.hideSoftInputFromWindow(web.getWindowToken(), 0);
                if (web.getParent() instanceof ViewGroup) ((ViewGroup) web.getParent()).removeView(web);
                web.stopLoading(); web.destroy(); web = null;
            }
            if (presentation != null) { presentation.dismiss(); presentation = null; }
            if (display != null) { display.release(); display = null; }
            if (reader != null) { reader.close(); reader = null; }
            if (preview != null) preview.setImageDrawable(null);
            if (previewBitmap != null) { previewBitmap.recycle(); previewBitmap = null; }
            if (panel != null && panel.getParent() instanceof ViewGroup) ((ViewGroup) panel.getParent()).removeView(panel);
            if (active.get() == this) active.clear();
            if (activity instanceof ImeProbeActivity && !activity.isFinishing()) activity.finish();
        }
    }
}
