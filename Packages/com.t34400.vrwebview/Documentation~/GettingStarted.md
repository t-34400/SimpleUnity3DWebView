# Quest setup

Use the same project settings for a single panel and the browser sample.
The development baseline is **Unity 6000.4.5f1, Meta XR Core SDK 207.0.1,
OpenXR 1.16.1 and Quest 3**. Other versions need their own validation.

## Project

1. Install Unity's Android Build Support module. In **Build Profiles**, switch
   the project to Android.
2. Install Meta XR Core SDK using Meta's Unity setup instructions. Enable Android
   **OpenXR** and its Meta Quest support feature. The package installs the Unity
   uGUI, Input System, XR Hands and OpenXR dependencies used by its UI adapter.
3. In Android Player Settings, use **ARM64**, **GameActivity**, and **Vulkan**.
   Use **Input System Package (New)** for Active Input Handling.
4. For hand input, enable OpenXR hand tracking and Meta Hand Tracking Aim.
   Controllers can be used instead. The supplied scene input adapter does not
   require Meta Interaction SDK.

OpenGL ES 3 has an implementation, but Vulkan is the Quest validation baseline.
The package does not emulate web pages in the Editor.

## System keyboard

Enable focus awareness and **Requires System Keyboard** in the Meta XR settings.
The built Android manifest must declare this feature:

```xml
<uses-feature android:name="oculus.software.overlay_keyboard"
              android:required="false" />
```

Meta XR can generate the declaration. If you manage a custom main manifest,
place it under the manifest root yourself; preserve your existing launcher and
other declarations.

For Unity text fields, select **VRWebView > Project Settings > Enable Quest
Keyboard Overlay**. The rich-browser scene command also enables this setting.
For Unity 6000.4.5f1 with Google GameActivity 4.4.0, enable **VRWebView >
Compatibility > GameActivity IME > Enable Patch** before building the browser
sample. This setting is optional and stored per project. Existing enabled
projects keep their setting after updating the package.

The patch changes only the generated Android build. It preserves the original
Google input connection and rejects incompatible dependency versions. It does
not modify Unity's installation or PackageCache. More detail:
[GameActivity compatibility](GameActivityImeCompatibility.md).

## Choose a starting point

- **One panel:** select **VRWebView > Create WebView Scene**. The scene contains
  a world-space Canvas, RawImage, BrowserView, BrowserController and Quest ray
  input. Change the controller's Initial Url or use the components in your own
  scene. Browser UI and Model is not required.
- **Browser:** import **Browser UI and Model** in Package Manager, then select
  **VRWebView > Create Rich Browser Sample Scene**. The imported sample owns
  tabs, windows, menus and local storage.

Both commands save a new scene under `Assets/VRWebViewExamples`. They do not
replace your active Build Profile's Scene List. Add the desired scene there;
make it the first enabled scene when running it as a standalone example.

When embedding the panel in an existing XR rig, keep one camera and EventSystem
appropriate for that rig. Supply standard uGUI pointer events, or use
`BrowserRayPointerEventData` and `IBrowserSpatialDragHandler` for a custom world
ray/spatial drag adapter. Do not run two competing input modules.
