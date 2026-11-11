# VRWebView 3 release preparation

Release version: **3.0.0**. Source tag: **v3.0.0**. Publish a regular GitHub
release with the prerelease option disabled.

The renamed repository is **t-34400/VRWebView**; the old GitHub URL redirects to
it. Keep `com.t34400.vrwebview` and `T34400.VRWebView`. Keep the local Windows
project folder name: the opt-in IME preference is keyed by project path.

The maintainer confirmed fresh-project installation of the core package and
optional Browser sample, and reviewed the locally rendered demo with narration
and Advertime music. The uploaded demo is <https://youtu.be/1guUxsETxzc>.
The final source tag, tagged Git installation and GitHub Release have not yet
been confirmed. Check YouTube playback after HD processing before making it public.

## Deliverables

- Primary: Git UPM package in `Packages/com.t34400.vrwebview`.
- Secondary: `com.t34400.vrwebview-3.0.0.tgz` and its SHA-256 inventory.
- Optional: editable Browser UI and Model sample, imported via Package Manager.
- Maintainer probes and research remain in
  `Development~/com.t34400.vrwebview.diagnostics`, outside the consumer archive.
- The README links to the actual Quest demo. Do not add raw recordings or music
  files to Git history.

The package changelog is the public release-history source. Historical development
notes remain in the development package's `DevelopmentChangelog.md`. Historical
v1/v2 tags and their MIT grants are unchanged. Current project code is Apache-2.0;
the existing GameActivity helper/tools and upstream components keep their own
terms. Preserve LICENSE, NOTICE and ThirdPartyNotices.md.

## Check and build the distribution

After applying the release patch, run from the source project:

```bash
python3 Tools/check_distribution.py
python3 Tools/build_upm.py --output /mnt/c/Users/tmiyoshi/Downloads/com.t34400.vrwebview-3.0.0.tgz
```

The archive excludes Assets, ProjectSettings, local caches and diagnostics. Its
`.manifest.json` records its SHA-256 and every file hash. Rebuilding unchanged
input gives identical bytes. Publish the archive together with the inventory
generated from those same local files.

The core and Browser installation check has already been reported successful.
This metadata/documentation change does not require rerunning the historical
IME probe matrix or recording the demo again. Quest 3 evidence covers native
page input, Unity address-field synchronization, inline video, browser controls
and Standard/Reduce shimmer. Keep Standard as the default. GPU cost and the
angled-view option have not been characterized. Audio-output comparison remains
deferred and is not a new release gate.

## Remaining publication steps

1. Maintainer applies the final release patch to the source project and checks
   the resulting source diff. Preserve unrelated local Unity changes.
2. Build the consumer archive and inventory with the commands above. Push the
   final applied source commit to `t-34400/VRWebView`.
3. Tag that source commit `v3.0.0` and push the tag. Never tag an
   ai-patch-exchange job commit as a source release.
4. Confirm the exact tagged Git UPM URL resolves in Package Manager. This checks
   the public source/tag packaging; it does not require repeating the completed
   device tests.
5. Create the regular GitHub Release from `v3.0.0`. Copy
   [ReleaseNotes-v3.md](ReleaseNotes-v3.md) as its body and attach the locally
   generated `.tgz` and `.manifest.json`. Leave **Set as a pre-release** unchecked.
6. Make the reviewed YouTube demo public and update its description to the
   renamed GitHub URL. Verify the live source, release, asset and video links.

The final Git installation URL, usable after tag publication, is:

```text
https://github.com/t-34400/VRWebView.git?path=/Packages/com.t34400.vrwebview#v3.0.0
```

Do not move an already published tag to a different commit. Regenerate release
assets and their inventory if package files change before tagging.

## Public copy and media

About:

> WebView panels for Unity on Meta Quest, with native text input and an optional multi-window browser sample.

Topics: `unity`, `webview`, `meta-quest`, `openxr`, `android`, `vr`, `upm`.

The README's linked YouTube thumbnail is the demo entry point. An additional hero
image should be a clear actual Quest capture. Social preview is a separate
1280x640 composition with the product name and real capture. Do not fabricate
interface images.

Keep badges minimal: Apache-2.0, Quest 3 tested and latest release once the tag
and Release exist. Add a CI badge only after a real source-repository workflow
exists. An optional demo APK is not a release requirement.
