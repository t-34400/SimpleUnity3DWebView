package com.t34400.vrwebview;

import android.app.Activity;
import android.content.Context;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.util.Log;
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
            host.focusRecovery.claimBrowser();
            host.web.requestFocus();
            InputMethodManager imm = (InputMethodManager) host.owner.getSystemService(Context.INPUT_METHOD_SERVICE);
            host.imeRequests++;
            host.imeAccepted = imm != null && imm.showSoftInput(host.web, 0);
            host.trace("explicitIme accepted=" + host.imeAccepted);
            host.updateStatus();
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
        UI.post(() -> { Host host = HOSTS.get(id); if (host != null && host.web != null) host.web.reload(); });
    }
    /** Normalized viewport coordinates, y down; never add DOM scroll offsets. */
    public static void pointer(long id, int action, float x, float y) {
        if (!Float.isFinite(x) || !Float.isFinite(y) || action < 0 || action > 3) return;
        UI.post(() -> {
            Host host = HOSTS.get(id); if (host == null || host.web == null) return;
            if (action == MotionEvent.ACTION_DOWN) {
                host.cancelTouch(); host.downTime = SystemClock.uptimeMillis(); host.touching = true;
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
        long focusReturnAt;
        int fullscreenRequests, imeRequests, focusReturns, focusChanges, focusRecoveries, lastTouchAction = -1;
        final ProbeFocusRecovery focusRecovery = new ProbeFocusRecovery();
        String lastFocusMove = "none", lastState;
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
        Host(Activity owner, long id, int width, int height) {
            this.owner = owner; this.id = id; this.width = width; this.height = height;
        }
        void start() throws Exception {
            surface = nativeSurface(id);
            if (surface == null) throw new IllegalStateException("Native surface unavailable");
            previousFocus = owner.getCurrentFocus();
            panel = new FrameLayout(owner);
            web = new ProbeWebView(owner);
            web.metadataListener = this::trace;
            web.windowFocusListener = this::windowFocusChanged;
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
            focusObserver = owner.getWindow().getDecorView().getViewTreeObserver();
            focusObserver.addOnGlobalFocusChangeListener(focusListener);
            trace("hostOpened previous=" + viewName(previousFocus));
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
            boolean active = imm != null && imm.isActive(web);
            String state = "viewFocus=" + web.hasFocus() + " windowFocus=" + web.hasWindowFocus()
                    + " immActive=" + active + " owner=" + viewName(owner.getCurrentFocus())
                    + " inputConnections=" + web.connectionRequests + "/" + web.nonNullConnections;
            if (!state.equals(lastState)) { trace("state " + state); lastState = state; }
            status = "sameActivity=" + owner.getClass().getSimpleName() + "\nviewFocus=" + web.hasFocus()
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
        }
        void returnFocus(String reason) {
            focusRecovery.release();
            focusReturnAt = 0; UI.removeCallbacks(delayedFocusReturn);
            focusReturns++;
            trace("returnFocus reason=" + reason + " target=" + viewName(previousFocus));
            cancelTouch(); hideIme(); web.clearFocus();
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
                if (web.hasFocus()) { trace("focusRecovery unnecessary"); return; }
                focusRecoveries++;
                boolean accepted = web.requestFocus();
                trace("focusRecovery request=" + accepted + " owner=" + viewName(owner.getCurrentFocus()));
                updateStatus();
            });
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
            trace("hostClosed");
            focusRecovery.release();
            closed = true; UI.removeCallbacks(draw); UI.removeCallbacks(delayedFocusReturn);
            if (focusObserver != null && focusObserver.isAlive()) focusObserver.removeOnGlobalFocusChangeListener(focusListener);
            if (web != null) { web.metadataListener = null; web.windowFocusListener = null; }
            cancelTouch(); hideIme();
            if (web != null) { web.stopLoading(); web.onPause(); panel.removeView(web); web.destroy(); web = null; }
            if (panel != null && panel.getParent() instanceof ViewGroup) ((ViewGroup) panel.getParent()).removeView(panel);
            if (surface != null) { surface.release(); surface = null; }
            if (previousFocus != null && previousFocus.isAttachedToWindow()) previousFocus.requestFocus();
            nativeStop(id); // consumer teardown is deferred until Unity's safe frame
            status = "Host closed";
        }
    }
    private static String viewName(View view) {
        return view == null ? "none" : view.getClass().getSimpleName() + "#" + Integer.toHexString(System.identityHashCode(view));
    }
}
