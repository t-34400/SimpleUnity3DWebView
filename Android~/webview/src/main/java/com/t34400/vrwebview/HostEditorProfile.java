package com.t34400.vrwebview;

/** Only EditorInfo flags, never editing text or an InputConnection. */
final class HostEditorProfile {
    final String provider;
    final int inputType, imeOptions;
    HostEditorProfile(String provider, int inputType, int imeOptions) {
        if (provider == null || provider.isEmpty() || (inputType & 0xf) != 1)
            throw new IllegalArgumentException("A text editor and identified WebView provider are required");
        this.provider = provider; this.inputType = inputType; this.imeOptions = imeOptions;
    }
    boolean matches(String currentProvider) { return provider.equals(currentProvider); }
    boolean matches(int type, int options) { return inputType == type && imeOptions == options; }
}
