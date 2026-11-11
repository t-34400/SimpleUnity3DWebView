# Clean consumer installation check

The maintainer has confirmed fresh-project installation of both the core package
and the optional Browser sample. The steps below document that check; resolution
of the final immutable Git tag remains a separate publication check.

Use one **new Unity 6000.4.5f1 project**, with Android Build Support installed.
Check it first without samples, then import the browser. The existing development
project remains available for filming. This check validates distribution; it
is not a request to rerun all historical diagnostic scenes or batchmode tests.

## Make the candidate archive

After applying the release-preparation patch, run from the existing source tree:

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
python3 Tools/build_upm.py --output /mnt/c/Users/tmiyoshi/Downloads/com.t34400.vrwebview-3.0.0-preview.1.tgz
```

## Core only

1. In the new project, open Package Manager, **Install package from tarball**,
   and select the generated `.tgz`. Confirm version `3.0.0-preview.1`.
2. Follow [Quest setup](../Packages/com.t34400.vrwebview/Documentation~/GettingStarted.md):
   Meta XR Core SDK 207.0.1, Android OpenXR/Meta Quest support, ARM64,
   GameActivity, Vulkan, New Input System, and hand input features if using hands.
3. Enable the Quest keyboard overlay as described in that guide. Do **not**
   import Browser UI and Model yet. The diagnostics package is not installed.
4. Select **VRWebView > Create WebView Scene**. Set Initial Url to a known HTTPS
   page with an editable field (for example `https://www.wikipedia.org/`).
5. Put that generated scene first in the active Android Build Profile's Scene
   List. Build And Run. Check page display, pointing/scrolling and page-field
   Japanese input/conversion/confirmation. Capture any actual error.

The Editor does not render the web page. A missing Editor page preview is expected.

## Core + browser

1. In Package Manager, import **Browser UI and Model** once.
2. Select **VRWebView > Compatibility > GameActivity IME > Enable Patch**.
   This is a new project, so the old project's opt-in preference is not inherited.
3. Select **VRWebView > Create Rich Browser Sample Scene**, put it first in the
   active Android Build Profile and Build And Run.
4. Check existing URL replacement and appended text in both the Unity address
   field and keyboard; then check a page text field. Briefly add a tab, move a
   window, and save a bookmark. Restart once to check saved state.

Report Core/browser pass or the first concrete blocker. Keep the Console/build
error and actual steps for a failure. No new log bundle is required if both pass.
Tarball installation checks package contents; resolution of the final immutable
Git tag is a later publication check once the tag exists.
