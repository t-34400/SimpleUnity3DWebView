# Recording the VRWebView demo

Keep originals locally. Share lightweight proxies with `manifest.json`; editing
will use seconds from each original's first displayed video frame. Final cuts
will be rendered on your machine from the originals, not from the proxies.

## Before filming

Complete [the clean consumer check](ConsumerCheck.md), then use the existing
source/development project for the controlled recording page. Use the accepted
Browser sample for browser shots. Keep personal URLs, accounts and notifications
out of the capture. Use an empty/demo browser state if necessary; back up your
current data before clearing it. Do not clear real bookmarks just for filming.

Use Quest recording at its original aspect ratio and highest practical quality.
1080p/30 fps is useful when available; do not stretch a square capture to 16:9.
First record 5 seconds of keyboard interaction and check that the system keyboard
is present in the actual recording. Its visibility in the HMD alone is not enough.
No narration or music is required; the first edit uses English captions.

## Controlled core page

In the repository development project select **VRWebView > Development > Create
Recording Scene**. Put the generated scene first in the Android Build Profile,
then Build And Run. This uses the real Core controller and native WebView input,
with a small maintainer-only loader. It loads an offline, self-contained page
and the existing original synthetic H.264 clip through `LoadHtmlAsync`.
The page is not a public website and is not part of the consumer package.

This new scene still needs its first Unity/Quest check. If filming exposes a
scene-specific failure, report it before recording a long take; the existing
consumer scene/API and Browser sample are unchanged by this addition.

The video starts muted and requires a tap. Its synthetic content is original
project material, not a third-party film. Audio comparison is not a filming gate.
The 8-second loop is intentional. There is no recording-page network dependency.

## Takes (record 10–30 seconds each, with 2–3 seconds still at both ends)

| Take | Action | Final segment / English caption |
| --- | --- | --- |
| 01-core | Hold a clear view of the panel, point, then scroll a little | 0–8 s: Embed a web panel in your Unity scene |
| 02-video | Tap play, let motion show, then scroll while it plays | 8–18 s: Browse and play inline video |
| 03-ime | Scroll to Your message, enter `こんにちは`, convert/confirm, close keyboard | 18–30 s: Native text input — Japanese IME shown |
| 04-tabs | In Browser, add/reorder a few tabs; detach one into a window | 30–42 s: Import the optional browser sample |
| 05-windows | Move/resize the detached window; optionally join its tab back | 42–50 s: Arrange your browser in space |
| 06-library | Save/open a bookmark; hold a clean view with two readable windows | 50–60 s: Tabs, windows, bookmarks and more |

For browser content use a non-personal HTTPS page such as Wikipedia. The controlled
page is a Core HTML example, not a new browser protocol or hosted start page.
Do not spend filming time demonstrating every menu, setting or internal probe.
Repeat a failed action as another take instead of restarting the whole recording.
Keep the clearest two-window view still for 2–3 seconds: this can supply the hero.

## Make review proxies in WSL

FFmpeg and ffprobe are needed only for proxy creation. If absent, install the WSL
package with `sudo apt install ffmpeg`. From the source directory:

```bash
python3 Tools/make_video_proxies.py \
  "/mnt/c/Users/tmiyoshi/Downloads/VRWebView-Originals/01-core.mp4" \
  "/mnt/c/Users/tmiyoshi/Downloads/VRWebView-Originals/02-video.mp4" \
  "/mnt/c/Users/tmiyoshi/Downloads/VRWebView-Originals/03-ime.mp4"
```

Pass all desired original takes as arguments (the three above are examples).
The script creates a new dated folder in `C:\Users\tmiyoshi\Downloads`, with
silent, aspect-preserving H.264 proxies up to 960×720 / 15 fps and **30 MB each**,
plus `manifest.json`. An existing output directory is never overwritten; optional
`--output-dir` selects a new one. Originals are only read and SHA-256 hashed.

Each take is kept whole: no trimming, speed changes or concatenation. The maximum
input length is 120 seconds per take. Duration is checked within one proxy frame;
use seconds, never proxy frame numbers, for edit decisions. The manifest records
source/proxy filenames, original hash, video frame rate/time base, sizes and
zero source offset. Camera/container GPS and unrelated tags are not exported.
Variable-frame-rate originals remain originals; proxy frame rate is for review.

Upload the `.proxy.mp4` files individually and `manifest.json` here. Keep each
file below the attachment limit; ZIP usually does not shrink MP4 substantially.
Keep originals unchanged and with their recorded filenames until final rendering.
Send any take preference with its filename and approximate seconds.

## Editing handoff

The assistant will propose about 60 seconds of cuts/captions and a rough cut from
the proxies. After review, it will supply an edit-decision file, English subtitles
and an FFmpeg script that finds the matching originals by filename/hash and
renders the approved cuts locally. Hero/thumbnail extraction uses those same
originals. That final script is made from actual footage and the approved cut,
not assumed frame numbers. Share a small final review copy and selected stills;
the original recordings do not need to be uploaded.
