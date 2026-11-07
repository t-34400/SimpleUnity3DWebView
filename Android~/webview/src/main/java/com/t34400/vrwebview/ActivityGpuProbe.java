package com.t34400.vrwebview;

import android.app.Activity;
import android.app.Application;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.webkit.JavascriptInterface;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import java.lang.ref.WeakReference;

/** AG1: native Activity owns editing; Unity owns the GPU target and render-event pump. */
public final class ActivityGpuProbe {
    static final Handler UI = new Handler(Looper.getMainLooper());
    private static final ActivityGpuLifetime LIFE = new ActivityGpuLifetime();
    private static volatile Session current;
    private static volatile long lastClosed;
    private ActivityGpuProbe() {}
    static void log(String event) { Log.i("VRWebViewActivityGPU", "[VRWebViewActivityGPU] AG1 " + event); }
    public static String startupMode(Activity owner) {
        Intent intent = owner.getIntent();
        String mode = ActivityGpuLifetime.mode(intent == null ? null : intent.getStringExtra("vrwebview_activity_gpu_mode"));
        if (intent != null) intent.removeExtra("vrwebview_activity_gpu_mode");
        return mode;
    }
    public static void open(Activity owner, long id, int width, int height, String mode) {
        UI.post(() -> {
            if (current != null) current.close("replacement");
            if (id <= 0 || width < 128 || width > 2048 || height < 128 || height > 2048) {
                if (id > 0) VulkanProbe.nativeStop(id); lastClosed = id; return;
            }
            if (!LIFE.open(id)) { VulkanProbe.nativeStop(id); lastClosed = id; log("launch refused: previous producer still owns lifetime"); return; }
            Session session = new Session(owner, id, width, height, ActivityGpuLifetime.mode(mode)); current = session;
            session.application.registerActivityLifecycleCallbacks(session.lifecycle);
            log("launch id=" + id + " mode=" + session.mode + " owner=" + owner.getClass().getSimpleName());
            try { owner.startActivity(new Intent(owner, ActivityGpuHostActivity.class).putExtra("session", id)); }
            catch (RuntimeException error) { log("launchFailed=" + error.getClass().getSimpleName()); session.close("launchFailure"); }
        });
    }
    static Session attach(ActivityGpuHostActivity owner, long id) {
        Session session = current;
        if (!LIFE.current(id) || session == null || session.closed || session.id != id) return null;
        session.activity = new WeakReference<>(owner); return session;
    }
    public static void close(long id) { UI.post(() -> { Session s = current; if (s != null && s.id == id) s.close("UnityClose"); else VulkanProbe.nativeStop(id); }); }
    public static int phase(long id) { Session s = current; return s != null && s.id == id && !s.closed ? 1 : lastClosed == id ? -1 : 0; }
    public static int inputState(long id) {
        Session s = current;
        return s != null && s.id == id && !s.closed && SystemClock.elapsedRealtime() - s.snapshotAt < 500 ? s.bits : 0;
    }
    public static String status(long id) { Session s = current; return s != null && s.id == id ? s.status : "AG1 host closed/pending"; }
    public static void pulse(long id, long frame, long renders, String focus) {
        Session s = current; if (s == null || s.id != id) return;
        s.tick = "unityFrameLast=" + frame + " unityRenderLast=" + renders + " focusLast=" + focus;
        s.tickAt = SystemClock.elapsedRealtime();
    }
    public static void source(long id, boolean visible) { UI.post(() -> { Session s = current; if (s != null && s.id == id) s.setSourceVisible(visible); }); }
    public static void browse(long id) { UI.post(() -> { Session s = current; if (s != null && s.id == id) s.browse(); }); }
    public static void requestIme(long id) { UI.post(() -> { Session s = current; if (s != null && s.id == id) s.focusField(); }); }
    public static void pointer(long id, int action, float x, float y) {
        if (!Float.isFinite(x) || !Float.isFinite(y) || action < 0 || action > 3) return;
        UI.post(() -> {
            Session s = current; if (s == null || s.id != id || s.closed || s.web == null) return;
            if (action != MotionEvent.ACTION_CANCEL && (s.editing || s.awaitingEditCheck || !s.resumed || !s.web.hasWindowFocus())) { s.cancelTouch(); return; }
            s.touch(action, x, y, "UnityRay");
        });
    }
    static final class Session {
        final long id; final int width, height; final String mode;
        final WeakReference<Activity> unity;
        final Application application;
        WeakReference<ActivityGpuHostActivity> activity = new WeakReference<>(null);
        ProbeWebView web; FrameLayout panel; Surface surface;
        boolean closed, resumed, editing, awaitingEditCheck, injecting, touching, pendingIme, pendingAutoVideo;
        boolean sourceVisible, touchPassthrough;
        long downTime, lastTrace, editQuery, returnAt;
        volatile int bits;
        volatile long snapshotAt, tickAt;
        volatile String status = "AG1 launch pending", tick = "Unity heartbeat pending";
        final Application.ActivityLifecycleCallbacks lifecycle = new Application.ActivityLifecycleCallbacks() {
            @Override public void onActivityDestroyed(Activity owner) { if (owner == unity.get()) close("UnityDestroyed"); }
            @Override public void onActivityCreated(Activity a, Bundle b) {}
            @Override public void onActivityStarted(Activity a) {}
            @Override public void onActivityResumed(Activity a) {}
            @Override public void onActivityPaused(Activity a) { if (a == unity.get()) log("unityActivity paused id=" + id); }
            @Override public void onActivityStopped(Activity a) {}
            @Override public void onActivitySaveInstanceState(Activity a, Bundle b) {}
        };
        Session(Activity owner, long id, int width, int height, String mode) {
            unity = new WeakReference<>(owner); application = owner.getApplication();
            this.id = id; this.width = width; this.height = height; this.mode = mode;
            sourceVisible = mode.equals("native");
        }
        void create(ActivityGpuHostActivity owner, FrameLayout root) throws Exception {
            surface = VulkanProbe.nativeSurface(id);
            if (surface == null) throw new IllegalStateException("Native surface unavailable");
            panel = root; web = new ProbeWebView(owner);
            web.setFocusable(true); web.setFocusableInTouchMode(true);
            web.suppressWindowDraw = !sourceVisible;
            web.metadataListener = e -> log("id=" + id + " web " + e);
            web.windowFocusListener = focused -> { if (focused) UI.postDelayed(() -> { tryIme(); if (pendingAutoVideo) autoPlay(); }, 150); updateState(); };
            web.getSettings().setJavaScriptEnabled(true);
            web.getSettings().setAllowFileAccess(false); web.getSettings().setAllowContentAccess(false);
            web.getSettings().setMediaPlaybackRequiresUserGesture(true);
            web.addJavascriptInterface(new EditMetadata(this), "AG1EditMetadata");
            web.setOnTouchListener((v, event) -> {
                if (!injecting && !sourceVisible) return true; // Invisible native page must not receive accidental taps.
                if (!injecting && event.getActionMasked() == MotionEvent.ACTION_UP) log("id=" + id + " nativePageTouch");
                return false;
            });
            web.setWebChromeClient(new WebChromeClient() {
                @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
                @Override public void onShowCustomView(View v, CustomViewCallback c) { c.onCustomViewHidden(); }
            });
            final ProbeAssets assets = new ProbeAssets(owner.getAssets());
            web.setWebViewClient(new WebViewClient() {
                @Override public WebResourceResponse shouldInterceptRequest(android.webkit.WebView v, WebResourceRequest r) { return assets.shouldInterceptRequest(v, r); }
                @Override public boolean shouldOverrideUrlLoading(android.webkit.WebView v, WebResourceRequest r) { return assets.shouldOverrideUrlLoading(v, r); }
                @Override public void onPageFinished(android.webkit.WebView v, String url) {
                    if (closed) return;
                    web.evaluateJavascript("(function(){function e(){var x=document.activeElement;AG1EditMetadata.editing(!!x&&(x.isContentEditable||/^(INPUT|TEXTAREA)$/.test(x.tagName)));}document.addEventListener('focusin',e);document.addEventListener('focusout',function(){setTimeout(e,0);});e();})()", null);
                    log("id=" + id + " pageReady mode=" + mode);
                    if (mode.equals("ime")) UI.postDelayed(() -> { if (!closed) focusField(); }, 1000);
                    if (mode.equals("video")) { pendingAutoVideo = true; UI.postDelayed(() -> { if (!closed && pendingAutoVideo) autoPlay(); }, 1000); }
                }
            });
            panel.addView(web, new FrameLayout.LayoutParams(width, height));
            panel.setFocusableInTouchMode(true); panel.requestFocus();
            web.loadUrl(ProbeAssets.PAGE); returnAt = SystemClock.elapsedRealtime() + 120000;
            UI.post(draw); UI.postDelayed(autoReturn, 120000);
        }
        void setSourceVisible(boolean value) {
            if (closed || web == null) return;
            cancelTouch();
            sourceVisible = value; web.suppressWindowDraw = !value; web.invalidate();
            log("id=" + id + " sourceVisible=" + value); updateState();
        }
        void focusField() {
            if (closed || web == null) return;
            editing = true; pendingIme = true; cancelTouch(); web.requestFocus();
            web.evaluateJavascript("document.getElementById('text').focus()", r -> { if (!closed) UI.postDelayed(this::tryIme, 150); });
            log("id=" + id + " focusField"); updateState();
        }
        void tryIme() {
            if (closed || web == null || !editing || !pendingIme || !web.hasFocus() || !web.hasWindowFocus() || !web.lastConnectionNonNull) return;
            pendingIme = false;
            InputMethodManager imm = (InputMethodManager) web.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
            boolean accepted = imm != null && imm.showSoftInput(web, 0);
            log("id=" + id + " imeRequest accepted=" + accepted + " windowFocus=" + web.hasWindowFocus());
        }
        void browse() {
            if (closed || web == null) return;
            cancelTouch(); pendingIme = false; awaitingEditCheck = true;
            InputMethodManager imm = (InputMethodManager) web.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm != null) imm.hideSoftInputFromWindow(web.getWindowToken(), 0);
            web.evaluateJavascript("(function(){var x=document.activeElement;if(x)x.blur();return false;})()", r -> {
                if (closed) return;
                web.clearFocus(); panel.requestFocus(); queryEditing();
            });
            log("id=" + id + " browseRequested"); updateState();
        }
        void queryEditing() {
            if (closed || web == null) return;
            final long query = ++editQuery;
            awaitingEditCheck = true; updateState();
            web.evaluateJavascript("(function(){var x=document.activeElement;return !!x&&(x.isContentEditable||/^(INPUT|TEXTAREA)$/.test(x.tagName));})()", value -> {
                if (closed || query != editQuery) return;
                editing = !"false".equals(value); awaitingEditCheck = false;
                log("id=" + id + " editing=" + editing); updateState();
            });
        }
        void autoPlay() {
            if (closed || web == null) return;
            if (!web.hasWindowFocus()) { log("id=" + id + " autoPlay waiting for Window focus"); return; }
            pendingAutoVideo = false;
            // Fixed local control rectangle only. Returns geometry, never text.
            web.evaluateJavascript("(function(){var r=document.getElementById('play').getBoundingClientRect();return [(r.left+r.width/2)/innerWidth,(r.top+r.height/2)/innerHeight];})()", value -> {
                if (closed) return;
                if (!web.hasWindowFocus() || !resumed || editing) { pendingAutoVideo = true; return; }
                try {
                    org.json.JSONArray xy = new org.json.JSONArray(value);
                    float x = (float)xy.getDouble(0), y = (float)xy.getDouble(1);
                    if (Float.isFinite(x) && Float.isFinite(y)) { touch(0, x, y, "autoVideo"); touch(1, x, y, "autoVideo"); }
                } catch (org.json.JSONException error) { log("id=" + id + " autoPlay geometry unavailable"); }
            });
        }
        void touch(int action, float x, float y, String source) {
            if (closed || web == null) return;
            if (!Float.isFinite(x * width) || !Float.isFinite(y * height)) return;
            if (action != MotionEvent.ACTION_CANCEL && (!resumed || !web.hasWindowFocus())) { cancelTouch(); return; }
            if (action == MotionEvent.ACTION_DOWN) { cancelTouch(); downTime = SystemClock.uptimeMillis(); touching = true; web.requestFocus(); }
            else if (!touching) return;
            MotionEvent event = MotionEvent.obtain(downTime, SystemClock.uptimeMillis(), action, x * width, y * height, 0);
            injecting = true;
            try { web.dispatchTouchEvent(event); } finally { injecting = false; event.recycle(); }
            if (action != MotionEvent.ACTION_MOVE) log("id=" + id + " pointer source=" + source + " action=" + action);
            if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL) {
                touching = false;
                if (action == MotionEvent.ACTION_UP) { awaitingEditCheck = true; UI.postDelayed(this::queryEditing, 100); }
            }
            updateState();
        }
        void cancelTouch() { if (touching) touch(MotionEvent.ACTION_CANCEL, 0, 0, "cancel"); }
        void updateState() {
            if (closed || web == null) return;
            boolean pass = ActivityGpuTouchPolicy.passThrough(mode, sourceVisible, editing, awaitingEditCheck);
            ActivityGpuHostActivity owner = activity.get();
            if (owner != null && pass != touchPassthrough) {
                touchPassthrough = pass;
                if (pass) owner.getWindow().addFlags(WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE);
                else owner.getWindow().clearFlags(WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE);
                owner.touchPolicyChanged(pass);
                log("id=" + id + " nativeTouchPassthrough=" + pass);
            }
            if (owner != null) owner.touchPolicyChanged(pass);
            boolean ime = false;
            if (Build.VERSION.SDK_INT >= 30) {
                WindowInsets insets = web.getRootWindowInsets(); ime = insets != null && insets.isVisible(WindowInsets.Type.ime());
            }
            // Shared bit contract with ActivityProbeInputPolicy.cs. Conservative editing gate.
            bits = 1 | (web.hasWindowFocus() ? 2 : 0) | (!sourceVisible ? 4 : 0)
                | (editing || awaitingEditCheck ? 8 : 0) | (ime ? 16 : 0)
                | (web.isAttachedToWindow() && web.getWidth() == width && web.getHeight() == height ? 32 : 0) | (resumed ? 64 : 0) | (touchPassthrough ? 128 : 0);
            snapshotAt = SystemClock.elapsedRealtime();
            status = "AG1 id=" + id + " sourceVisible=" + sourceVisible + " stateBits=" + bits
                + "\nwindowFocus=" + web.hasWindowFocus() + " editing=" + editing + " editCheck=" + awaitingEditCheck + " imeInsets=" + ime + " resumed=" + resumed + " nativeTouchPassthrough=" + touchPassthrough
                + "\ncanvas=" + web.draws + " hardware=" + web.targetHardware + " connection=" + web.connectionRequests + "/" + web.nonNullConnections
                + " autoReturnIn=" + Math.max(0, (returnAt - snapshotAt) / 1000) + "s";
        }
        final Runnable draw = new Runnable() {
            @Override public void run() {
                if (closed || web == null) return;
                if (resumed && web.isAttachedToWindow() && web.getWidth() == width && web.getHeight() == height && VulkanProbe.nativeReserve(id)) {
                    try { web.drawTo(surface); }
                    catch (RuntimeException error) { VulkanProbe.nativeCancelReserve(id); log("id=" + id + " drawFailed=" + error.getClass().getSimpleName()); close("drawFailure"); return; }
                }
                updateState(); long now = SystemClock.elapsedRealtime();
                if (now - lastTrace >= 1000) { lastTrace = now; log(status.replace('\n', ' ') + " " + tick + " heartbeatAgeMs=" + (tickAt == 0 ? -1 : now - tickAt)); }
                UI.postDelayed(this, 33);
            }
        };
        final Runnable autoReturn = () -> close("autoReturn120s");
        void close(String reason) {
            if (closed) return;
            cancelTouch(); closed = true; bits = 0; ++editQuery;
            UI.removeCallbacks(draw); UI.removeCallbacks(autoReturn);
            application.unregisterActivityLifecycleCallbacks(lifecycle);
            try { if (web != null) {
                InputMethodManager imm = (InputMethodManager) web.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
                if (imm != null) imm.hideSoftInputFromWindow(web.getWindowToken(), 0);
                web.metadataListener = null; web.windowFocusListener = null; web.removeJavascriptInterface("AG1EditMetadata"); web.stopLoading(); web.onPause();
                if (web.getParent() instanceof ViewGroup) ((ViewGroup)web.getParent()).removeView(web);
                web.destroy(); web = null;
            } } catch (RuntimeException error) { log("id=" + id + " cleanupWeb=" + error.getClass().getSimpleName()); web = null; }
            if (surface != null) { try { surface.release(); } catch (RuntimeException error) { log("id=" + id + " cleanupSurface=" + error.getClass().getSimpleName()); } surface = null; }
            VulkanProbe.nativeStop(id); lastClosed = id;
            if (LIFE.close(id) && current == this) current = null;
            ActivityGpuHostActivity owner = activity.get(); activity.clear();
            if (owner != null && !owner.isFinishing()) owner.finish();
            log("closed id=" + id + " reason=" + reason);
        }
    }
    public static final class EditMetadata {
        private final WeakReference<Session> target;
        EditMetadata(Session session) { target = new WeakReference<>(session); }
        @JavascriptInterface public void editing(boolean value) {
            UI.post(() -> {
                Session s = target.get(); if (s == null || s.closed || current != s) return;
                ++s.editQuery; s.editing = value; s.awaitingEditCheck = false;
                if (value) s.pendingIme = true; else s.pendingIme = false;
                log("id=" + s.id + " DOMediting=" + value);
                s.updateState(); if (value) UI.postDelayed(s::tryIme, 150);
            });
        }
    }
}
