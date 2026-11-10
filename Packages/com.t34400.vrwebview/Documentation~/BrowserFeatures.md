# Browser features and ownership

The rich sample is an application, not a required runtime service. Core exposes
optional content operations; Android owns WebView calls. Core knows neither UI,
bookmarks nor files. The sample owns address interpretation, library, session,
window placement, context actions and toolbars. Existing IME ownership and visible
presentation leases remain authoritative.

## This increment

- Address or search (configurable HTTPS search endpoint; DuckDuckGo initially).
- Local bookmarks/history and a useful new-tab library; remove/clear controls.
- Versioned local session: window order, active tabs, HTTP(S) addresses, text zoom,
  parent-local positions, rotations and dimensions. Save with debounce and on
  pause/quit; retain a last-good file. Reopen recently closed tabs. Restore creates
  new documents: POST bodies, DOM, navigation stacks and form input are not saved.
- Text zoom, asynchronous find with result counts, developer JavaScript execution
  returning WebView's JSON encoded value. Cancellation abandons the result, not
  JavaScript side effects. No web-to-native JavascriptInterface is installed.
- Long-press link/image context actions (open HTTP(S) target in new tab, copy link).
- Bring all windows back in front of the viewer. Placement is relative to the
  browser root, not a persistent spatial anchor across Quest recentering.

The next media increment needs a separate decision: native custom video views can
contain SurfaceViews whose pixels do not participate in hardware Canvas drawing.
Do not claim native fullscreen support by accepting onShowCustomView until that
path is proven. Existing inline video playback remains supported. File pickers,
downloads, site permissions, independent private profiles and popup/dialog policy
also require their own platform implementation; they are not enabled implicitly.

## Data and lifecycle

Schema 1 stores at most 8 windows / 32 tabs per window, 200 bookmarks, 200 visits,
and 10 recently closed tabs. Store only validated HTTP(S) URLs without user-info;
URLs can still contain private query parameters. Data stays in the application's
persistent data directory, never in PlayerPrefs, logs or exported diagnostics.
Users can clear history and forget saved sessions independently of cookies.
Cookies and site storage remain WebView-managed and are not exported in this file.
Invalid/unknown versions are preserved and ignored; never overwrite an unknown
schema merely because loading it failed. Writes use a same-directory temporary
file and replace with a backup. No scene assets are rewritten at runtime.

History observes successful, non-loading page states and deduplicates successive
same-address notifications per tab; URL drafts and requested navigations are not
visits. Sample restoration validates and bounds every record before opening any
page. A malformed record must not prevent other valid windows from restoring.

## Content API

IBrowserContentBackend is optional and separate from mandatory IBrowserBackend.
Script, zoom and find are bounded operations on a ready backend. Find returns a
query revision with asynchronous results; stale replies are ignored by the UI.
Context notifications contain validated target data only on the owner dispatcher.
A tab switch closes transient tools/context menus and never changes input focus
by itself. Native long-press consumes the action before Android's floating menu.

## Verification boundary

Cloud checks exercise address policies, history/deduplication, session limits,
recovery, script quoting, API lifetime and native contracts. Unity Editor import
and type compilation, Quest ray/long-press behavior, Japanese IME and persistence
across actual Player restarts remain device-side checks. No new Editor startup
loop or diagnostic matrix is needed: use the updated rich browser sample once.

Restored hidden tabs are lazy: keep their address/title/zoom in the model, but
initialize the WebView only when selected. Thus restoration does not instantiate
hundreds of native pages at launch. Already initialized hidden pages retain their
DOM, with the existing presentation policy withdrawing their GPU output.

Imported sample updates use `Tools/update_browser_sample.py` as an explicit user
command with a complete external backup. Existing metadata GUIDs remain intact,
so the existing scene stays usable. The command is not executed by patch apply,
Editor startup or package import. Application-only files are retained, while
modified package sample files can be merged from the backup.
