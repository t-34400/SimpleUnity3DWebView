package com.t34400.vrwebview;

import android.app.Activity;
import android.content.pm.ActivityInfo;
import android.content.pm.FeatureInfo;
import android.content.pm.PackageInfo;
import android.content.pm.PackageManager;
import android.os.Build;
import android.provider.Settings;
import android.webkit.WebView;
import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;

/** Logs setup metadata only, never other boot settings or entered text. */
final class HostProbeEnvironment {
    private HostProbeEnvironment() {}
    static void trace(Activity owner) {
        try {
            PackageManager pm = owner.getPackageManager();
            PackageInfo app = pm.getPackageInfo(owner.getPackageName(), PackageManager.GET_CONFIGURATIONS);
            boolean overlay = false;
            if (app.reqFeatures != null) for (FeatureInfo f : app.reqFeatures)
                if ("oculus.software.overlay_keyboard".equals(f.name)) overlay = true;
            ActivityInfo info = pm.getActivityInfo(owner.getComponentName(), PackageManager.GET_META_DATA);
            boolean aware = info.metaData != null && info.metaData.getBoolean("com.oculus.vr.focusaware", false);
            PackageInfo provider = WebView.getCurrentWebViewPackage();
            HostComparison.log("setup owner=" + owner.getClass().getSimpleName() + " sdk=" + Build.VERSION.SDK_INT
                    + " overlayDeclared=" + overlay + " focusAware=" + aware + " bootKeyboard=" + bootKeyboard(owner)
                    + " provider=" + (provider == null ? "unknown" : provider.packageName + "/" + provider.versionName));
        } catch (Exception error) { HostComparison.log("setup unavailable=" + error.getClass().getSimpleName()); }
        try { HostComparison.log("defaultIme=" + Settings.Secure.getString(owner.getContentResolver(), Settings.Secure.DEFAULT_INPUT_METHOD)); }
        catch (Exception error) { HostComparison.log("defaultIme unavailable=" + error.getClass().getSimpleName()); }
    }
    private static String bootKeyboard(Activity owner) {
        try (BufferedReader reader = new BufferedReader(new InputStreamReader(owner.getAssets().open("bin/Data/boot.config"), StandardCharsets.UTF_8))) {
            String line;
            while ((line = reader.readLine()) != null) {
                if (line.equals("xr-keyboard-overlay-enabled=1")) return "1";
                if (line.equals("xr-keyboard-overlay-enabled=0")) return "0";
            }
            return "missing";
        } catch (Exception error) { return "unavailable"; }
    }
}
