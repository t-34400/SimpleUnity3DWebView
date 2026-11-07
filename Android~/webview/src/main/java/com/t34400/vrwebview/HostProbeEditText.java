package com.t34400.vrwebview;

import android.content.Context;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.widget.EditText;

/** A real native editor. Only two EditorInfo flags are copied; no WebView editing is proxied. */
final class HostProbeEditText extends EditText {
    private final HostEditorProfile profile;
    private final String label;
    HostProbeEditText(Context context, HostEditorProfile profile, String label) {
        super(context); this.profile = profile; this.label = label;
        setSingleLine(true);
        setInputType(profile == null ? android.text.InputType.TYPE_CLASS_TEXT : profile.inputType);
        if (profile != null) setImeOptions(profile.imeOptions);
        setHint(profile == null ? "HC2 native EditText" : "HC2 EditText: captured WebView flags");
    }
    @Override public InputConnection onCreateInputConnection(EditorInfo info) {
        InputConnection connection = super.onCreateInputConnection(info);
        int originalType = info.inputType, originalOptions = info.imeOptions;
        // TextView may adjust flags in onCreateInputConnection. Match the final outgoing flags.
        if (profile != null) { info.inputType = profile.inputType; info.imeOptions = profile.imeOptions; }
        HostComparison.log("nativeConnection target=" + label + " nonNull=" + (connection != null)
                + " type=0x" + Integer.toHexString(info.inputType) + " imeOptions=0x" + Integer.toHexString(info.imeOptions)
                + " originalType=0x" + Integer.toHexString(originalType) + " originalOptions=0x" + Integer.toHexString(originalOptions)
                + " profileMatch=" + (profile == null ? "not-requested" : Boolean.toString(profile.matches(info.inputType, info.imeOptions))));
        return connection;
    }
}
