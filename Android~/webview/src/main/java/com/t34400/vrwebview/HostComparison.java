package com.t34400.vrwebview;

import android.app.Activity;
import android.app.Application;
import android.content.Context;
import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.ResultReceiver;
import android.os.SystemClock;
import android.text.InputType;
import android.util.Log;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.widget.EditText;
import android.webkit.WebViewClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebChromeClient;
import android.webkit.PermissionRequest;
import java.lang.ref.WeakReference;

/** Diagnostic host comparison. Never copies editing text into a WebView. */
public final class HostComparison {
    static final Handler UI = new Handler(Looper.getMainLooper());
    static final HostTrialState TRIAL = new HostTrialState(); // UI thread only
    private static WeakReference<Activity> unity = new WeakReference<>(null);
    private static WeakReference<HostComparisonActivity> child = new WeakReference<>(null);
    private static Application app;
    private static Application.ActivityLifecycleCallbacks lifecycle;
    private static EditText editor;
    private static ProbeWebView browser;
    private static WeakReference<View> previousFocus = new WeakReference<>(null);
    private static long epoch;
    private static volatile boolean trialActive;
    private static volatile Tick tick;
    private static final class Tick {
        final long frame, renders, at;
        final boolean running, focus;
        Tick(long frame, long renders, boolean running, boolean focus) {
            this.frame = frame; this.renders = renders; this.running = running; this.focus = focus; at = SystemClock.elapsedRealtime();
        }
    }
    private HostComparison() {}
    static void log(String value) { Log.i("VRWebViewHost", "[VRWebViewHost] HC1 " + value); }
    static String viewName(View view) { return view == null ? "none" : view.getClass().getSimpleName(); }
    public static String startupCase(Activity owner) {
        Intent intent = owner.getIntent();
        String value = HostTrialState.startupCase(intent == null ? null : intent.getStringExtra("vrwebview_host_case"));
        if (intent != null) intent.removeExtra("vrwebview_host_case");
        return value;
    }
    public static void pulse(long frame, long renders, boolean xrRunning, boolean focused) { tick = new Tick(frame, renders, xrRunning, focused); }
    public static boolean isTrialActive() { return trialActive; }
    public static void start(Activity owner) {
        UI.post(() -> {
            cleanup(); unity = new WeakReference<>(owner); app = owner.getApplication();
            final long session = ++epoch;
            lifecycle = new Application.ActivityLifecycleCallbacks() {
                private void event(Activity activity, String name) {
                    if (activity == unity.get()) log("unityActivity " + name + " class=" + activity.getClass().getSimpleName());
                }
                @Override public void onActivityCreated(Activity a, Bundle state) { event(a, "created"); }
                @Override public void onActivityStarted(Activity a) { event(a, "started"); }
                @Override public void onActivityResumed(Activity a) { event(a, "resumed"); }
                @Override public void onActivityPaused(Activity a) { event(a, "paused"); }
                @Override public void onActivityStopped(Activity a) { event(a, "stopped"); }
                @Override public void onActivityDestroyed(Activity a) { event(a, "destroyed"); }
                @Override public void onActivitySaveInstanceState(Activity a, Bundle state) {}
            };
            app.registerActivityLifecycleCallbacks(lifecycle);
            log("start owner=" + owner.getClass().getSimpleName() + " focus=" + owner.hasWindowFocus()
                    + " currentView=" + viewName(owner.getCurrentFocus()));
            HostProbeEnvironment.trace(owner);
            UI.post(new Runnable() {
                @Override public void run() {
                    Activity owner = unity.get();
                    if (session != epoch || owner == null) return;
                    Tick latest = tick;
                    View target = browser != null ? browser : editor;
                    InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
                    log("watch nativeTrial=" + TRIAL.kind() + " unityFrameLast=" + (latest == null ? -1 : latest.frame)
                            + " unityRenderLast=" + (latest == null ? -1 : latest.renders)
                            + " heartbeatAgeMs=" + (latest == null ? -1 : SystemClock.elapsedRealtime() - latest.at)
                            + " xrRunningLast=" + (latest != null && latest.running)
                            + " unityFocusLast=" + (latest != null && latest.focus)
                            + " ownerView=" + viewName(owner.getCurrentFocus()) + " windowFocus=" + owner.hasWindowFocus()
                            + " editorFocus=" + (target != null && target.hasFocus())
                            + " immActive=" + (target != null && imm != null && imm.isActive(target)));
                    UI.postDelayed(this, 1000);
                }
            });
        });
    }
    public static void stop(Activity owner) { UI.post(() -> { if (unity.get() == owner) cleanup(); }); }
    private static void cleanup() {
        ++epoch; stopTrial();
        if (app != null && lifecycle != null) app.unregisterActivityLifecycleCallbacks(lifecycle);
        app = null; lifecycle = null; unity.clear(); tick = null;
    }
    private static void stopTrial() {
        TRIAL.clear(); trialActive = false;
        if (editor != null) {
            hideIme(editor);
            if (editor.getParent() instanceof ViewGroup) ((ViewGroup) editor.getParent()).removeView(editor);
            editor = null;
            View old = previousFocus.get(); if (old != null && old.isAttachedToWindow()) old.requestFocus();
            previousFocus.clear();
        }
        if (browser != null) {
            hideIme(browser); browser.metadataListener = null; browser.stopLoading();
            if (browser.getParent() instanceof ViewGroup) ((ViewGroup) browser.getParent()).removeView(browser);
            browser.destroy(); browser = null;
            View old = previousFocus.get(); if (old != null && old.isAttachedToWindow()) old.requestFocus();
            previousFocus.clear();
        }
        HostComparisonActivity old = child.get(); child.clear();
        if (old != null && !old.isFinishing()) old.finish();
    }
    public static void openNativeEditor(Activity owner) {
        UI.post(() -> {
            stopTrial(); final long ticket = TRIAL.begin("native"); trialActive = true;
            previousFocus = new WeakReference<>(owner.getCurrentFocus());
            try {
            editor = new EditText(owner); editor.setSingleLine(true);
            editor.setInputType(InputType.TYPE_CLASS_TEXT); editor.setHint("HC1 native EditText");
            editor.setOnFocusChangeListener((v, focused) -> log("sameActivityEditText viewFocus=" + focused));
            owner.addContentView(editor, new ViewGroup.LayoutParams(600, 120));
            editor.requestFocus();
            log("sameActivityEditText start owner=" + owner.getClass().getSimpleName() + " ticket=" + ticket);
            UI.postDelayed(() -> { if (TRIAL.isCurrent(ticket) && editor != null) showIme(editor, ticket, "sameActivityEditText"); }, 200);
            UI.postDelayed(() -> {
                if (!TRIAL.isCurrent(ticket)) return;
                log("sameActivityEditText autoClose ticket=" + ticket); stopTrial();
            }, HostTrialState.DURATION_MS);
            } catch (RuntimeException error) { log("sameActivityEditText failed=" + error.getClass().getSimpleName()); stopTrial(); }
        });
    }
    public static void openNativeWebView(Activity owner) {
        UI.post(() -> {
            stopTrial(); final long ticket = TRIAL.begin("webview"); trialActive = true;
            previousFocus = new WeakReference<>(owner.getCurrentFocus());
            try {
                final ProbeAssets assets = new ProbeAssets(owner.getAssets());
                browser = new ProbeWebView(owner); browser.setFocusable(true); browser.setFocusableInTouchMode(true);
                browser.metadataListener = event -> log("sameActivityWebView " + event);
                browser.getSettings().setJavaScriptEnabled(true);
                browser.getSettings().setAllowFileAccess(false); browser.getSettings().setAllowContentAccess(false);
                browser.getSettings().setMediaPlaybackRequiresUserGesture(true);
                browser.setWebChromeClient(new WebChromeClient() {
                    @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
                });
                browser.setWebViewClient(new WebViewClient() {
                    @Override public WebResourceResponse shouldInterceptRequest(android.webkit.WebView view, WebResourceRequest request) { return assets.shouldInterceptRequest(view, request); }
                    @Override public boolean shouldOverrideUrlLoading(android.webkit.WebView view, WebResourceRequest request) { return assets.shouldOverrideUrlLoading(view, request); }
                    @Override public void onPageFinished(android.webkit.WebView view, String url) {
                        if (TRIAL.isCurrent(ticket) && browser != null) focusTestField(browser, ticket, "sameActivityWebView");
                    }
                });
                owner.addContentView(browser, new ViewGroup.LayoutParams(1024, 768));
                browser.loadUrl(ProbeAssets.PAGE);
                log("sameActivityWebView start owner=" + owner.getClass().getSimpleName() + " windowDraw=on ticket=" + ticket);
                UI.postDelayed(() -> { if (TRIAL.isCurrent(ticket)) { log("sameActivityWebView autoClose"); stopTrial(); } }, HostTrialState.DURATION_MS);
            } catch (Exception error) { log("sameActivityWebView failed=" + error.getClass().getSimpleName()); stopTrial(); }
        });
    }
    static void focusTestField(ProbeWebView view, long ticket, String label) {
        if (!TRIAL.isCurrent(ticket)) return;
        view.requestFocus();
        view.evaluateJavascript("(function(){var e=document.getElementById('text');if(!e)return false;e.focus();return true;})()", result -> {
            if ("true".equals(result)) UI.postDelayed(() -> showIme(view, ticket, label), 150);
        });
    }
    public static void launch(Activity owner, boolean transparent) {
        UI.post(() -> {
            stopTrial(); final long ticket = TRIAL.begin(transparent ? "transparent" : "activity"); trialActive = true;
            Class<?> target = transparent ? TransparentHostActivity.class : OpaqueHostActivity.class;
            log("launch target=" + target.getSimpleName() + " ticket=" + ticket);
            try { owner.startActivity(new Intent(owner, target).putExtra("ticket", ticket)); }
            catch (RuntimeException error) { TRIAL.finish(ticket); trialActive = false; log("launch failed=" + error.getClass().getSimpleName()); }
        });
    }
    static boolean attach(HostComparisonActivity activity, long ticket) {
        if (!TRIAL.isCurrent(ticket)) return false;
        child = new WeakReference<>(activity); return true;
    }
    static void detached(HostComparisonActivity activity, long ticket) {
        if (child.get() != activity) return;
        child.clear(); if (TRIAL.finish(ticket)) trialActive = false;
    }
    static void hideIme(View view) {
        InputMethodManager imm = (InputMethodManager) view.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null) imm.hideSoftInputFromWindow(view.getWindowToken(), 0);
    }
    static void showIme(View view, long ticket, String label) {
        if (!TRIAL.isCurrent(ticket) || !view.isAttachedToWindow()) return;
        InputMethodManager imm = (InputMethodManager) view.getContext().getSystemService(Context.INPUT_METHOD_SERVICE);
        boolean accepted = imm != null && imm.showSoftInput(view, 0, new ImeResult(view, ticket, label));
        log("imeRequest target=" + label + " accepted=" + accepted + " viewFocus=" + view.hasFocus()
                + " windowFocus=" + view.hasWindowFocus() + " immActive=" + (imm != null && imm.isActive(view)));
    }
    private static final class ImeResult extends ResultReceiver {
        final WeakReference<View> target; final long ticket; final String label;
        ImeResult(View view, long ticket, String label) { super(UI); target = new WeakReference<>(view); this.ticket = ticket; this.label = label; }
        @Override protected void onReceiveResult(int result, Bundle ignored) {
            View view = target.get();
            if (view != null && TRIAL.isCurrent(ticket)) log("imeResult target=" + label + " code=" + result);
        }
    }
}
