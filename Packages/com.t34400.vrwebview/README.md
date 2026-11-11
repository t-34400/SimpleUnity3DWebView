# VRWebView

WebView panels for Unity on Meta Quest, with native text input and an optional
multi-window browser sample.

**3.0.0.** Developed and tested on **Quest 3 / Unity 6000.4.5f1**, with Meta
XR Core SDK 207.0.1 and OpenXR 1.16.1. Pages run in Android players; there is no
Editor browser. The maintainer has confirmed fresh-project installation of the
core package and the optional Browser sample.

## Install

In **Window > Package Manager**, select **Add package from git URL**:

```text
https://github.com/t-34400/VRWebView.git?path=/Packages/com.t34400.vrwebview
```

For repeatable builds, append a released tag or source commit using `#`.
You can also install the consumer `.tgz` via Package Manager.

Set up an Android **ARM64 / Vulkan / GameActivity** project with OpenXR and Meta
XR Core SDK. Follow [Quest setup](Documentation~/GettingStarted.md), including
the system keyboard settings.

## Use a web panel

1. Select **VRWebView > Create WebView Scene**. No sample import is required.
2. Set **Initial Url** on `BrowserController`.
3. Put the scene first in the active Android Build Profile's Scene List, then
   **Build And Run** on Quest.

For your own UI, add `BrowserView` to a `RawImage`, add `BrowserController`, and
assign its **View**. Your XR input module supplies uGUI pointer events.
[Components and API examples](Documentation~/API.md).

## Use the optional browser

1. In Package Manager, select VRWebView and import **Browser UI and Model** from
   **Samples**.
2. Select **VRWebView > Create Rich Browser Sample Scene**.
3. Add the scene to the Android Build Profile's Scene List and **Build And Run**.

The sample provides tabs, movable windows, search, bookmarks, history and saved
layouts. Its UI and storage policy are editable and separate from the core.
[Sample guide](Samples~/Browser/README.md).

Unity text fields in the tested Unity/GameActivity combination need **VRWebView >
Compatibility > GameActivity IME > Enable Patch** before building the sample.
The patch is opt-in and version-checked. [Compatibility details](Documentation~/GameActivityImeCompatibility.md).

## Scope

Inline video and native page text input are supported. Native fullscreen video,
file upload/download, site permission UI and independent private profiles are
not implemented. Other devices and dependency versions need their own validation.

[Changelog](CHANGELOG.md) · [Migrating from v2](Documentation~/Migration-v3.md) ·
[Troubleshooting](Documentation~/Troubleshooting.md)

Apache-2.0 for VRWebView code. See [LICENSE](LICENSE) and
[third-party notices](ThirdPartyNotices.md) for bundled components and exceptions.
