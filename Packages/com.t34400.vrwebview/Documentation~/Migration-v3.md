# Migrating from SimpleUnity3DWebView v2

VRWebView 3 is a rewrite. There is no automatic conversion of v2 components,
prefabs, scenes or Java bridge calls. Keep a backup or a separate v2 branch
before migrating. Existing v1/v2 tags and their MIT license grants remain intact.

## Replace the integration

1. Remove the old `Assets/SimpleUnity3DWebView` package from the migrated project,
   including its WebView scripts and `webviewtexture-release.aar`. Check for
   copies elsewhere in Assets. Do not delete unrelated plugins.
2. Review custom Android manifests and Gradle templates. Remove v2-specific
   additions only when no other dependency needs them. Keep app permissions,
   launcher settings and other SDK declarations that your application uses.
3. Install `com.t34400.vrwebview` and complete [Quest setup](GettingStarted.md).
   The tested baseline is Unity 6000.4.5f1 / OpenXR / GameActivity / Vulkan.
4. Create a fresh web-panel scene, or import the optional Browser sample and
   create its scene. Rebuild your application integration using the components
   and contracts below. Reassign references in your own scenes and prefabs.

| v2 integration | v3 starting point |
| --- | --- |
| `WebViewManager`, Java bridge and texture updater | `BrowserController` with a `BrowserSession` and Android backend |
| Custom texture display and pointer forwarding | `BrowserView` on a `RawImage`, receiving uGUI pointer/scroll events |
| Navigation/Java calls | Session APIs and page-state events; see [API](API.md) |
| Sample prefab/address bar | Optional imported Browser UI and Model sample |
| Direct OVR-based input integration | Supplied OpenXR hand/controller adapter or your own XR input module |

Do not mix the old and new native libraries or expect old serialized scene
components to rebind to the new types. The UPM identity is
`com.t34400.vrwebview`; the namespace is `T34400.VRWebView`.

## Input and data

Web-page fields use native Android WebView input. The Browser sample's address
bar is a Unity text field and uses a separate, guarded GameActivity compatibility
patch on the tested dependency versions. Enable it explicitly as described in
[GameActivity compatibility](GameActivityImeCompatibility.md).

Browser sample state uses its own bounded JSON storage. Old sample state is not
migrated. Site cookies are managed by Android WebView; the sample does not offer
independent private profiles or migration of a v2 browser session.

Keep the local project directory name during initial migration: the optional
IME compatibility preference is stored per project path. A later folder rename
may require enabling that preference again.

Current VRWebView code uses Apache-2.0. Bundled components and the existing MIT
GameActivity helper retain their own terms; see [notices](../ThirdPartyNotices.md).
