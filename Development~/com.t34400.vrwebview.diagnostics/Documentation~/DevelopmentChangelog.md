# Pre-release development history

Internal development versions, retained for maintainers. Public v3 release notes
are in the consumer package CHANGELOG.md. Earlier investigations and evidence
are in CurrentStatus.md and EvidenceLedger.md in this development package.


## 0.1.0-preview.55

- Add a saved, browser-wide Page display setting with Standard, Reduce shimmer and Improve angled views modes.
- Generate mipmaps after native GPU copies in the same graphics command buffer for visible outputs only.
- Preserve the loaded page and its logical size when output quality changes; reuse the same texture between filtered modes.
- Expose optional display-quality capability and controller API without changing project-wide graphics settings.
- Keep Standard as the legacy/default mode; Quest quality, softness and performance comparisons are pending.

## 0.1.0-preview.54

- Dock the new-tab button outside the scrolling strip when tabs overflow; return it inline when they fit.
- Display WebView-provided favicons in tabs, saved-page lists and new-tab bookmark shortcuts.
- Cache bounded page/origin icons independently from browser state and GPU presentation; share reference-counted Unity textures.
- Add explicit Edit and Delete controls to new-tab bookmarks.
- Preserve the tested IME compatibility patch and Android window/focus behaviour.

## 0.1.0-preview.53

- License the project and optional browser sample under Apache-2.0 from this preview.
- Preserve copyright, earlier MIT grants and the existing terms of bundled components and GameActivity compatibility helpers.
- Keep runtime, sample behaviour and native/IME binary payloads unchanged; the pending Quest UI check still targets UI revision 51.

## 0.1.0-preview.52

- Add English quick starts for standalone web panels and the optional rich browser.
- Separate historical probe components, Editor commands, research notes and probe AAR into a local development package.
- Add a consumer WebView scene command and keep sample scene creation independent from probes.
- Add deterministic UPM tarballs and consumer/development boundary checks.
- Bundle third-party notices and prepare release/media/license decisions.
- Keep the preview.51 browser UI and existing Android/IME binary payloads; fresh-project and device validation is pending.

## 0.1.0-preview.51

- Keep navigation buttons at fixed usable widths when the address is long; clip and scroll field text.
- Replace the full-page library overlay with anchored browser, link and bookmark menus.
- Show bookmark title/domain lists with scrolling and explicit edit/delete actions; keep new-tab shortcuts separate.
- Overlay the find bar without resizing the WebView viewport.
- Use yaw-only placement by default, with a configurable fixed tilt (initially 5 degrees) and consistent restore/recall.
- Retain the device-tested input, rendering and browser feature APIs. Audio investigation is deferred at the user's request.
- Cloud policy and package checks are available; Unity/Quest verification of this UI revision is pending.

## 0.1.0-preview.50

- Add address/search resolution, bookmarks, visit history and new-tab library to the rich sample.
- Restore bounded windows, selected tabs, text size and placement; initialize hidden restored tabs lazily.
- Reopen closed tabs, recall windows and clear saved data.
- Add optional content APIs for JSON-valued JavaScript execution, text zoom, revisioned find and link context actions.
- Preserve the Quest-confirmed preview.49 GameActivity IME compatibility patch.
- New features await Unity Editor and Quest validation.


