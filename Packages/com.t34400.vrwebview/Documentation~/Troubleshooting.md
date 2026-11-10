# Troubleshooting

- **No web page in Editor:** pages render in Android players. Check the scene and
  build on Quest; an Editor Play session is not a WebView implementation.
- **Black panel on device:** check Android ARM64, Vulkan, the assigned View, and
  `BrowserController.LastError`. Native rendering plugins must be included.
- **No ray:** verify Android OpenXR, the active input module, and either a tracked
  controller or hand tracking/Meta Hand Tracking Aim. Avoid duplicate EventSystems.
- **No keyboard:** check the overlay keyboard manifest declaration and focus
  awareness. For Unity fields also enable the keyboard overlay boot setting.
- **Unity field and keyboard disagree:** use the version-checked GameActivity
  compatibility patch for the tested Unity/GameActivity version. Keep the normal
  InputField implementation; do not copy the keyboard text into DOM fields.
- **Compatibility build rejected:** the dependency version or artifact differs
  from the supported version. Do not bypass the check; update the patch for that
  dependency, or disable it and validate the platform input path separately.
- **Old browser sample after a package update:** Package Manager copies samples
  into Assets. Update that copy as described in its README; updating the package
  alone does not update imported sample code.
- **Video plays without sound:** check the player mute control and headset output
  settings with a known audible video. Audio output is Android WebView's path;
  lack of sound alone does not identify a GPU transfer failure.

Maintainer probes are in the separate development diagnostics package. They are
not required to install or use the runtime or browser sample.
