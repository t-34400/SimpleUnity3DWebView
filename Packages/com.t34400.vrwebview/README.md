# VRWebView preview

Session/input foundations and Android WebView GPU, video and IME comparison probes for Unity 6000.4.

This preview does **not** implement production GPU rendering into a Unity texture. Standalone Activity probes do not prove Unity/XR focus coexistence.

Import the IME Probe sample through Package Manager, then use **VRWebView > Create IME Probe Scene**. See `Documentation~/ImeProbe.md` for the test matrix and limitations.

For the video/GPU comparison use **VRWebView > Create GPU Probe Scene**; no sample import is required. The native screen switches between normal WebView, host-window hardware Canvas, and VirtualDisplay GPU output without rebuilding. See `Documentation~/GpuProbe.md`. The bundled synthetic H.264/AAC clip makes moving video pixels independently observable; successful capture and IME behavior still require device validation.

Core is independent of Unity and Meta SDK. UnityUI uses uGUI; the sample Android probe uses a bundled AAR built from this repository's `Android~` project. Web permissions are denied by default; probe input is not logged.
