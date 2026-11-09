# Unity invokes these entry points by name. No JavascriptInterface is exposed.
-keep class com.t34400.vrwebview.BrowserHost { public *; }
-keep interface com.t34400.vrwebview.BrowserHost$Callback { *; }
-keep class com.t34400.vrwebview.GpuBridge { *; }
