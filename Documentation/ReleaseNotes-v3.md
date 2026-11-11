# VRWebView 3.0.0

VRWebView is a rewrite of SimpleUnity3DWebView for Unity on Meta Quest.
Embed a single web panel, or import the optional multi-window browser sample.

- Android WebView rendering through GPU textures, native page text input and
  inline video; Vulkan is the validated Quest rendering path.
- APIs for navigation, trusted HTML, JavaScript, page state, text zoom, find,
  link actions and favicons.
- Optional browser with tabs, detachable/joinable windows, search, bookmarks,
  history, menus and saved layouts. Only visible pages have GPU outputs.
- Optional page-display filtering for reduced text shimmer, with Standard default.
- Guarded GameActivity IME compatibility for Unity address fields.
- Git UPM delivery and Apache-2.0 licensing, with preserved upstream/MIT notices.

This is a breaking major update. Existing v2 scenes and API calls are not
converted in place. Follow the [migration guide](https://github.com/t-34400/VRWebView/blob/v3.0.0/Packages/com.t34400.vrwebview/Documentation~/Migration-v3.md).
Existing v1/v2 releases retain their original license grants.

[Watch the Browser sample demo](https://youtu.be/1guUxsETxzc).

## Install

In Unity Package Manager, select **Add package from git URL**:

```text
https://github.com/t-34400/VRWebView.git?path=/Packages/com.t34400.vrwebview#v3.0.0
```

Follow the [Quest setup guide](https://github.com/t-34400/VRWebView/blob/v3.0.0/Packages/com.t34400.vrwebview/Documentation~/GettingStarted.md).
Import **Browser UI and Model** from the package's Samples only if you want the
optional browser interface.

Alternatively, install `com.t34400.vrwebview-3.0.0.tgz` from the release assets.
Its accompanying `.manifest.json` records the archive SHA-256 and each file hash.

## Tested environment and scope

Quest 3, Unity 6000.4.5f1, Meta XR Core SDK 207.0.1, OpenXR 1.16.1,
Android ARM64/GameActivity/Vulkan. The maintainer confirmed fresh-project
installation of the core package and optional Browser sample. Other dependency
versions and headsets require their own validation.

Native fullscreen video, upload/download, site permission UI, independent
private profiles and Editor web rendering are not implemented. Filtered rendering
can soften text and adds GPU work; no performance figure is claimed.
