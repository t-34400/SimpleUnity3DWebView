# Public JNI entry points are called by Unity using their names.
-keep class com.t34400.vrwebview.ImeProbe { public *; }
-keep class com.t34400.vrwebview.GpuProbe { public *; }
-keep class com.t34400.vrwebview.VulkanProbe { *; }
-keep class com.t34400.vrwebview.ActivityGpuProbe { public *; }
-keep class com.t34400.vrwebview.HostComparison { public *; }
-keepclassmembers class com.t34400.vrwebview.ActivityGpuProbe$EditMetadata {
    @android.webkit.JavascriptInterface <methods>;
}

-keep class com.t34400.vrwebview.RuntimeProbeContent { public *; }
