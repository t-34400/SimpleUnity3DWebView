# Browser UI and Model

An optional browser application built on VRWebView. It provides tabs, movable
windows, URL/search input, bookmarks, history, page tools and saved layouts.

## Start

1. Complete the package's Quest setup, including system keyboard support.
2. Import **Browser UI and Model** from VRWebView's Samples in Package Manager.
3. Select **VRWebView > Create Rich Browser Sample Scene**.
4. Put the created scene first in the active Android Build Profile's Scene List
   and **Build And Run**.

The scene uses the real Android backend. It does not provide an Editor browser
or a fake default page. Meta Interaction SDK is not required by this sample.

## Use

- Enter a URL or search terms in the address bar. Use + for a tab, □+ for a window.
- Drag tabs to reorder, detach or join windows. Grab empty title-bar space to
  move a window; drag a highlighted corner to resize it.
- The standard placement rotates horizontally with a fixed tilt. Change the tilt
  in Settings. Recall windows brings them back in front of the viewer.
- Use ☆/★ to save or edit a bookmark. The menu opens bookmarks, history, text size
  and find. Long-press a page link for open/copy actions.

Bookmarks, history and layouts stay in the application's persistent data directory.
Restoration reloads pages; it does not restore forms, DOM or per-page back stacks.
Clear history and Forget session are in Settings. These do not clear site cookies.

## Update an imported copy

Samples are copied into Assets and are editable. Do not leave two versions of
the same sample assembly in Assets. Back up your changes before replacing it.

In this repository's development project, close Unity and run:

```bash
python3 Tools/update_browser_sample.py --backup-dir <folder-outside-the-project> --apply
```

This backs up the old copy and preserves existing metadata GUIDs and scene
references. Merge your customizations from the backup. For a Git-package consumer
without the repository's Tools folder, back up the imported sample and update it
from Package Manager, taking care to preserve or reassign your scene references.

The core runtime does not depend on this sample's UI or storage policy.
