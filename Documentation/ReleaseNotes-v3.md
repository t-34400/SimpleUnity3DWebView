# VRWebView 3.0.0-preview.1

Draft prerelease notes — do not publish until installation and final URLs are checked.

VRWebView is a rewrite of SimpleUnity3DWebView for Unity on Meta Quest.
Embed a single web panel, or import the optional multi-window browser sample.

- Android WebView rendering through GPU textures, native page text input and
  inline video; Vulkan is the validated Quest rendering path.
- Core APIs for navigation, trusted HTML, JavaScript, page state, text zoom,
  find, link actions and favicons.
- Optional browser with tabs, detachable/joinable windows, search, bookmarks,
  history, menus and saved layouts. Only visible pages have GPU outputs.
- Optional page-display filtering for reduced text shimmer, with Standard default.
- Guarded GameActivity IME compatibility for Unity address fields.
- Git UPM delivery and Apache-2.0 licensing, with preserved upstream/MIT notices.

This is a breaking v3 preview. Existing v2 scenes/API calls are not converted
in place; follow the package's Migration-v3 guide. Existing v1/v2 releases retain
their original license grants.

Development baseline: Quest 3, Unity 6000.4.5f1, Meta XR Core SDK 207.0.1,
OpenXR 1.16.1, Android ARM64/GameActivity/Vulkan. Other dependency versions and
headsets require validation. The maintainer confirmed fresh-project installation
of the core package and optional Browser sample. The final tagged Git UPM URL
remains a publication check.

[Watch the Browser sample demo](https://youtu.be/1guUxsETxzc).

Limitations: no Editor web rendering, native fullscreen video, upload/download,
site permission UI or independent private profiles. Filtered rendering can soften
text and adds GPU work. No performance figure is claimed.

Install using the final tagged Git UPM URL in README, or the attached consumer
`.tgz` via Package Manager. Attach its generated hash inventory; the maintainer
must insert the final SHA-256 here after the package bytes are settled.
