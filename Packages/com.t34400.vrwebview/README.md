# VRWebView preview

Session/input foundations and Android WebView GPU, video and IME comparison probes for Unity 6000.4.

For the next separate-Activity GPU transfer test, use **VRWebView > Create Activity GPU Probe Scene**. AG1 connects a native WebView source to the existing AHardwareBuffer/Vulkan/GLES consumer and adds fixed texture/video/IME comparisons. See `Documentation~/ActivityGpuProbe.md`; the latest focus/touch changes remain unverified on device.

AG1 has user-confirmed Vulkan rendering and IME opening. Preview.15 (AG2) replaces missing display-focus notifications with an observation-only OpenXR session feature, colors Unity rays magenta, and adds the `passthrough` comparison for the transparent source Window. Enable **VRWebView > Activity GPU > Enable OpenXR Focus Probe** before rebuilding an existing AG1 scene. Unity-ray operation and the new touch mode await device confirmation.

This preview includes an experimental AHardwareBuffer-to-Unity Vulkan/OpenGL ES 3 transfer and a same-Activity XR probe. It is not a production browser backend. Standalone Activity results do not establish the new probe's Unity/XR or IME behavior.

For the unresolved Quest keyboard issue, use **VRWebView > Create Host Comparison Scene** first. HC1 isolates Unity's official keyboard API, same-Activity EditText/WebView, and separate opaque/translucent Activities. See `Documentation~/HostComparison.md` for automated WSL trials and Downloads log capture. The experimental UnityPlayerActivity switch preserves manifest capabilities and detects SDK resets; Meta SDK 207 requires GameActivity for the supported configuration.

Quest 3 observations now include native WebView/system keyboard alongside the Unity VR scene, with Japanese conversion reported for the translucent Activity. Preview.13 adds an optional XR Hands 1.7.3 reset-sync experiment for the remaining hand-ray recovery failure; its effect is awaiting device verification.

Use **VRWebView > Create GPU XR Probe Scene** for the next Quest test. Keep ARM64 enabled and choose Vulkan or OpenGLES3 for comparison. See `Documentation~/GpuXrProbe.md` for controller controls, diagnostics and the device validation sequence.

Import the IME Probe sample through Package Manager, then use **VRWebView > Create IME Probe Scene**. See `Documentation~/ImeProbe.md` for the test matrix and limitations.

For the video/GPU comparison use **VRWebView > Create GPU Probe Scene**; no sample import is required. The native screen switches between normal WebView, host-window hardware Canvas, and VirtualDisplay GPU output without rebuilding. See `Documentation~/GpuProbe.md`. The bundled synthetic H.264/AAC clip makes moving video pixels independently observable; successful capture and IME behavior still require device validation.

Core is independent of Unity and Meta SDK. UnityUI uses uGUI; the sample Android probe uses a bundled AAR built from this repository's `Android~` project. Web permissions are denied by default; input text is never logged. The XR probe records focus and input-connection metadata.

Vulkan and GLES share the Java WebView/IME host and native image queue. Only GPU import, copy and completion tracking differ; see `Documentation~/GlesSupport.md` for extension requirements and OpenXR feature limits. Existing Vulkan probe scenes retain their component GUIDs.

Hand input: `Pointer Mode=Auto` uses Meta Hand Tracking Aim when no controller is tracked. Pinch to click/drag, including probe buttons. See `Documentation~/HandInputProbe.md`. Existing scenes should enable Flip Image Vertically before rebuilding; click coordinates now follow the displayed UV rectangle.

If selecting a field hides the ray, use the HI4 hand-reset compatibility sync, one-shot IME retry and focus experiments in `Documentation~/FocusProbe.md`. These observations do not yet establish Unity/Quest IME support.

Preview.16 (AG3) adds a diagnostic `head` mode: aim with the rendered camera view and explicitly pinch, with no dwell activation. A pulsing magenta endpoint and projected hit/gesture logs separate hand-ray coordinates from native panel input. See ActivityGpuProbe.md.
