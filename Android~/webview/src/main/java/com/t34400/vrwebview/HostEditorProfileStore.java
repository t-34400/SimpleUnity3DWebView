package com.t34400.vrwebview;

import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.PackageInfo;
import android.webkit.WebView;

/** App-local calibration for the bundled page's #text field. Invalid after provider changes. */
final class HostEditorProfileStore {
    private HostEditorProfileStore() {}
    private static SharedPreferences preferences(Context context) {
        return context.getSharedPreferences("vrwebview-hc2-text-profile-v1", Context.MODE_PRIVATE);
    }
    private static String provider() {
        PackageInfo info = WebView.getCurrentWebViewPackage();
        return info == null ? null : info.packageName + "/" + info.versionName + "/" + info.getLongVersionCode();
    }
    static void save(Context context, int type, int options, String source) {
        String provider = provider();
        if (provider == null || (type & 0xf) != 1) return;
        preferences(context).edit().putString("provider", provider)
                .putInt("inputType", type).putInt("imeOptions", options).apply();
        HostComparison.log("editorProfile saved source=" + source + " field=text provider=" + provider
                + " type=0x" + Integer.toHexString(type) + " imeOptions=0x" + Integer.toHexString(options));
    }
    static HostEditorProfile load(Context context) {
        SharedPreferences prefs = preferences(context);
        if (!prefs.contains("inputType") || !prefs.contains("imeOptions")) return null;
        try {
            HostEditorProfile profile = new HostEditorProfile(prefs.getString("provider", null),
                    prefs.getInt("inputType", 0), prefs.getInt("imeOptions", 0));
            return profile.matches(provider()) ? profile : null;
        } catch (IllegalArgumentException error) { return null; }
    }
}
