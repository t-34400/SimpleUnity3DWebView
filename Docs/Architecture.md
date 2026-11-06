# Architecture boundaries

- BrowserSession owns exactly one IBrowserBackend. Public session operations are owner-thread APIs; a Unity integration must provide a Unity-main-thread dispatcher. Core alone does not claim thread-safe session mutation.
- RequestLedger is thread-safe correlation for native responses. IDs are not recycled. Detach advances generation and cancels pending responses; duplicates/stale generations are ignored. Per-request timeout and production native transport remain to be implemented.
- PointerCapture owns one gesture. A second controller cannot steal it. Cancellation is explicit. BrowserPointerSource maps actual RectTransform bounds and supports a null Overlay Canvas camera. It does not implement Android delivery yet.
- AndroidImeProbe is separate from IBrowserBackend. It is a diagnostic UI, not a browser implementation disguised as a mock.
- The Android library currently uses Java rather than requiring Kotlin runtime for a small bridge. WebView/View work is on the UI thread. Future InputConnection forwarding must respect getHandler and reverse-direction notifications rather than blindly forwarding everything to the UI thread.
- GPU producer/import/fences and InputConnection proxying remain research gates. The CPU diagnostic preview is not a production fallback.
- No Meta SDK implementation is copied into the UPM Runtime. Meta Canvas events can be connected through Unity EventSystems later.

The old SimpleUnity3DWebView runtime, AAR, outdated samples/scenes, and custom Android templates were removed from the new development tree. The provided source ZIP is kept unchanged as the exchange baseline. Current Unity/Meta/OpenXR dependency versions are preserved.

No API compatibility with finalized legacy patches or the old library is promised. This is a new implementation.
