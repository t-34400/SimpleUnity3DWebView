# VRWebView 3 release preparation

Candidate: **3.0.0-preview.1**, planned source tag **v3.0.0-preview.1**.
No v3 tag, GitHub rename or Release has been confirmed. The maintainer has
uploaded the reviewed demo: <https://youtu.be/1guUxsETxzc>.

The maintainer confirmed fresh-project installation of the core package and
optional Browser sample, then reviewed the locally rendered video with narration
and the selected Advertime music. YouTube playback after HD processing and the
final tagged Git UPM URL remain publication checks.

The current repository is `t-34400/SimpleUnity3DWebView`; the agreed public name
is **t-34400/VRWebView**, retaining existing history, issues and tags. Keep
`com.t34400.vrwebview` and `T34400.VRWebView`. Keep the local Windows project
folder name during validation: the opt-in IME preference is keyed by project path.

## Deliverables

- Primary: Git UPM package in `Packages/com.t34400.vrwebview`.
- Secondary: deterministic consumer `.tgz` and its SHA-256 inventory.
- Optional: editable Browser UI and Model sample, imported via Package Manager.
- Development probes, research and recording assets stay in
  `Development~/com.t34400.vrwebview.diagnostics`, outside the consumer tarball.
- Demo video: YouTube link, with an actual Quest capture as README hero.
  Do not add raw MP4 recordings to Git history.

The package changelog is the public release-history source. Internal development
preview notes remain in the development package's `DevelopmentChangelog.md`.
Historical v1/v2 tags and their MIT grants are unchanged. Current project code is
Apache-2.0; the existing GameActivity helper/tools and upstream components keep
their own terms. Preserve LICENSE, NOTICE and ThirdPartyNotices.md.

## Prepare and check

```bash
python3 Tools/check_distribution.py
python3 Tools/build_upm.py --output /mnt/c/Users/tmiyoshi/Downloads/com.t34400.vrwebview-3.0.0-preview.1.tgz
```

The archive excludes Assets, ProjectSettings, local caches and diagnostics. Its
manifest records every file's hash. Rebuilding unchanged input gives identical
bytes. Run the [clean consumer check](ConsumerCheck.md) before recording; then
use [Recording.md](Recording.md). No repeated historical IME probe matrix is
required for this documentation/repackaging change.

Quest 3 development evidence covers page IME, Unity address-field synchronization,
inline video, browser controls and Standard/Reduce shimmer. The maintainer
reported reduced shimmer with some softness; keep Standard default. GPU cost
and the angled-view option have not been characterized. Fresh consumer
installation has been confirmed by the maintainer. The shipped demo uses an
actual Browser sample recording; a separate core-only recording is not required.
Audio-output comparison remains deferred; do not make it a new release gate.

## Publication order

1. Assistant prepares candidate source/docs, clean-install archive, recording
   page/shot list and proxy tooling.
2. Maintainer applies and checks Core only, then Core + Browser in a new project.
   Assistant fixes any install blockers; settle the version used for recording.
3. Maintainer records originals, keeps them locally, shares proxies + manifest.
4. Assistant proposes second-based cuts/captions and a proxy rough cut.
5. Maintainer reviews; assistant supplies the original-resolution rendering
   script, subtitle file and hero/thumbnail extraction commands.
6. Maintainer renders originals locally and checks final media.
7. Maintainer renames the existing repository to VRWebView, uploads the reviewed
   video as unlisted and shares its actual URL.
8. Assistant inserts reviewed captures/video and changes current repository URLs
   to the actual renamed repository, without changing historical source links.
9. Maintainer pushes that final source, tags it `v3.0.0-preview.1`, checks that the
   exact tagged UPM URL resolves, and publishes the prerelease/video.
10. Assistant verifies live links, versions and release assets.

Never use an ai-patch-exchange job commit as a source repository tag.
The tag must point to the final applied source commit. Release asset hashes must
be regenerated if the package changes before tagging.

## Public copy and media slots

About:

> WebView panels for Unity on Meta Quest, with native text input and an optional multi-window browser sample.

Topics: `unity`, `webview`, `meta-quest`, `openxr`, `android`, `vr`, `upm`.

Prepared final Git URL (activate only once rename + tag exist):

```text
https://github.com/t-34400/VRWebView.git?path=/Packages/com.t34400.vrwebview#v3.0.0-preview.1
```

After rename, update the current README Git URL and package.json's documentation,
changelog and license URLs. The working URLs are intentionally retained until
then. At publication replace the README's candidate/pending wording with the
actual installation result. Do not publish a dangling future tag as a usable URL.

Hero: one clear, stable Quest capture, actual interface, no personal pages.
Extract it from an original take. Store the approved image as
`Documentation/Media/hero.webp`; add a linked video thumbnail only after its URL
exists. Social preview is a separate 1280×640 composition with product name and
real capture. A wordmark is optional, not a substitute for the actual interface.
No fabricated placeholder image is needed.

Keep badges to about three: Apache-2.0, Quest 3 tested, and latest release
(including prereleases) once v3 is published. Add a CI badge only after a real
source-repository workflow exists; do not present headless checks as device tests.

Use [ReleaseNotes-v3.md](ReleaseNotes-v3.md) for the GitHub prerelease body, then
fill in actual install results and SHA-256. An optional demo APK is not a blocker.
