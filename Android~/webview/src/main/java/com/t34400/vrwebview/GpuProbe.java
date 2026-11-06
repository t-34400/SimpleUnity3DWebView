package com.t34400.vrwebview;

import android.app.Activity;
import android.content.Intent;

/** Stable Unity JNI entry point. All modes start an independent diagnostic Activity. */
public final class GpuProbe {
    private GpuProbe() {}
    public static void launch(Activity activity, int mode, String unityGraphicsApi) {
        if (mode < 0 || mode > 2) throw new IllegalArgumentException("Unknown GPU probe mode");
        activity.runOnUiThread(() -> activity.startActivity(new Intent(activity, GpuProbeActivity.class)
                .putExtra("mode", mode).putExtra("unityGraphicsApi", unityGraphicsApi)
                .putExtra("originActivity", activity.getClass().getName())));
    }
}
