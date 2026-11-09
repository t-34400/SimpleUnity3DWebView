package com.t34400.vrwebview;

import android.app.Activity;
import android.content.Context;
import android.content.pm.ActivityInfo;
import android.content.pm.FeatureInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.ResultReceiver;
import android.os.SystemClock;
import android.util.Log;
import android.provider.Settings;
import android.text.InputType;
import android.view.MotionEvent;
import android.view.Surface;
import android.view.View;
import android.view.ViewGroup;
import android.view.ViewTreeObserver;
import android.view.inputmethod.InputMethodManager;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebView;
import android.widget.FrameLayout;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.lang.ref.WeakReference;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

/** Same Unity Activity, fixed local assets, native Vulkan consumer. Experimental, not a browser API. */
public final class VulkanProbe {
    static { System.loadLibrary("GfxPluginVRWebView"); }
    private static final Handler UI = new Handler(Looper.getMainLooper());
    private static final Map<Long, Host> HOSTS = new HashMap<>(); // UI thread only
    private static volatile String status = "Host not created";
    private static final String TAG = "VRWebViewFocus";
    private VulkanProbe() {}
    static native Surface nativeSurface(long id);
    static native boolean nativeReserve(long id);
    static native void nativeCancelReserve(long id);
    static native void nativeStop(long id);

    public static void open(Activity owner, long id, int width, int height) {
        openInternal(owner, id, width, height, false);
    }
    public static void openImeProxy(Activity owner, long id, int width, int height) {
        openInternal(owner, id, width, height, true);
    }
    private static void openInternal(Activity owner, long id, int width, int height, boolean proxyTrial) {
        UI.post(() -> {
            if (HOSTS.containsKey(id)) return;
            Host host = new Host(owner, id, width, height, proxyTrial);
            HOSTS.put(id, host);
            try { host.start(); }
            catch (Exception error) {
                HOSTS.remove(id);
                for (String row : HostFailureMetadata.describe(error, host.startStage))
                    host.trace((proxyTrial ? "proxy=IP1 " : "") + row);
                try { host.close(); }
                catch (RuntimeException cleanup) {
                    for (String row : HostFailureMetadata.describe(cleanup, "host_cleanup")) host.trace(row);
                }
                finally { status = "Host failed at " + host.startStage + ": " + error.getClass().getSimpleName(); }
            }
        });
    }
    public static void close(long id) {
        UI.post(() -> { Host host = HOSTS.remove(id); if (host != null) host.close(); else nativeStop(id); });
    }
    public static String status() { return status; }
    public static void requestIme(long id) {
        UI.post(() -> {
            Host host = HOSTS.get(id);
            if (host == null || host.closed || host.web == null) {
                Log.i(TAG, "[VRWebViewFocus] host=" + id + " proxy=IP1 event=ime_request_rejected reason=host_unavailable");
                return;
            }
            if (host.proxy != null && host.proxy.isEnabled()) { host.proxy.request(); return; }
            host.focusRecovery.claimBrowser();
            host.web.requestFocus();
            host.showIme("manual", 0);
            host.updateStatus();
        });
    }
    public static void setImeProxy(long id, boolean enabled) {
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.closed || host.proxy == null) return;
            host.hideIme(); host.proxy.setEnabled(enabled);
            host.focusRecovery.setEnabled(false); // Do not mix recovery retries with this trial.
            host.updateStatus();
        });
    }
    public static void requestNativeIme(long id) {
        UI.post(() -> {
            Host host = HOSTS.get(id);
            if (host == null || host.closed || host.proxy == null) return;
            host.proxy.requestNativeControl();
        });
    }
    public static void releaseFocus(long id) {
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.web == null) return;
            host.returnFocus("button");
        });
    }
    public static void setFocusRecovery(long id, boolean enabled) {
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.closed) return;
            host.focusRecovery.setEnabled(enabled);
            host.trace("focusRecovery enabled=" + enabled);
        });
    }
    /** Opt-in recovery experiment: arm BEFORE selecting a field; does not require a live XR ray. */
    public static void armFocusReturn(long id) {
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.web == null || host.closed) return;
            UI.removeCallbacks(host.delayedFocusReturn);
            host.focusReturnAt = SystemClock.uptimeMillis() + 5000;
            host.trace("focusReturn armed=5s");
            UI.postDelayed(host.delayedFocusReturn, 5000);
        });
    }
    public static void reload(long id) {
        UI.post(() -> { Host host = HOSTS.get(id); if (host != null && host.web != null) { if (host.proxy != null) host.proxy.cancel("reload"); host.videoTelemetry.reset(); host.trace("video=bundled event=reload"); host.web.reload(); } });
    }
    /** Normalized viewport coordinates, y down; never add DOM scroll offsets. */
    public static void pointer(long id, int action, float x, float y) {
        if (!Float.isFinite(x) || !Float.isFinite(y) || action < 0 || action > 3) return;
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.web == null) return;
            if (action == MotionEvent.ACTION_DOWN) {
                host.cancelTouch(); host.downTime = SystemClock.uptimeMillis(); host.touching = true;
                if (host.proxy != null && host.proxy.isEnabled()) host.proxy.arm();
                host.focusRecovery.claimBrowser();
                boolean accepted = host.web.requestFocus();
                host.trace("touchDown focusRequest=" + accepted);
            } else if (!host.touching) return;
            MotionEvent event = MotionEvent.obtain(host.downTime, SystemClock.uptimeMillis(), action,
                    x * host.width, y * host.height, 0);
            try { host.web.dispatchTouchEvent(event); } finally { event.recycle(); }
            if (action != MotionEvent.ACTION_MOVE) {
                host.lastTouchAction = action;
                host.trace("touchAction=" + action);
            }
            if (action == MotionEvent.ACTION_UP || action == MotionEvent.ACTION_CANCEL) {
                host.touching = false;
                if (host.proxy != null) host.proxy.gestureEnded(action == MotionEvent.ACTION_CANCEL);
            }
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
        final Activity owner; final long id; final int width, height; final boolean proxyTrial;
        ImeProxyProbe proxy;
        final FocusCallTrace focusCalls = new FocusCallTrace(this::trace);
        final ProbeVideoTelemetry videoTelemetry = new ProbeVideoTelemetry(this::trace);
        ProbeWebView web; FrameLayout panel; Surface surface; View previousFocus;
        boolean closed, touching, imeAccepted;
        long downTime;
        long focusReturnAt;
        int fullscreenRequests, imeRequests, focusReturns, focusChanges, focusRecoveries, lastTouchAction = -1;
        final ProbeFocusRecovery focusRecovery = new ProbeFocusRecovery();
        String lastFocusMove = "none", lastState;
        String startStage = "new_host";
        ViewTreeObserver focusObserver;
        final ViewTreeObserver.OnGlobalFocusChangeListener focusListener = (before, after) -> {
            focusChanges++;
            lastFocusMove = viewName(before) + ">" + viewName(after);
            trace("globalFocus " + lastFocusMove);
        };
        final Runnable delayedFocusReturn = () -> {
            focusReturnAt = 0;
            if (!closed && web != null) returnFocus("timer");
        };
        Host(Activity owner, long id, int width, int height, boolean proxyTrial) {
            this.owner = owner; this.id = id; this.width = width; this.height = height; this.proxyTrial = proxyTrial;
        }
        void start() throws Exception {
            startStage = "native_surface";
            surface = nativeSurface(id);
            if (surface == null) throw new IllegalStateException("Native surface unavailable");
            previousFocus = owner.getCurrentFocus();
            startStage = "webview_construct";
            panel = new FrameLayout(owner);
            web = new ProbeWebView(owner);
            web.metadataListener = this::trace;
            web.windowFocusListener = this::windowFocusChanged;
            web.suppressWindowDraw = true;
            web.setFocusable(true); web.setFocusableInTouchMode(true);
            web.getSettings().setJavaScriptEnabled(true);
            web.getSettings().setAllowFileAccess(false); web.getSettings().setAllowContentAccess(false);
            web.getSettings().setMediaPlaybackRequiresUserGesture(true);
            startStage = "local_assets";
            web.setWebViewClient(new ProbeAssets(owner.getAssets(), proxyTrial));
            web.setWebChromeClient(new WebChromeClient() {
                @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
                @Override public void onShowCustomView(View view, CustomViewCallback callback) {
                    fullscreenRequests++; callback.onCustomViewHidden();
                }
            });
            startStage = "webview_attach";
            panel.addView(web, new FrameLayout.LayoutParams(width, height));
            owner.addContentView(panel, new ViewGroup.LayoutParams(width, height));
            if (proxyTrial) {
                startStage = "proxy_construct";
                proxy = new ImeProxyProbe(owner, web, panel, this::trace, () -> { if (!closed && web != null) returnFocus("proxy_done_or_back"); });
                proxy.setEnabled(true); focusRecovery.setEnabled(false);
            }
            startStage = "focus_observer";
            focusObserver = owner.getWindow().getDecorView().getViewTreeObserver();
            focusObserver.addOnGlobalFocusChangeListener(focusListener);
            trace("probe=FO1 event=focus_trace_installed");
            trace("hostOpened previous=" + viewName(previousFocus));
            traceCapabilities();
            startStage = "page_load";
            web.loadUrl(ProbeAssets.PAGE);
            UI.post(draw);
            startStage = "started";
            trace((proxyTrial ? "proxy=IP1 " : "") + "event=host_started stage=" + startStage);
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
            if (proxy != null) proxy.pollMetadata();
            videoTelemetry.poll(web);
            InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            boolean active = imm != null && imm.isActive(web);
            String state = "viewFocus=" + web.hasFocus() + " windowFocus=" + web.hasWindowFocus()
                    + " immActive=" + active + " owner=" + viewName(owner.getCurrentFocus())
                    + " inputConnections=" + web.connectionRequests + "/" + web.nonNullConnections;
            if (!state.equals(lastState)) { trace("state " + state); lastState = state; }
            status = (proxy == null ? "" : "proxy=IP1 enabled=" + proxy.isEnabled() + "\n") + "sameActivity=" + owner.getClass().getSimpleName() + "\nviewFocus=" + web.hasFocus()
                    + " windowFocus=" + web.hasWindowFocus() + " immActive=" + active
                    + " imeAccepted=" + imeAccepted + "\nattached=" + web.isAttachedToWindow() + " shown=" + web.isShown()
                    + " view=" + web.getWidth() + "x" + web.getHeight() + " window=" + panel.getRootView().getWidth() + "x" + panel.getRootView().getHeight()
                    + "\ncanvas=" + web.draws + " hardware=" + web.targetHardware + " windowDraw=off"
                    + " scroll=" + web.getScrollX() + "," + web.getScrollY()
                    + "\ninputConnections=" + web.connectionRequests + "/" + web.nonNullConnections
                    + " fullscreenRequests=" + fullscreenRequests
                    + "\nfocusOwner=" + viewName(owner.getCurrentFocus()) + " changes=" + focusChanges
                    + " returns=" + focusReturns + " imeRequests=" + imeRequests
                    + " recovery=" + focusRecovery.isEnabled() + "/" + focusRecoveries
                    + "\nfocusMove=" + lastFocusMove + " touch=" + lastTouchAction
                    + " returnIn=" + (focusReturnAt == 0 ? "off" : Math.max(0, focusReturnAt - SystemClock.uptimeMillis()) + "ms");
        }
        // Deliberately avoid getText(), hint/editor contents, URLs, and connection retention.
        void trace(String event) {
            Log.i(TAG, "[VRWebViewFocus] host=" + id + " " + event);
            // All metadata here comes from our own focus callbacks/actions, never field text.
            String kind = event.startsWith("globalFocus ") ? "view_change"
                    : event.startsWith("webFocusEnter=") ? "web_entry"
                    : event.startsWith("webFocus=") ? "web_exit"
                    : event.startsWith("windowFocusEnter=") ? "window_entry"
                    : event.startsWith("windowFocus=") ? "window_exit"
                    : event.startsWith("returnFocus ") ? "app_return"
                    : event.startsWith("proxy=IP1 event=proxy_focus_changed ") ? "native_view"
                    : null;
            if (kind != null) {
                String metadata = kind.equals("native_view") ? event.substring("proxy=IP1 event=proxy_focus_changed ".length()) : event;
                focusCalls.record(kind, metadata + " current=" + viewName(owner.getCurrentFocus()), new Throwable().getStackTrace());
            }
        }
        void returnFocus(String reason) {
            focusRecovery.release();
            focusReturnAt = 0; UI.removeCallbacks(delayedFocusReturn);
            focusReturns++;
            trace("returnFocus reason=" + reason + " target=" + viewName(previousFocus));
            cancelTouch(); hideIme(); if (proxy != null) proxy.cancel("return_unity"); web.clearFocus();
            if (previousFocus != null && previousFocus.isAttachedToWindow()) previousFocus.requestFocus();
            updateStatus();
        }
        void windowFocusChanged(boolean focused) {
            if (!focused) { focusRecovery.windowLost(web.hasFocus()); return; }
            long ticket = focusRecovery.windowGained();
            if (ticket < 0) return;
            // Let the Activity finish its window-focus callback before restoring the
            // view that owned browser input. Never poll/reclaim focus every frame.
            UI.post(() -> {
                if (closed || web == null || !web.isAttachedToWindow() || !web.hasWindowFocus()
                        || !focusRecovery.consume(ticket)) return;
                boolean alreadyFocused = web.hasFocus();
                if (!alreadyFocused) focusRecoveries++;
                boolean accepted = alreadyFocused || web.requestFocus();
                trace("focusRecovery request=" + accepted + " owner=" + viewName(owner.getCurrentFocus()));
                // Focus restoration creates an InputConnection but need not resend
                // the original (now lost) show request. Wait once for that connection.
                if (accepted) {
                    long token = focusRecovery.currentToken();
                    UI.postDelayed(() -> retryImeAfterFocus(token), 100);
                }
                updateStatus();
            });
        }
        void retryImeAfterFocus(long token) {
            if (closed || web == null || !focusRecovery.isCurrent(token)) return;
            if (!web.isAttachedToWindow() || !web.hasFocus() || !web.hasWindowFocus()
                    || !web.lastConnectionNonNull || web.inputType == InputType.TYPE_NULL) {
                trace("recoveryIme skipped: focus/window/editor not ready"); return;
            }
            showIme("focusRecovery", InputMethodManager.SHOW_IMPLICIT);
            updateStatus();
        }
        void showIme(String reason, int flags) {
            InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            int request = ++imeRequests;
            ResultReceiver receiver = new ImeResultReceiver(this, request, reason);
            imeAccepted = imm != null && imm.showSoftInput(web, flags, receiver);
            trace("imeRequest request=" + request + " reason=" + reason + " accepted=" + imeAccepted
                    + " viewFocus=" + web.hasFocus() + " windowFocus=" + web.hasWindowFocus()
                    + " immActive=" + (imm != null && imm.isActive(web)) + " editor=" + web.lastConnectionNonNull);
        }
        void traceCapabilities() {
            try {
                PackageManager pm = owner.getPackageManager();
                PackageInfo app = pm.getPackageInfo(owner.getPackageName(), PackageManager.GET_CONFIGURATIONS);
                boolean overlay = false;
                if (app.reqFeatures != null) for (FeatureInfo feature : app.reqFeatures)
                    if ("oculus.software.overlay_keyboard".equals(feature.name)) overlay = true;
                ActivityInfo activity = pm.getActivityInfo(owner.getComponentName(), PackageManager.GET_META_DATA);
                boolean aware = activity.metaData != null && activity.metaData.getBoolean("com.oculus.vr.focusaware", false);
                PackageInfo provider = WebView.getCurrentWebViewPackage();
                trace("imeCapabilities sdk=" + Build.VERSION.SDK_INT + " overlayDeclared=" + overlay + " focusAware=" + aware
                        + " bootKeyboard=" + bootKeyboardSetting() + " webView=" + (provider == null ? "unknown" : provider.packageName + "/" + provider.versionName)
                        + " windowFlags=0x" + Integer.toHexString(owner.getWindow().getAttributes().flags)
                        + " softInputMode=0x" + Integer.toHexString(owner.getWindow().getAttributes().softInputMode));
            } catch (Exception error) { trace("imeCapabilities unavailable=" + error.getClass().getSimpleName()); }
            try { trace("defaultIme=" + Settings.Secure.getString(owner.getContentResolver(), Settings.Secure.DEFAULT_INPUT_METHOD)); }
            catch (Exception error) { trace("defaultIme unavailable=" + error.getClass().getSimpleName()); }
        }
        String bootKeyboardSetting() {
            try (BufferedReader reader = new BufferedReader(new InputStreamReader(
                    owner.getAssets().open("bin/Data/boot.config"), StandardCharsets.UTF_8))) {
                String line;
                while ((line = reader.readLine()) != null) {
                    if (line.equals("xr-keyboard-overlay-enabled=1")) return "1";
                    if (line.equals("xr-keyboard-overlay-enabled=0")) return "0";
                }
                return "missing";
            } catch (Exception error) { return "unavailable"; }
        }
        void cancelTouch() {
            if (!touching || web == null) return;
            MotionEvent event = MotionEvent.obtain(downTime, SystemClock.uptimeMillis(), MotionEvent.ACTION_CANCEL, 0, 0, 0);
            try { web.dispatchTouchEvent(event); } finally { event.recycle(); }
            touching = false;
        }
        void hideIme() {
            if (web == null) return;
            if (proxy != null) proxy.hideIme();
            InputMethodManager imm = (InputMethodManager) owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm != null && imm.isActive(web)) {
                trace((proxy == null ? "" : "proxy=IP1 ") + "event=app_hide target=web"); imm.hideSoftInputFromWindow(web.getWindowToken(), 0);
            }
        }
        void close() {
            if (closed) return;
            trace("hostClosed");
            focusRecovery.release();
            closed = true; UI.removeCallbacks(draw); UI.removeCallbacks(delayedFocusReturn);
            videoTelemetry.close();
            if (focusObserver != null && focusObserver.isAlive()) focusObserver.removeOnGlobalFocusChangeListener(focusListener);
            if (web != null) { web.metadataListener = null; web.windowFocusListener = null; }
            cancelTouch(); hideIme();
            if (proxy != null) { proxy.close(); proxy = null; }
            if (web != null) { web.stopLoading(); web.onPause(); panel.removeView(web); web.destroy(); web = null; }
            if (panel != null && panel.getParent() instanceof ViewGroup) ((ViewGroup) panel.getParent()).removeView(panel);
            if (surface != null) { surface.release(); surface = null; }
            if (previousFocus != null && previousFocus.isAttachedToWindow()) previousFocus.requestFocus();
            nativeStop(id); // consumer teardown is deferred until Unity's safe frame
            status = "Host closed";
        }
    }
    // An IME need not reply. Its callback must not retain an obsolete host/Activity.
    private static final class ImeResultReceiver extends ResultReceiver {
        final WeakReference<Host> target;
        final int request;
        final String reason;
        ImeResultReceiver(Host host, int request, String reason) {
            super(UI); target = new WeakReference<>(host); this.request = request; this.reason = reason;
        }
        @Override protected void onReceiveResult(int resultCode, Bundle ignored) {
            Host host = target.get();
            if (host != null && !host.closed && request == host.imeRequests)
                host.trace("imeResult request=" + request + " reason=" + reason + " code=" + resultCode);
        }
    }
    private static String viewName(View view) {
        return view == null ? "none" : view.getClass().getSimpleName() + "#" + Integer.toHexString(System.identityHashCode(view));
    }
}
