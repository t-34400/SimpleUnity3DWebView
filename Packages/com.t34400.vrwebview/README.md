# VRWebView preview

Session/input foundations and explicit Android WebView IME probes for Unity 6000.4.

This preview does **not** implement production GPU rendering into a Unity texture. Standalone Activity probes do not prove Unity/XR focus coexistence.

Import the IME Probe sample through Package Manager, then use **VRWebView > Create IME Probe Scene**. See `Documentation~/ImeProbe.md` for the test matrix and limitations.

Core is independent of Unity and Meta SDK. UnityUI uses uGUI; the sample Android probe uses a bundled AAR built from this repository's `Android~` project. Web permissions are denied by default; probe input is not logged.
