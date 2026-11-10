# GameActivity IME compatibility

The tested Unity 6000.4.5f1 / Google GameActivity 4.4.0 path can show a system
keyboard whose text does not follow Unity InputField edits. VRWebView includes an
optional, version-checked build patch for this combination.

After switching the build target to Android, select **VRWebView > Compatibility >
GameActivity IME > Enable Patch**, then rebuild. The setting is disabled by default
and stored per project; it is not embedded in the package or saved to your scenes.

The postprocessor changes the generated Gradle dependency only. It verifies the
original Google artifact, preserves native/prefab resources and retains the
original input connection. It adds full extracted-text updates and orders initial
keyboard display after input connection creation. It does not install a proxy
editor or synchronize text into page DOM through JavaScript.

A dependency mismatch fails the build. Updating Unity or GameActivity requires
revalidating the patch; do not remove its version/hash guard. The original
Google code remains Apache-2.0, with its license and modification notice included
under `Editor/GameActivityImeCompat`.
