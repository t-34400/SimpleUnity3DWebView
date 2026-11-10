# Changelog

## 3.0.0-preview.1 — Unreleased

A new architecture for Unity on Meta Quest, replacing SimpleUnity3DWebView v2.
This is a preview with breaking API and scene changes; see the
[v2 migration guide](Documentation~/Migration-v3.md).

- Install the core as a Git UPM package. Import the optional Browser UI and Model
  sample only when tabs, windows and browser controls are needed.
- Render Android WebView panels through a GPU texture, with native page text
  input and inline video. Vulkan is the Quest validation baseline.
- Expose navigation, trusted HTML loading, JSON-valued JavaScript evaluation,
  page state, find, text size, link actions and page icons through core APIs.
- Add a version-checked, opt-in GameActivity IME build patch for Unity text fields.
- Provide tabs with reorder/detach/join, movable/resizable windows, address/search,
  bookmarks, history, favicons, page tools and bounded session restoration.
- Transfer only the selected page of each visible browser window to a GPU output.
- Offer saved page-display settings to trade sharpness for reduced text shimmer.
  Standard remains the default.
- Separate maintainer probes and research from the public package.
- License VRWebView under Apache-2.0, preserving upstream notices and earlier MIT
  grants, including the existing GameActivity compatibility helper exception.

Tested during development on Quest 3 with Unity 6000.4.5f1, Meta XR Core SDK
207.0.1 and OpenXR 1.16.1. Page IME, Unity address-field synchronization, browser
controls and Standard/Reduce shimmer have device evidence. Fresh-project
installation checks are still pending. Filtered rendering can soften text;
GPU cost and the angled-view option have not been characterized on device.

Not implemented: native fullscreen video, file upload/download, site permission
UI, independent private profiles or Editor web rendering. Other headsets and
Unity/provider versions need validation. No public v3 tag has been created yet.
