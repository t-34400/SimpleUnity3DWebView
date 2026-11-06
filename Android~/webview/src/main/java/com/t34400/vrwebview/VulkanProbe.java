package com.t34400.vrwebview;

import android.app.Activity;
import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebView;
import android.widget.FrameLayout;
import java.util.HashMap;
import java.util.Map;

/** Same Unity Activity, fixed local assets, native Vulkan consumer. Experimental, not a browser API. */
public final class VulkanProbe {
    static { System.loadLibrary("GfxPluginVRWebView"); }
    private static final Handler UI = new Handler(Looper.getMainLooper());
    private static final Map<Long, Host> HOSTS = new HashMap<>(); // UI thread only
    private static volatile String status = "Host not created";
    private VulkanProbe() {}
    private static native Surface nativeSurface(long id);
    private static native boolean nativeReserve(long id);
    private static native void nativeCancelReserve(long id);
    private static native void nativeStop(long id);

    public static void open(Activity owner, long id, int width, int height) {
        UI.post(() -> {
            if (HOSTS.containsKey(id)) return;
            Host host = new Host(owner, id, width, height);
            HOSTS.put(id, host);
            try { host.start(); }
            catch (Exception error) { host.close(); status = "Host failed: " + error.getClass().getSimpleName(); }
        });
    }
    public static void close(long id) {
        UI.post(() -> { Host host = HOSTS.remove(id); if (host != null) host.close(); else nativeStop(id); });
    }
    public static String status() { return status; }
    public static void requestIme(long id) {
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.web == null) return;
            host.web.requestFocus();
            InputMethodManager imm = (InputMethodManager) host.owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            host.imeAccepted = imm != null && imm.showSoftInput(host.web, 0);
            host.updateStatus();
        });
    }
    public static void releaseFocus(long id) {
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.web == null) return;
            host.cancelTouch(); host.hideIme(); host.web.clearFocus();
            if (host.previousFocus != null && host.previousFocus.isAttachedToWindow()) host.previousFocus.requestFocus();
            host.updateStatus();
        });
    }
    public static void reload(long id) {
        UI.post(() -> { Host host = HOSTS.get(id); if (host != null && host.web != null) host.web.reload(); });
    }
    /** Normalized viewport coordinates, y down; never add DOM scroll offsets. */
    public static void pointer(long id, int action, float x, float y) {
        if (!Float.isFinite(x) || !Float.isFinite(y) || action < 0 || action > 3) return;
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.web == null) return;
            if (action == MotionEvent.ACTION_DOWN) {
                host.cancelTouch(); host.downTime = SystemClock.uptimeMillis(); host.touching = true;
                host.web.requestFocus();
            } else if (!host.touching) return;
            MotionEvent event = MotionEvent.obtain(host.downTime, SystemClock.uptimeMillis(), action,
                    x * host.width, y * host.height, 0);
            try { host.web.dispatchTouchEvent(event); } finally { event.recycle(); }
            if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL) host.touching = false;
        });
    }
    public static void scroll(long id, float amount) {
        if (!Float.isFinite(amount)) return;
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.web == null || host.touching) return;
            host.web.scrollBy(0, Math.round(Math.max(-1, Math.min(1, amount)) * host.height * 0.08f));
        });
    }

    private static final class Host {
        final Activity owner; final long id; final int width, height;
        ProbeWebView web; FrameLayout panel; Surface surface; View previousFocus;
        boolean closed, touching, imeAccepted;
        long downTime;
        int fullscreenRequests;
        Host(Activity owner, long id, int width, int height) {
            this.owner = owner; this.id = id; this.width = width; this.height = height;
        }
        void start() throws Exception {
            surface = nativeSurface(id);
            if (surface == null) throw new IllegalStateException("Native surface unavailable");
            previousFocus = owner.getCurrentFocus();
            panel = new FrameLayout(owner);
            web = new ProbeWebView(owner);
            web.suppressWindowDraw = true;
            web.setFocusable(true); web.setFocusableInTouchMode(true);
            web.getSettings().setJavaScriptEnabled(true);
            web.getSettings().setAllowFileAccess(false); web.getSettings().setAllowContentAccess(false);
            web.getSettings().setMediaPlaybackRequiresUserGesture(true);
            web.setWebViewClient(new ProbeAssets(owner.getAssets()));
            web.setWebChromeClient(new WebChromeClient() {
                @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
                @Override public void onShowCustomView(View view, CustomViewCallback callback) {
                    fullscreenRequests++; callback.onCustomViewHidden();
                }
            });
            panel.addView(web, new FrameLayout.LayoutParams(width, height));
            owner.addContentView(panel, new ViewGroup.LayoutParams(width, height));
            web.loadUrl(ProbeAssets.PAGE);
            UI.post(draw);
        }
        final Runnable draw = new Runnable() {
            @Override public void run() {
                if (closed || web == null) return;
                if (web.isAttachedToWindow() && web.getWidth() == width && web.getHeight() == height && nativeReserve(id)) {
                    try { web.drawTo(surface); }
                    catch (RuntimeException error) {
                        // A failed producer submission is fatal for this generation. Never reuse its accounting.
                        nativeCancelReserve(id); close();
                        status = "HardwareCanvas failed: " + error.getClass().getSimpleName(); return;
                    }
                }
                updateStatus(); UI.postDelayed(this, 33);
            }
        };
        void updateStatus() {
            if (web == null) return;
            InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            status = "sameActivity=" + owner.getClass().getSimpleName() + "\nviewFocus=" + web.hasFocus()
                    + " windowFocus=" + web.hasWindowFocus() + " immActive=" + (imm != null && imm.isActive(web))
                    + " imeAccepted=" + imeAccepted + "\nattached=" + web.isAttachedToWindow() + " shown=" + web.isShown()
                    + " view=" + web.getWidth() + "x" + web.getHeight() + " window=" + panel.getRootView().getWidth() + "x" + panel.getRootView().getHeight()
                    + "\ncanvas=" + web.draws + " hardware=" + web.targetHardware + " windowDraw=off"
                    + " scroll=" + web.getScrollX() + "," + web.getScrollY()
                    + "\ninputConnections=" + web.connectionRequests + "/" + web.nonNullConnections
                    + " fullscreenRequests=" + fullscreenRequests;
        }
        void cancelTouch() {
            if (!touching || web == null) return;
            MotionEvent event = MotionEvent.obtain(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_CANCEL, 0, 0, 0);
            try { web.dispatchTouchEvent(event); } finally { event.recycle(); }
            touching = false;
        }
        void hideIme() {
            if (web == null) return;
            InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm != null && imm.isActive(web)) imm.hideSoftInputFromWindow(web.getWindowToken(), 0);
        }
        void close() {
            if (closed) return;
            closed = true; UI.removeCallbacks(draw); cancelTouch(); hideIme();
            if (web != null) { web.stopLoading(); web.onPause(); panel.removeView(web); web.destroy(); web = null; }
            if (panel != null && panel.getParent() instanceof ViewGroup) ((ViewGroup) panel.getParent()).removeView(panel);
            if (surface != null) { surface.release(); surface = null; }
            if (previousFocus != null && previousFocus.isAttachedToWindow()) previousFocus.requestFocus();
            nativeStop(id); // consumer teardown is deferred until Unity's safe frame
            status = "Host closed";
        }
    }
}
