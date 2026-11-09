package com.t34400.vrwebview;

import android.app.Activity;
import android.app.Application;
import android.content.Context;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.IdentityHashMap;
import java.util.Map;

/** UI-thread owner of one WebView and one native producer. No diagnostic pages/activities. */
public final class BrowserHost {
    public interface Callback {
        void ready();
        void completed(long operation, boolean success, String code);
        void failed(String code);
    }
    private static final Handler UI = new Handler(Looper.getMainLooper());
    private static final Map<Long, Host> HOSTS = new HashMap<>();
    private static final Map<Activity, Host> EDITORS = new IdentityHashMap<>();
    private BrowserHost() {}

    public static void open(Activity activity, long id, int width, int height, boolean providerWorkaround, boolean diagnostics, Callback callback) {
        UI.post(() -> {
            if (HOSTS.containsKey(id)) { callback.failed("duplicate_host"); return; }
            Host host = new Host(activity, id, width, height, diagnostics, callback);
            HOSTS.put(id, host);
            try { host.start(providerWorkaround); host.trace("event=host_ready"); callback.ready(); }
            catch (Exception error) {
                HOSTS.remove(id);
                try { host.close(); } finally { callback.failed("host_open_failed"); }
            }
        });
    }
    public static void close(long id) {
        UI.post(() -> { Host host = HOSTS.remove(id); if (host != null) host.close(); else GpuBridge.nativeStop(id); });
    }
    public static void navigate(long id, long operation, String address) {
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null) return;
            if (!allowed(address)) { host.callback.completed(operation, false, "unsupported_url"); return; }
            try {
                host.endEditing(true); host.web.loadUrl(address);
                host.callback.completed(operation, true, "navigation_submitted");
            } catch (RuntimeException error) { host.callback.completed(operation, false, "navigation_failed"); }
        });
    }
    public static void loadHtml(long id, long operation, String html, String baseAddress) {
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null) return;
            if (!allowed(baseAddress)) { host.callback.completed(operation, false, "unsupported_base_url"); return; }
            try {
                host.endEditing(true);
                host.web.loadDataWithBaseURL(baseAddress, html, "text/html", "UTF-8", null);
                host.callback.completed(operation, true, "html_submitted");
            } catch (RuntimeException error) { host.callback.completed(operation, false, "html_failed"); }
        });
    }
    private static boolean allowed(String address) {
        if (address == null) return false;
        Uri uri = Uri.parse(address);
        return ("https".equalsIgnoreCase(uri.getScheme()) || "http".equalsIgnoreCase(uri.getScheme()))
                && uri.getHost() != null && !uri.getHost().isEmpty() && uri.getUserInfo() == null;
    }
    public static void endEditing(long id) { UI.post(() -> { Host h = HOSTS.get(id); if (h != null) h.endEditing(true); }); }
    public static void setPaused(long id, boolean paused) {
        UI.post(() -> { Host h = HOSTS.get(id); if (h != null) h.setPaused(paused); });
    }
    public static void pointer(long id, int action, float x, float y) {
        if (!Float.isFinite(x) || !Float.isFinite(y) || action < 0 || action > 3) return;
        UI.post(() -> {
            Host h = HOSTS.get(id); if (h == null || h.paused || h.closed) return;
            if (action == MotionEvent.ACTION_DOWN) {
                h.cancelTouch();
                Host before = EDITORS.get(h.owner);
                if (before != null && before != h) before.endEditing(false);
                EDITORS.put(h.owner, h);
                h.downTime = SystemClock.uptimeMillis(); h.touching = true;
                h.web.armGuard(); h.web.requestFocus();
            } else if (!h.touching) return;
            MotionEvent event = MotionEvent.obtain(h.downTime, SystemClock.uptimeMillis(), action, x * h.width, y * h.height, 0);
            try { h.web.dispatchTouchEvent(event); } finally { event.recycle(); }
            if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL) h.touching = false;
            if (action == MotionEvent.ACTION_CANCEL) h.web.cancelArming();
        });
    }
    public static void scroll(long id, float x, float y, float deltaX, float deltaY) {
        if (!Float.isFinite(x) || !Float.isFinite(y) || !Float.isFinite(deltaX) || !Float.isFinite(deltaY)) return;
        UI.post(() -> {
            Host h = HOSTS.get(id); if (h == null || h.paused || h.touching) return;
            MotionEvent.PointerProperties properties = new MotionEvent.PointerProperties();
            properties.id = 0; properties.toolType = MotionEvent.TOOL_TYPE_MOUSE;
            MotionEvent.PointerCoords coords = new MotionEvent.PointerCoords(); coords.x = x * h.width; coords.y = y * h.height;
            coords.setAxisValue(MotionEvent.AXIS_HSCROLL, deltaX); coords.setAxisValue(MotionEvent.AXIS_VSCROLL, deltaY);
            long now = SystemClock.uptimeMillis();
            MotionEvent event = MotionEvent.obtain(now, now, MotionEvent.ACTION_SCROLL, 1,
                    new MotionEvent.PointerProperties[]{properties}, new MotionEvent.PointerCoords[]{coords},
                    0, 0, 1, 1, 0, 0, InputDevice.SOURCE_MOUSE, 0);
            try { h.web.dispatchGenericMotionEvent(event); } finally { event.recycle(); }
        });
    }

    private static final class Host implements Application.ActivityLifecycleCallbacks {
        final Activity owner; final long id; final int width, height; final Callback callback; final boolean diagnostics;
        FrameLayout panel; BrowserWebView web; Surface surface; View previousFocus;
        boolean closed, paused, touching, registered;
        long downTime;
        String endEditingScript;
        Host(Activity owner, long id, int width, int height, boolean diagnostics, Callback callback) {
            this.owner = owner; this.id = id; this.width = width; this.height = height; this.callback = callback; this.diagnostics = diagnostics;
        }
        void trace(String event) { if (diagnostics) android.util.Log.i("VRWebViewRuntime", "host=" + id + " " + event); }
        void start(boolean workaround) throws Exception {
            if (owner.isFinishing() || owner.isDestroyed()) throw new IllegalStateException("Activity unavailable");
            surface = GpuBridge.nativeSurface(id);
            if (surface == null) throw new IllegalStateException("Producer surface unavailable");
            try (InputStream stream = owner.getAssets().open("vrwebview-end-editing.js")) {
                ByteArrayOutputStream bytes = new ByteArrayOutputStream(); byte[] buffer = new byte[4096]; int count;
                while ((count = stream.read(buffer)) != -1) bytes.write(buffer, 0, count);
                endEditingScript = new String(bytes.toByteArray(), StandardCharsets.UTF_8);
            }
            previousFocus = owner.getCurrentFocus();
            for (Host existing : HOSTS.values()) {
                if (existing != this && existing.owner == owner && existing.web == previousFocus) {
                    previousFocus = existing.previousFocus; break;
                }
            }
            panel = new FrameLayout(owner); web = new BrowserWebView(owner);
            web.setFocusable(true); web.setFocusableInTouchMode(true);
            web.getSettings().setJavaScriptEnabled(true);
            web.getSettings().setDomStorageEnabled(true);
            web.getSettings().setAllowFileAccess(false); web.getSettings().setAllowContentAccess(false);
            web.getSettings().setMediaPlaybackRequiresUserGesture(true);
            web.getSettings().setSupportMultipleWindows(false);
            web.setWebViewClient(new WebViewClient() {
                @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                    return !allowed(request.getUrl().toString());
                }
            });
            web.setWebChromeClient(new WebChromeClient() {
                @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
                @Override public void onShowCustomView(View view, CustomViewCallback callback) { callback.onCustomViewHidden(); }
            });
            web.viewFocusLost = this::clearCompletedEditor;
            panel.addView(web, new FrameLayout.LayoutParams(width, height));
            owner.addContentView(panel, new ViewGroup.LayoutParams(width, height));
            web.configureGuard(workaround, diagnostics ? this::trace : null); // Snapshot actual Window focus only after attachment.
            owner.getApplication().registerActivityLifecycleCallbacks(this); registered = true;
            UI.post(draw);
        }
        void clearCompletedEditor() {
            if (closed || web == null || web.hasFocus() || !web.hasWindowFocus()) return;
            web.evaluateJavascript(endEditingScript, result -> { if (!closed) trace("event=editing_end cleared=" + "true".equals(result)); });
        }
        final Runnable draw = new Runnable() {
            @Override public void run() {
                if (closed) return;
                if (!paused && web.isAttachedToWindow() && web.getWidth() == width && web.getHeight() == height && GpuBridge.nativeReserve(id)) {
                    try { web.drawTo(surface); }
                    catch (RuntimeException error) {
                        GpuBridge.nativeCancelReserve(id); HOSTS.remove(id);
                        try { close(); } finally { callback.failed("producer_draw_failed"); }
                        return;
                    }
                }
                UI.postDelayed(this, 33);
            }
        };
        void cancelTouch() {
            if (!touching || web == null) return;
            MotionEvent event = MotionEvent.obtain(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_CANCEL, 0, 0, 0);
            try { web.dispatchTouchEvent(event); } finally { event.recycle(); }
            touching = false; web.cancelArming();
        }
        void endEditing(boolean restoreUnity) {
            if (web == null) return;
            boolean owns = EDITORS.get(owner) == this || web.hasFocus();
            web.releaseGuard(); cancelTouch();
            InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm != null && imm.isActive(web)) imm.hideSoftInputFromWindow(web.getWindowToken(), 0);
            web.clearFocus(); clearCompletedEditor();
            if (EDITORS.get(owner) == this) EDITORS.remove(owner);
            if (owns && restoreUnity && previousFocus != null && previousFocus.isAttachedToWindow()) previousFocus.requestFocus();
        }
        void setPaused(boolean value) {
            if (closed || paused == value) return;
            paused = value; trace("event=pause paused=" + value); web.setResumed(!value);
            if (value) { endEditing(false); web.onPause(); } else web.onResume();
        }
        void close() {
            if (closed) return;
            trace("event=closed"); UI.removeCallbacks(draw);
            try { endEditing(true); }
            finally {
                closed = true;
                if (EDITORS.get(owner) == this) EDITORS.remove(owner);
                if (registered) { owner.getApplication().unregisterActivityLifecycleCallbacks(this); registered = false; }
                try {
                    if (web != null) {
                        web.viewFocusLost = null;
                        try { web.stopLoading(); web.onPause(); }
                        finally {
                            if (panel != null) panel.removeView(web);
                            web.destroy(); web = null;
                        }
                    }
                } finally {
                    try {
                        if (panel != null && panel.getParent() instanceof ViewGroup) ((ViewGroup) panel.getParent()).removeView(panel);
                    } finally {
                        // No future callback can draw to this Surface. Acknowledge only after its release.
                        if (surface != null) { surface.release(); surface = null; }
                        GpuBridge.nativeStop(id);
                    }
                }
            }
        }
        @Override public void onActivityCreated(Activity a, Bundle b) {}
        @Override public void onActivityStarted(Activity a) {}
        @Override public void onActivityResumed(Activity a) { if (a == owner) setPaused(false); }
        @Override public void onActivityPaused(Activity a) { if (a == owner) setPaused(true); }
        @Override public void onActivityStopped(Activity a) { if (a == owner) setPaused(true); }
        @Override public void onActivitySaveInstanceState(Activity a, Bundle b) {}
        @Override public void onActivityDestroyed(Activity a) {
            if (a == owner && !closed) {
                HOSTS.remove(id);
                try { close(); } finally { callback.failed("activity_destroyed"); }
            }
        }
    }
}
