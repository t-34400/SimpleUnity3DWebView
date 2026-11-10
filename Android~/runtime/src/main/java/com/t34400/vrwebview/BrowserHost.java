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
import android.webkit.WebResourceError;
import android.content.pm.PackageManager;
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
        void evaluated(long operation, String result, boolean success);
        void found(long revision, int active, int count, boolean complete);
        void context(String address, boolean image);
        void pageState(String address, String title, boolean back, boolean forward, boolean loading, int progress, int error);
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
    /** Page lifetime is independent from a window's transient output queue. */
    public static void openPage(Activity activity, long id, int width, int height, boolean workaround, boolean diagnostics, Callback callback) {
        UI.post(() -> {
            if (HOSTS.containsKey(id)) { callback.failed("duplicate_host"); return; }
            Host host = new Host(activity, id, width, height, diagnostics, callback); host.output = 0;
            HOSTS.put(id, host);
            try { host.start(workaround); host.trace("event=host_ready"); callback.ready(); }
            catch (Exception error) { HOSTS.remove(id); try { host.close(); } finally { callback.failed("host_open_failed"); } }
        });
    }
    public static void present(long id, long operation, long output, int width, int height) {
        UI.post(() -> {
            Host h = HOSTS.get(id);
            if (h == null) { if (output != 0) GpuBridge.nativeStop(output); return; }
            try {
                h.detachOutput();
                if (output != 0) {
                    Surface value = GpuBridge.nativeSurface(output);
                    if (value == null) throw new IllegalStateException("Output unavailable");
                    h.output = output; h.surface = value; h.width = width; h.height = height;
                    h.web.setLayoutParams(new FrameLayout.LayoutParams(width, height));
                    ViewGroup.LayoutParams layout = h.panel.getLayoutParams(); layout.width = width; layout.height = height; h.panel.setLayoutParams(layout);
                    UI.post(h.draw);
                }
                h.trace("event=presentation visible=" + (output != 0));
                h.callback.completed(operation, true, "presentation_bound");
            } catch (RuntimeException error) {
                if (output != 0 && h.output != output) GpuBridge.nativeStop(output);
                h.detachOutput(); h.callback.completed(operation, false, "presentation_failed");
            }
        });
    }
    public static void command(long id, long operation, String command) {
        UI.post(() -> {
            Host h = HOSTS.get(id); if (h == null) return;
            try {
                h.endEditing(true);
                switch (command) {
                    case "back": if (h.web.canGoBack()) h.web.goBack(); break;
                    case "forward": if (h.web.canGoForward()) h.web.goForward(); break;
                    case "reload": h.web.reload(); break;
                    case "stop": h.web.stopLoading(); h.loading = false; break;
                    default: throw new IllegalArgumentException("Unknown page command");
                }
                h.publishState(); h.callback.completed(operation, true, "command_submitted");
            } catch (RuntimeException error) { h.callback.completed(operation, false, "command_failed"); }
        });
    }
    public static void evaluate(long id, long operation, String script) {
        UI.post(() -> {
            Host h = HOSTS.get(id); if (h == null) return;
            if (script == null || script.length() > 1024 * 1024) { h.callback.evaluated(operation, "null", false); return; }
            try { h.web.evaluateJavascript(script, result -> {
                if (!h.closed) h.callback.evaluated(operation, result != null && result.length() <= 1024 * 1024 ? result : "null", result != null && result.length() <= 1024 * 1024);
            }); } catch (RuntimeException error) { h.callback.evaluated(operation, "null", false); }
        });
    }
    private interface ContentAction { void run(Host host); }
    private static void content(long id, long operation, ContentAction action) {
        UI.post(() -> { Host h = HOSTS.get(id); if (h == null) return;
            try { action.run(h); h.callback.completed(operation, true, "content_submitted"); }
            catch (RuntimeException error) { h.callback.completed(operation, false, "content_failed"); }
        });
    }
    public static void textZoom(long id, long operation, int percent) {
        content(id, operation, h -> { if (percent < 50 || percent > 200) throw new IllegalArgumentException("Invalid zoom"); h.web.getSettings().setTextZoom(percent); });
    }
    public static void find(long id, long operation, String query, long revision) {
        content(id, operation, h -> { if (query == null || query.length() > 4096 || revision < 0) throw new IllegalArgumentException("Invalid query"); h.content.find(query, revision); });
    }
    public static void findNext(long id, long operation, boolean forward) { content(id, operation, h -> h.content.next(forward)); }
    public static void clearFind(long id, long operation) { content(id, operation, h -> h.content.clear()); }
    public static void endEditingAsync(long id, long operation) {
        UI.post(() -> { Host h = HOSTS.get(id); if (h == null) return;
            Host editor = EDITORS.get(h.owner);
            if (editor != null && editor != h) editor.endEditing(true);
            h.endEditing(true); h.callback.completed(operation, true, "editing_ended"); });
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
    private static boolean declaresKeyboard(Activity owner) {
        try {
            android.content.pm.PackageInfo info = owner.getPackageManager().getPackageInfo(owner.getPackageName(), PackageManager.GET_CONFIGURATIONS);
            if (info.reqFeatures != null) for (android.content.pm.FeatureInfo feature : info.reqFeatures)
                if ("oculus.software.overlay_keyboard".equals(feature.name)) return true;
        } catch (PackageManager.NameNotFoundException ignored) { }
        return false;
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
            Host h = HOSTS.get(id); if (h == null || h.paused || h.closed || h.surface == null) return;
            if (action == MotionEvent.ACTION_DOWN) {
                h.cancelTouch(); h.exitHover();
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
    /** Unpressed mouse/VR-ray hover. Never request View/IME focus or arm the IME guard. */
    public static void hover(long id, int action, float x, float y) {
        if (!Float.isFinite(x) || !Float.isFinite(y) ||
                (action != MotionEvent.ACTION_HOVER_ENTER && action != MotionEvent.ACTION_HOVER_MOVE && action != MotionEvent.ACTION_HOVER_EXIT)) return;
        UI.post(() -> {
            Host h = HOSTS.get(id); if (h == null || h.closed || h.paused || h.touching || h.surface == null) return;
            if (action == MotionEvent.ACTION_HOVER_ENTER) {
                if (h.hovering) return;
                h.hovering = true;
            } else if (!h.hovering) return;
            h.hoverX = x; h.hoverY = y;
            if (action == MotionEvent.ACTION_HOVER_EXIT) h.hovering = false;
            h.dispatchHover(action, x, y);
        });
    }
    public static void scroll(long id, float x, float y, float deltaX, float deltaY) {
        if (!Float.isFinite(x) || !Float.isFinite(y) || !Float.isFinite(deltaX) || !Float.isFinite(deltaY)) return;
        UI.post(() -> {
            Host h = HOSTS.get(id); if (h == null || h.paused || h.touching || h.surface == null) return;
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
        final Activity owner; final long id; int width, height; long output; final Callback callback; final boolean diagnostics;
        FrameLayout panel; BrowserWebView web; BrowserContent content; Surface surface; View previousFocus;
        boolean closed, paused, touching, hovering, registered, loading; int progress, pageError;
        float hoverX, hoverY;
        long downTime;
        String endEditingScript;
        Host(Activity owner, long id, int width, int height, boolean diagnostics, Callback callback) {
            this.owner = owner; this.id = id; this.output = id; this.width = width; this.height = height; this.callback = callback; this.diagnostics = diagnostics;
        }
        void trace(String event) { if (diagnostics) android.util.Log.i("VRWebViewRuntime", "host=" + id + " " + event); }
        void start(boolean workaround) throws Exception {
            if (owner.isFinishing() || owner.isDestroyed()) throw new IllegalStateException("Activity unavailable");
            surface = output == 0 ? null : GpuBridge.nativeSurface(output);
            if (output != 0 && surface == null) throw new IllegalStateException("Producer surface unavailable");
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
            if (diagnostics) {
                String boot = "unknown";
                try (InputStream input = owner.getAssets().open("bin/Data/boot.config")) {
                    java.util.Properties props = new java.util.Properties(); props.load(input);
                    boot = props.getProperty("xr-keyboard-overlay-enabled", "missing");
                    if (!boot.equals("0") && !boot.equals("1")) boot = "missing";
                } catch (Exception ignored) { }
                trace("event=keyboard_configuration bootKeyboard=" + boot + " overlayDeclared=" + declaresKeyboard(owner));
            }
            content = new BrowserContent(web, (revision, active, count, done) -> { if (!closed) callback.found(revision, active, count, done); });
            web.setOnLongClickListener(view -> {
                WebView.HitTestResult hit = web.getHitTestResult();
                if (hit == null) return true;
                int type = hit.getType(); String address = hit.getExtra();
                if (type == WebView.HitTestResult.SRC_IMAGE_ANCHOR_TYPE) {
                    android.os.Message message = android.os.Message.obtain(new Handler(Looper.getMainLooper()) {
                        @Override public void handleMessage(android.os.Message msg) {
                            String link = msg.getData().getString("url");
                            if (!closed && allowed(link)) callback.context(link, false);
                        }
                    });
                    web.requestFocusNodeHref(message);
                } else if ((type == WebView.HitTestResult.SRC_ANCHOR_TYPE || type == WebView.HitTestResult.IMAGE_TYPE) && allowed(address))
                    callback.context(address, type == WebView.HitTestResult.IMAGE_TYPE);
                // Retain standard text-selection behavior for editable/text targets.
                else return false;
                return true;
            });
            web.setWebViewClient(new WebViewClient() {
                @Override public void onPageStarted(WebView view, String url, android.graphics.Bitmap icon) { content.clear(); loading = true; pageError = 0; progress = 0; publishState(); }
                @Override public void onPageFinished(WebView view, String url) { loading = false; publishState(); }
                @Override public void doUpdateVisitedHistory(WebView view, String url, boolean reload) { publishState(); }
                @Override public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                    if (request.isForMainFrame()) { loading = false; pageError = error.getErrorCode(); publishState(); }
                }
                @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                    return !allowed(request.getUrl().toString());
                }
            });
            web.setWebChromeClient(new WebChromeClient() {
                @Override public void onReceivedTitle(WebView view, String title) { publishState(); }
                @Override public void onProgressChanged(WebView view, int value) { progress = value; publishState(); }
                @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
                @Override public void onShowCustomView(View view, CustomViewCallback callback) { callback.onCustomViewHidden(); }
            });
            web.viewFocusLost = this::clearCompletedEditor;
            panel.addView(web, new FrameLayout.LayoutParams(width, height));
            owner.addContentView(panel, new ViewGroup.LayoutParams(width, height));
            web.configureGuard(workaround, diagnostics ? this::trace : null); // Snapshot actual Window focus only after attachment.
            owner.getApplication().registerActivityLifecycleCallbacks(this); registered = true;
            if (surface != null) UI.post(draw);
        }
        void publishState() {
            if (!closed && web != null) callback.pageState(web.getUrl(), web.getTitle(), web.canGoBack(), web.canGoForward(), loading, progress, pageError);
        }
        void detachOutput() {
            UI.removeCallbacks(draw); cancelTouch(); exitHover();
            if (surface != null) { surface.release(); surface = null; }
            if (output != 0) { if (output != 0) GpuBridge.nativeStop(output); output = 0; }
        }
        void clearCompletedEditor() {
            exitHover();
            if (web != null && !web.hasFocus() && EDITORS.get(owner) == this) EDITORS.remove(owner);
            if (closed || web == null || web.hasFocus() || !web.hasWindowFocus()) return;
            web.evaluateJavascript(endEditingScript, result -> { if (!closed) trace("event=editing_end cleared=" + "true".equals(result)); });
        }
        final Runnable draw = new Runnable() {
            @Override public void run() {
                if (closed || surface == null) return;
                if (!paused && web.isAttachedToWindow() && web.getWidth() == width && web.getHeight() == height && GpuBridge.nativeReserve(output)) {
                    try { web.drawTo(surface); }
                    catch (RuntimeException error) {
                        GpuBridge.nativeCancelReserve(output); HOSTS.remove(id);
                        try { close(); } finally { callback.failed("producer_draw_failed"); }
                        return;
                    }
                }
                UI.postDelayed(this, 33);
            }
        };
        void dispatchHover(int action, float x, float y) {
            MotionEvent.PointerProperties properties = new MotionEvent.PointerProperties();
            properties.id = 0; properties.toolType = MotionEvent.TOOL_TYPE_MOUSE;
            MotionEvent.PointerCoords coords = new MotionEvent.PointerCoords(); coords.x = x * width; coords.y = y * height;
            long now = SystemClock.uptimeMillis();
            MotionEvent event = MotionEvent.obtain(now, now, action, 1,
                    new MotionEvent.PointerProperties[]{properties}, new MotionEvent.PointerCoords[]{coords},
                    0, 0, 1, 1, 0, 0, InputDevice.SOURCE_MOUSE, 0);
            try { web.dispatchGenericMotionEvent(event); } finally { event.recycle(); }
            // Enter/exit only: do not flood logcat every frame or log coordinates/content.
            if (action != MotionEvent.ACTION_HOVER_MOVE) trace("event=hover action=" + action);
        }
        void exitHover() {
            if (!hovering || web == null) return;
            hovering = false;
            dispatchHover(MotionEvent.ACTION_HOVER_EXIT, hoverX, hoverY);
        }
        void cancelTouch() {
            if (!touching || web == null) return;
            MotionEvent event = MotionEvent.obtain(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_CANCEL, 0, 0, 0);
            try { web.dispatchTouchEvent(event); } finally { event.recycle(); }
            touching = false; web.cancelArming();
        }
        void endEditing(boolean restoreUnity) {
            if (web == null) return;
            InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            boolean owns = web.hasFocus() || (imm != null && imm.isActive(web));
            web.releaseGuard(); cancelTouch(); exitHover();
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
                            if (content != null) content.close(); web.destroy(); web = null;
                        }
                    }
                } finally {
                    try {
                        if (panel != null && panel.getParent() instanceof ViewGroup) ((ViewGroup) panel.getParent()).removeView(panel);
                    } finally {
                        // No future callback can draw to this Surface. Acknowledge only after its release.
                        if (surface != null) { surface.release(); surface = null; }
                        if (output != 0) GpuBridge.nativeStop(output); output = 0;
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
