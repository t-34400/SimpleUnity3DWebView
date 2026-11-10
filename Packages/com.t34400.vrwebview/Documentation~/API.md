# Embedding and API

## Components

| Component | Role |
| --- | --- |
| `BrowserController` | Creates one Android backend/session and binds a view. |
| `BrowserView` | Displays the supplied GPU texture through a RawImage. |
| `BrowserPointerSource` | Receives uGUI pointer, hover and scroll events; added by BrowserView. |
| `BrowserDispatcher` | Owns the Unity thread queue; added by BrowserController. |

Configure the controller's View and Initial Url in the Inspector before entering
Play or building. On Android it initializes on enable and closes on disable.
Set Initial Url to empty if your code will perform the first navigation.

## Commands

Call operations on the Unity owner thread after initialization. For example,
with a serialized controller reference and an empty Initial Url:

```csharp
using UnityEngine;
using T34400.VRWebView;

public sealed class WebPanelExample : MonoBehaviour
{
    [SerializeField] private BrowserController browser;

    private async void Start()
    {
        var session = browser.Session;
        if (session == null) return;
        try
        {
            await session.InitializeAsync();
            await session.NavigateAsync("https://example.com/");
        }
        catch (System.Exception error) { Debug.LogException(error, this); }
    }
}
```

Handle operation exceptions in your application. `BrowserSession.State` reports
host readiness; a navigation task acknowledges submission, not document load.
Observe `PageState` / `PageStateChanged` for the URL, title, loading and error state.

After the desired document has loaded:

```csharp
string json = await browser.Session.EvaluateJavaScriptAsync("document.title");
await browser.Session.SetTextZoomAsync(120);
await browser.Session.FindAsync("example", revision: 1);
await browser.Session.FindNextAsync(true);
await browser.Session.ClearFindAsync();
await browser.Session.ExecuteAsync(BrowserPageCommand.Reload);
```

JavaScript results use WebView's JSON encoding. Evaluation does not await
Promises or turn JavaScript exceptions into C# exceptions. Cancellation abandons
a result; it does not undo already submitted script effects.

Find results arrive through `FindResultChanged` with the caller's revision.
Discard results from superseded queries. Text zoom is 50–200 percent; it changes
web text, not the physical size of the Unity panel.

## Ownership

The controller exposes `Session` and `Backend` for advanced integration. A
`BrowserSession` owns its backend; do not dispose or share that backend elsewhere.
Use `BrowserController.EndEditing()` when your UI explicitly takes editing away
from the page. `BrowserView` borrows surfaces and does not own the page.

The pure Core contracts do not depend on Meta, Unity UI, bookmarks or filesystem
storage. The Android implementation and Unity components provide those concrete
platform/view roles. The optional browser sample is an application on top of them.

## Page icons

Backends can implement optional `IBrowserIconSource`. `AndroidBrowserBackend`
exposes its latest `PageIcon` and `PageIconChanged` on the Unity owner thread.
Each `BrowserPageIcon` includes the page address and a copied PNG payload bounded
to 64 pixels per axis and 32 KiB. Check the address when associating an icon with
a current page; late page metadata may refer to a previous navigation.

Core defines this metadata capability only. Download/cache policy, default icons
and Unity texture lifetime belong to the host UI; the Browser sample supplies
those roles. Observing icons does not allocate a page GPU presentation.

## Display quality

`BrowserController` exposes an Inspector setting and
`SetDisplayQuality(BrowserDisplayQuality)` for a single panel. The Android backend
also implements optional `IBrowserDisplayQualityBackend` for custom composition.
Call it on the Unity owner thread; it may be selected before initialization.

```csharp
browser.SetDisplayQuality(BrowserDisplayQuality.ReduceShimmer);
```

| Mode | Output sampling |
| --- | --- |
| `Standard` | Bilinear, one mip level; default |
| `ReduceShimmer` | Mipmaps and Trilinear |
| `ImproveObliqueViews` | Mipmaps, Trilinear and anisotropy 4 |

Changing between Standard and a filtered mode recreates the GPU output, retaining
the same WebView/session and pixel dimensions. Changing between filtered modes
updates sampling on the existing output. Hidden pages do not allocate outputs.
The render pump records native copies followed by mip generation on the graphics
queue before UI sampling. Retiring a generation immediately removes its mip work.
Mipmaps are generated every rendered frame while enabled; this is not a
content-change detector.

The API does not change text zoom, page layout, IME ownership or global
QualitySettings. Anisotropy respects the application's quality settings. Full mip
chains add about 33% memory to a typical 2D output texture, plus generation and
sampling work. Compare readability, softness and frame time on the target device.
The Browser sample owns preference persistence and its Settings menu.
