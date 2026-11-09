package com.t34400.vrwebview;
import android.view.Surface;
/** Shared producer queue ABI. Called on the Android UI thread. */
public final class GpuBridge {
    static { System.loadLibrary("GfxPluginVRWebView"); }
    private GpuBridge() {}
    public static native Surface nativeSurface(long id);
    public static native boolean nativeReserve(long id);
    public static native void nativeCancelReserve(long id);
    public static native void nativeStop(long id);
}
