package com.t34400.vrwebview;

import android.app.Activity;
import android.graphics.Color;
import android.os.Bundle;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebViewClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;

/** Native foreground test host. It does not claim continuous Unity XR rendering. */
public abstract class HostComparisonActivity extends Activity {
    private long ticket;
    private ProbeWebView web;
    private EditText editor;
    private boolean closed;
    private String lastState;
    protected abstract boolean transparent();
    private void trace(String event) { HostComparison.log(getClass().getSimpleName() + " " + event); }
    @Override public void onCreate(Bundle state) {
        super.onCreate(state); ticket = getIntent().getLongExtra("ticket", -1);
        if (!HostComparison.attach(this, ticket)) { finish(); return; }
        trace("created transparent=" + transparent() + " ticket=" + ticket + " autoReturnSeconds=20");
        LinearLayout panel = new LinearLayout(this); panel.setOrientation(LinearLayout.VERTICAL);
        panel.setBackgroundColor(transparent() ? 0x44202020 : Color.WHITE);
        TextView title = new TextView(this); title.setText("HC1: native host; auto return in 20 seconds"); panel.addView(title);
        LinearLayout buttons = new LinearLayout(this); panel.addView(buttons);
        button(buttons, "Web field / IME", this::focusWeb);
        button(buttons, "EditText / IME", () -> {
            editor.requestFocus(); HostComparison.showIme(editor, ticket, "separateEditText");
        });
        button(buttons, "Return to Unity", this::finish);
        editor = new EditText(this); editor.setSingleLine(true); editor.setInputType(android.text.InputType.TYPE_CLASS_TEXT);
        editor.setHint("HC1 native EditText"); panel.addView(editor);
        web = new ProbeWebView(this); web.setFocusable(true); web.setFocusableInTouchMode(true);
        web.metadataListener = event -> trace("web " + event);
        web.setBackgroundColor(transparent() ? Color.TRANSPARENT : Color.WHITE);
        if (transparent()) web.setAlpha(0.35f);
        web.getSettings().setJavaScriptEnabled(true);
        web.getSettings().setAllowFileAccess(false); web.getSettings().setAllowContentAccess(false);
        web.getSettings().setMediaPlaybackRequiresUserGesture(true);
        web.setWebChromeClient(new WebChromeClient() {
            @Override public void onPermissionRequest(PermissionRequest request) { request.deny(); }
        });
        final ProbeAssets assets;
        try { assets = new ProbeAssets(getAssets()); }
        catch (java.io.IOException error) { trace("assets failed=" + error.getClass().getSimpleName()); finish(); return; }
        web.setWebViewClient(new WebViewClient() {
            @Override public WebResourceResponse shouldInterceptRequest(android.webkit.WebView view, WebResourceRequest request) {
                return assets.shouldInterceptRequest(view, request);
            }
            @Override public boolean shouldOverrideUrlLoading(android.webkit.WebView view, WebResourceRequest request) {
                return assets.shouldOverrideUrlLoading(view, request);
            }
            @Override public void onPageFinished(android.webkit.WebView view, String url) {
                super.onPageFinished(view, url); if (!closed) focusWeb();
            }
        });
        panel.addView(web, new LinearLayout.LayoutParams(-1, 0, 1)); setContentView(panel);
        web.loadUrl(ProbeAssets.PAGE);
        HostComparison.UI.postDelayed(autoReturn, HostTrialState.DURATION_MS);
        HostComparison.UI.post(poll);
    }
    private void button(LinearLayout parent, String text, Runnable action) {
        Button button = new Button(this); button.setText(text); button.setOnClickListener(v -> action.run()); parent.addView(button);
    }
    private void focusWeb() {
        if (closed || web == null || !HostComparison.TRIAL.isCurrent(ticket)) return;
        HostComparison.focusTestField(web, ticket, "separateWebView");
    }
    private final Runnable autoReturn = () -> { if (!closed) { trace("autoReturn"); finish(); } };
    private final Runnable poll = new Runnable() {
        @Override public void run() {
            if (closed || web == null) return;
            android.view.inputmethod.InputMethodManager imm = (android.view.inputmethod.InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
            String current = "windowFocus=" + hasWindowFocus() + " owner=" + HostComparison.viewName(getCurrentFocus())
                    + " webFocus=" + web.hasFocus() + " immActive=" + (imm != null && imm.isActive(web))
                    + " connections=" + web.connectionRequests + "/" + web.nonNullConnections;
            if (!current.equals(lastState)) { trace(current); lastState = current; }
            HostComparison.UI.postDelayed(this, 500);
        }
    };
    @Override protected void onResume() { super.onResume(); trace("resumed"); }
    @Override protected void onPause() { trace("paused"); super.onPause(); }
    @Override protected void onStop() { trace("stopped"); super.onStop(); }
    @Override public void onWindowFocusChanged(boolean focused) { super.onWindowFocusChanged(focused); trace("windowFocus=" + focused); }
    @Override protected void onDestroy() {
        closed = true; HostComparison.UI.removeCallbacks(autoReturn); HostComparison.UI.removeCallbacks(poll);
        if (web != null) {
            HostComparison.hideIme(web); web.metadataListener = null; web.stopLoading();
            if (web.getParent() instanceof ViewGroup) ((ViewGroup) web.getParent()).removeView(web);
            web.destroy(); web = null;
        }
        if (editor != null) HostComparison.hideIme(editor);
        HostComparison.detached(this, ticket); trace("destroyed"); super.onDestroy();
    }
}
