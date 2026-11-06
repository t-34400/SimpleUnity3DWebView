package com.t34400.vrwebview;

import android.app.Activity;
import android.app.Presentation;
import android.content.Context;
import android.content.pm.PackageInfo;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebView;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

/** H0/H1/V1 comparison. Does not claim to render into an OpenXR layer or Unity texture. */
public final class GpuProbeActivity extends Activity {
    private final Handler ui = new Handler(Looper.getMainLooper());
    private FrameLayout content;
    private TextView status;
    private Button sourceButton;
    private Session session;
    private int requestedMode;
    private boolean resumed;

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        requestedMode = state == null ? getIntent().getIntExtra("mode", 0) : state.getInt("mode", 0);
        if (requestedMode < 0 || requestedMode > 2) requestedMode = 0;
        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(0xff202020);
        LinearLayout modes = row(root);
        button(modes, "H0 Normal", () -> select(0));
        button(modes, "H1 Host GPU", () -> select(1));
        button(modes, "V1 VD GPU", () -> select(2));
        LinearLayout controls = row(root);
        button(controls, "Request IME", () -> { if (session != null) session.requestIme(); });
        button(controls, "Reload", () -> { if (session != null && session.web != null) session.web.reload(); });
        sourceButton = button(controls, "Window draw: on", () -> {
            if (session == null || session.mode != 1 || session.web == null) return;
            session.web.suppressWindowDraw = !session.web.suppressWindowDraw;
            session.web.invalidate();
            sourceButton.setText(session.web.suppressWindowDraw ? "Window draw: off" : "Window draw: on");
        });
        button(controls, "Close", this::finish);
        ScrollView diagnostics = new ScrollView(this);
        status = new TextView(this); status.setTextColor(0xffffffff); status.setTextSize(12);
        status.setPadding(8, 4, 8, 4); status.setTextIsSelectable(false);
        diagnostics.addView(status);
        root.addView(diagnostics, new LinearLayout.LayoutParams(-1, dp(150)));
        content = new FrameLayout(this); root.addView(content, new LinearLayout.LayoutParams(-1, 0, 1));
        setContentView(root);
    }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private LinearLayout row(LinearLayout parent) {
        LinearLayout result = new LinearLayout(this); parent.addView(result); return result;
    }
    private Button button(LinearLayout parent, String label, Runnable action) {
        Button result = new Button(this); result.setText(label); result.setTextSize(12);
        result.setOnClickListener(v -> action.run());
        parent.addView(result, new LinearLayout.LayoutParams(0, -2, 1)); return result;
    }
    private TextView label(LinearLayout parent, String text) {
        TextView result = new TextView(this); result.setTextColor(0xffffffff); result.setTextSize(12);
        result.setText(text); parent.addView(result); return result;
    }
    private void select(int mode) {
        requestedMode = mode;
        if (!resumed) return;
        stopSession();
        session = new Session(mode);
        sourceButton.setEnabled(mode == 1); sourceButton.setText("Window draw: on");
        try { session.start(); } catch (Exception error) { session.fail("create", error.getClass().getSimpleName()); }
    }
    private void stopSession() {
        Session previous = session; session = null;
        if (previous != null) previous.close();
        if (content != null) content.removeAllViews();
    }
    @Override public void onResume() { super.onResume(); resumed = true; select(requestedMode); }
    @Override public void onPause() { resumed = false; stopSession(); super.onPause(); }
    @Override public void onDestroy() { stopSession(); super.onDestroy(); }
    @Override public void onSaveInstanceState(Bundle state) { state.putInt("mode", requestedMode); super.onSaveInstanceState(state); }

    private final class Session implements GpuProbePreview.Listener {
        final int mode;
        ProbeWebView web;
        GpuProbePreview preview;
        GpuProbePreview.Output output;
        VirtualDisplay display;
        Presentation presentation;
        boolean closed, showAccepted, fullscreenRequested;
        String failure = "none";
        int touchOwner = -1, width = 1024, height = 768;
        long downTime;

        Session(int mode) { this.mode = mode; }
        void start() throws Exception {
            if (mode == 0) {
                web = createWeb(GpuProbeActivity.this);
                content.addView(web, new FrameLayout.LayoutParams(-1, -1));
            } else {
                LinearLayout panes = new LinearLayout(GpuProbeActivity.this);
                panes.setOrientation(LinearLayout.VERTICAL); content.addView(panes, new FrameLayout.LayoutParams(-1, -1));
                if (mode == 1) {
                    label(panes, "SOURCE: WebView in the host Window (native display)");
                    web = createWeb(GpuProbeActivity.this);
                    panes.addView(web, new LinearLayout.LayoutParams(-1, 0, 1));
                    web.addOnLayoutChangeListener((v, l, t, r, b, ol, ot, or, ob) -> resizeHost());
                }
                label(panes, "GPU OUTPUT: SurfaceTexture / GLES (tap this image to forward touch)");
                preview = new GpuProbePreview(GpuProbeActivity.this, this);
                panes.addView(preview, new LinearLayout.LayoutParams(-1, 0, 1));
                preview.setOnTouchListener((view, event) -> forwardTouch(event));
            }
            ui.post(tick);
        }

        private ProbeWebView createWeb(Context context) throws Exception {
            ProbeWebView result = new ProbeWebView(context);
            result.setFocusable(true); result.setFocusableInTouchMode(true);
            result.getSettings().setJavaScriptEnabled(true);
            result.getSettings().setAllowFileAccess(false); result.getSettings().setAllowContentAccess(false);
            result.getSettings().setMediaPlaybackRequiresUserGesture(true);
            result.setWebViewClient(new ProbeAssets(getAssets()));
            result.setWebChromeClient(new WebChromeClient() {
                @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
                @Override public void onShowCustomView(View view, CustomViewCallback callback) {
                    fullscreenRequested = true;
                    // This experiment captures the inline WebView only. Do not silently show
                    // fullscreen video outside the captured image and call that a success.
                    callback.onCustomViewHidden();
                }
            });
            result.loadUrl(ProbeAssets.PAGE); return result;
        }

        @Override public void onOutput(GpuProbePreview.Output created) {
            if (closed || session != this) { created.release(); return; }
            output = created;
            try {
                if (mode == 1) {
                    resizeHost(); ui.removeCallbacks(draw); ui.post(draw);
                } else if (display != null) {
                    display.setSurface(created.surface);
                } else {
                    preview.resizeSource(width, height);
                    DisplayManager manager = (DisplayManager) getSystemService(Context.DISPLAY_SERVICE);
                    if (manager == null) throw new IllegalStateException("No DisplayManager");
                    display = manager.createVirtualDisplay("VRWebViewGpuProbe", width, height, 160,
                            created.surface, DisplayManager.VIRTUAL_DISPLAY_FLAG_OWN_CONTENT_ONLY);
                    if (display == null) throw new IllegalStateException("No VirtualDisplay");
                    presentation = new Presentation(GpuProbeActivity.this, display.getDisplay());
                    if (presentation.getWindow() != null)
                        presentation.getWindow().addFlags(WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED);
                    web = createWeb(presentation.getContext());
                    presentation.setContentView(web, new ViewGroup.LayoutParams(width, height));
                    presentation.show();
                }
            } catch (Exception error) { fail("bind output", error.getClass().getSimpleName()); }
        }

        private void resizeHost() {
            if (closed || mode != 1 || web == null || preview == null || web.getWidth() <= 0 || web.getHeight() <= 0) return;
            width = web.getWidth(); height = web.getHeight(); preview.resizeSource(width, height);
        }

        private final Runnable draw = new Runnable() {
            @Override public void run() {
                if (closed || mode != 1 || !"none".equals(failure)) return;
                try {
                    if (web != null && web.isAttachedToWindow() && web.getWidth() > 0 && web.getHeight() > 0
                            && output != null && output.valid()) web.drawTo(output.surface);
                } catch (RuntimeException error) { fail("hardware Canvas", error.getClass().getSimpleName()); return; }
                if (!closed) ui.postDelayed(this, 33);
            }
        };

        void requestIme() {
            if (closed || web == null) return;
            web.requestFocus();
            InputMethodManager imm = web.getContext().getSystemService(InputMethodManager.class);
            showAccepted = imm != null && imm.showSoftInput(web, 0);
        }

        private boolean forwardTouch(MotionEvent original) {
            if (closed || web == null || preview == null || preview.getWidth() <= 0 || preview.getHeight() <= 0) return false;
            ProbeGeometry fit = new ProbeGeometry(preview.getWidth(), preview.getHeight(), width, height);
            float x = fit.sourceX(original.getX()), y = fit.sourceY(original.getY());
            if (original.getPointerCount() > 1) {
                if (touchOwner != -1) dispatch(MotionEvent.ACTION_CANCEL, original.getEventTime(), x, y);
                touchOwner = -1; return true;
            }
            int action = original.getActionMasked();
            if (action == MotionEvent.ACTION_DOWN) {
                if (!fit.contains(original.getX(), original.getY())) return false;
                downTime = original.getEventTime(); touchOwner = original.getPointerId(0);
            }
            if (touchOwner == -1 || touchOwner != original.getPointerId(0)) return false;
            dispatch(action, original.getEventTime(), x, y);
            if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL) touchOwner = -1;
            return true;
        }

        private void dispatch(int action, long eventTime, float x, float y) {
            MotionEvent event = MotionEvent.obtain(downTime, eventTime, action, x, y, 0);
            event.setSource(InputDevice.SOURCE_TOUCHSCREEN);
            try { web.dispatchTouchEvent(event); } finally { event.recycle(); }
        }

        private final Runnable tick = new Runnable() {
            @Override public void run() {
                if (closed || session != Session.this) return;
                updateStatus(); ui.postDelayed(this, 500);
            }
        };

        private void updateStatus() {
            String name = mode == 0 ? "H0 normal Window" : mode == 1 ? "H1 host Window / hardware Canvas" : "V1 Virtual Display / GPU";
            PackageInfo provider = WebView.getCurrentWebViewPackage();
            StringBuilder text = new StringBuilder(name).append("\nfailure=").append(failure);
            if (web != null) {
                InputMethodManager imm = web.getContext().getSystemService(InputMethodManager.class);
                text.append("\nviewFocus=").append(web.hasFocus()).append("  windowFocus=").append(web.hasWindowFocus())
                        .append("\nimmActive=").append(imm != null && imm.isActive(web)).append("  showRequestAccepted=").append(showAccepted)
                        .append("\nviewDisplayId=").append(web.getDisplay() == null ? -1 : web.getDisplay().getDisplayId())
                        .append("  contextDisplayId=").append(contextDisplayId())
                        .append("\nattached=").append(web.isAttachedToWindow()).append("  shown=").append(web.isShown())
                        .append("  visibility=").append(web.getVisibility()).append("  size=").append(web.getWidth()).append('x').append(web.getHeight())
                        .append("\nwebHardware=").append(web.isHardwareAccelerated()).append("  targetCanvasHardware=")
                        .append(mode == 1 ? Boolean.toString(web.targetHardware) : "n/a")
                        .append("\nconnectionRequests=").append(web.connectionRequests).append("  nonNullConnections=").append(web.nonNullConnections)
                        .append("\ninputType=").append(web.inputType).append("  imeOptions=").append(web.imeOptions)
                        .append("  windowFocusChanges=").append(web.windowFocusChanges)
                        .append("\ncanvasSubmissions=").append(mode == 1 ? Long.toString(web.draws) : "n/a")
                        .append("  sourceWindowDraw=").append(!web.suppressWindowDraw);
            }
            text.append("\ngpuFramesAcquired=").append(preview == null ? "n/a" : Long.toString(preview.acquiredFrames))
                    .append("  textureTimestampNs=").append(preview == null ? "n/a" : Long.toString(preview.textureTimestamp))
                    .append("\nactivityWindowFlags=0x").append(Integer.toHexString(getWindow().getAttributes().flags))
                    .append("  presentationWindowFlags=").append(presentation == null || presentation.getWindow() == null
                            ? "n/a" : "0x" + Integer.toHexString(presentation.getWindow().getAttributes().flags))
                    .append("\nAPI=").append(Build.VERSION.SDK_INT).append("  Android=").append(Build.VERSION.RELEASE)
                    .append("\nWebView=").append(provider == null ? "unknown" : provider.packageName + " / " + provider.versionName)
                    .append("\nGLES=").append(preview == null ? "n/a" : preview.graphics)
                    .append("\nUnity graphics=").append(getIntent().getStringExtra("unityGraphicsApi"))
                    .append("\nOrigin Activity=").append(getIntent().getStringExtra("originActivity"))
                    .append("\nfullscreenRequested=").append(fullscreenRequested).append(" (fullscreen is not captured by this probe)")
                    .append("\nRequest acceptance / frame counts are not proof of visible IME or video.");
            status.setText(text.toString());
        }

        private String contextDisplayId() {
            if (Build.VERSION.SDK_INT < 30) return "n/a (API 29)";
            try {
                return web.getContext().getDisplay() == null ? "none" : Integer.toString(web.getContext().getDisplay().getDisplayId());
            } catch (UnsupportedOperationException error) { return "unavailable"; }
        }

        @Override public void onFailure(String stage, String kind) { fail(stage, kind); }
        void fail(String stage, String kind) {
            if (closed) return;
            failure = stage + ": " + kind; ui.removeCallbacks(draw);
            if (display != null) display.setSurface(null);
            Log.e("VRWebViewGpuProbe", failure); updateStatus();
        }

        void close() {
            if (closed) return;
            closed = true; ui.removeCallbacks(draw); ui.removeCallbacks(tick);
            touchOwner = -1;
            if (web != null) {
                InputMethodManager imm = web.getContext().getSystemService(InputMethodManager.class);
                if (imm != null) imm.hideSoftInputFromWindow(web.getWindowToken(), 0);
                web.onPause(); web.stopLoading();
                if (web.getParent() instanceof ViewGroup) ((ViewGroup) web.getParent()).removeView(web);
                web.destroy(); web = null;
            }
            if (presentation != null) { presentation.dismiss(); presentation = null; }
            if (display != null) { display.setSurface(null); display.release(); display = null; }
            // Stop every producer before stopping the SurfaceTexture consumer.
            if (preview != null) { preview.dispose(); preview.onPause(); preview = null; }
            output = null;
        }
    }
}
