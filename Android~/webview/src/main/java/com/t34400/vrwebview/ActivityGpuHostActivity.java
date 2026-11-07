package com.t34400.vrwebview;

import android.app.Activity;
import android.graphics.Color;
import android.os.Bundle;
import android.view.Gravity;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import java.util.ArrayList;

/** Transparent source Window; no Unity Activity subclass or private IME bridge. */
public final class ActivityGpuHostActivity extends Activity {
    private ActivityGpuProbe.Session session;
    private final ArrayList<Button> buttons = new ArrayList<>();
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        session = ActivityGpuProbe.attach(this, getIntent().getLongExtra("session", -1));
        if (session == null) { finish(); return; }
        FrameLayout root = new FrameLayout(this); root.setBackgroundColor(Color.TRANSPARENT); setContentView(root);
        try { session.create(this, root); }
        catch (Exception error) { ActivityGpuProbe.log("sourceCreateFailed=" + error.getClass().getSimpleName()); session.close("createFailure"); return; }
        LinearLayout controls = new LinearLayout(this); controls.setBackgroundColor(0xdd203040);
        button(controls, "Source ON/OFF", () -> session.setSourceVisible(!session.sourceVisible));
        button(controls, "Edit field", () -> session.focusField());
        button(controls, "Browse", () -> session.browse());
        button(controls, "Close host", () -> session.close("nativeButton"));
        FrameLayout.LayoutParams layout = new FrameLayout.LayoutParams(-1, 72, Gravity.BOTTOM); root.addView(controls, layout);
        touchPolicyChanged(session.touchPassthrough);
        ActivityGpuProbe.log("created id=" + session.id + " autoReturn=120s");
    }
    private void button(LinearLayout parent, String name, Runnable action) {
        Button b = new Button(this); b.setText(name); b.setOnClickListener(v -> { ActivityGpuProbe.log("nativeButton=" + name); action.run(); }); buttons.add(b); parent.addView(b, new LinearLayout.LayoutParams(0, -1, 1));
    }
    void touchPolicyChanged(boolean pass) {
        boolean disabled = pass || (session != null && session.mode.equals("head") && !session.sourceVisible && !session.editing);
        for (Button button : buttons) if (button.isEnabled() == disabled) button.setEnabled(!disabled);
    }
    @Override protected void onResume() { super.onResume(); if (session != null && !session.closed) { session.resumed = true; if (session.web != null) session.web.onResume(); session.updateState(); } }
    @Override protected void onPause() { if (session != null && !session.closed) { session.resumed = false; session.cancelTouch(); if (session.web != null) session.web.onPause(); session.updateState(); } super.onPause(); }
    @Override public void onWindowFocusChanged(boolean focused) { super.onWindowFocusChanged(focused); ActivityGpuProbe.log("sourceWindowFocus=" + focused); if (session != null && !session.closed) session.updateState(); }
    @Override protected void onDestroy() { if (session != null) session.close("sourceDestroyed"); session = null; super.onDestroy(); }
}
