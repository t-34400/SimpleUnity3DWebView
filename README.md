# VRWebView

Android WebView panels for Unity VR. Embed a single web panel in your app, or
import the optional browser sample for tabs and movable windows.

**Preview:** tested with Quest 3 and Unity 6000.4.5f1. The latest UI and package
layout still need device and fresh-project checks. Web pages run in Android
players; the Unity Editor is used for setup.

## Install

In **Window > Package Manager**, select **Add package from git URL**:

```text
https://github.com/t-34400/SimpleUnity3DWebView.git?path=/Packages/com.t34400.vrwebview
```

For a repeatable build, pin the URL to a published release tag or source commit.
Set up an Android ARM64 / Vulkan / GameActivity project with OpenXR and Meta XR
Core SDK, then follow the short [Quest setup](Packages/com.t34400.vrwebview/Documentation~/GettingStarted.md).

## Use a web panel

1. Select **VRWebView > Create WebView Scene**. No sample import is required.
2. Set **Initial Url** on the `BrowserController` component.
3. Put the generated scene first in your active Android Build Profile's Scene
   List, then **Build And Run** on Quest.

To use your own UI, add `BrowserView` to a `RawImage`, add `BrowserController`, and
assign its **View** field. Your EventSystem/XR input module supplies pointer
events. [Embedding and API examples](Packages/com.t34400.vrwebview/Documentation~/API.md).

## Use the browser sample

1. In Package Manager, select VRWebView and import **Browser UI and Model** from
   **Samples**.
2. Select **VRWebView > Create Rich Browser Sample Scene**.
3. Add that scene to the active Android Build Profile's Scene List and **Build
   And Run**.

The sample provides tabs, windows, search, bookmarks, history and saved layouts.
Its UI and storage policy are separate from the core runtime; you can edit the
imported copy. [Sample setup and updates](Packages/com.t34400.vrwebview/Samples~/Browser/README.md).

## Text input and support

Quest text input needs the system keyboard overlay enabled. For the tested
Unity/GameActivity combination, the optional **VRWebView > Compatibility >
GameActivity IME > Enable Patch** fixes Unity text-field synchronization. It is
version-checked and disabled by default. [Setup and troubleshooting](Packages/com.t34400.vrwebview/Documentation~/GettingStarted.md).

Inline video playback is supported. Native fullscreen video, file upload/download,
site permission UI and independent private profiles are not implemented.

[Changelog](Packages/com.t34400.vrwebview/CHANGELOG.md) · [Troubleshooting](Packages/com.t34400.vrwebview/Documentation~/Troubleshooting.md)

MIT for VRWebView code. See [LICENSE](LICENSE) and [third-party notices](Packages/com.t34400.vrwebview/ThirdPartyNotices.md)
for bundled components.
