# AOSPに基づく描画・IME検証の根拠

調査対象はAOSP Android 12.0.0_r1／14.0.0_r1とChromium 128.0.6613.84。Quest搭載版そのもののソースではなく、一般的なAndroidの条件を確認するための資料である。

Android 12／14の`WindowState.canReceiveKeys(fromUserTouch)`は、可視状態・Window flags等に加え、タッチ由来か、Displayが最前面か、trustedかを検査する。WindowManagerが認識したタッチにはDisplayを最前面へ移す経路がある。アプリ内で直接`WebView.dispatchTouchEvent()`を呼ぶだけでは、このシステム側の処理を代替しない。

Android 14のVirtual Displayの`OWN_FOCUS`はtrusted displayを要求し、trusted化にもシステム側の権限検査がある。フラグの数値追加やViewのフォーカス値の偽装で解決する前提にしない。

IMEにはWindow focus・Window token・client displayId・top focused display等の条件がある。一方、Virtual DisplayのIMEをホスト側へ表示するfallback経路もあるため、画像にIMEが含まれないことと、入力接続自体の失敗は区別する。

`Surface.lockHardwareCanvas()`は、AOSP内部でRenderNodeとHardwareRendererを使ってSurfaceへ描く。Chromiumの`AwContents::OnDraw()`は、hardware CanvasでWindowへattachされている等の条件を満たすとGPU経路へ進む。Bitmapへのソフトウェア描画と同一ではない。

このため、同じWindowにattachしたWebViewを維持し、hardware Canvas → SurfaceTextureという経路を試す。ただし、この分岐だけでは、動画の別レイヤー合成も取り込めるとは証明できない。WebViewのvisual-state callbackもvideo tagの状態を含まない。取得画像内の動画フレームを直接観測する。

GPU取り込みとIMEの両方が成立した構成だけを、Unity／OpenXR統合へ進める。Virtual Displayで特権的操作だけが成功した結果は、通常配布アプリでの成功に数えない。動画未対応のBitmap経路へ要件を下げない。

一次資料：

- [Android 14 WindowState.canReceiveKeys](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/services/core/java/com/android/server/wm/WindowState.java#L2961)
- [Android 12の同条件](https://github.com/aosp-mirror/platform_frameworks_base/blob/cebf5c06997b64f4e47a1611edb5f97044509d76/services/core/java/com/android/server/wm/WindowState.java#L3106)
- [WindowManagerによるフォーカス移動](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/services/core/java/com/android/server/wm/WindowState.java#L5973)
- [VirtualDisplayAdapterのOWN_FOCUS条件](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/services/core/java/com/android/server/display/VirtualDisplayAdapter.java#L536)
- [IME clientのWindow/Display検査](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/services/core/java/com/android/server/wm/WindowManagerService.java#L8011)
- [Surface.lockHardwareCanvasと内部実装](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/core/java/android/view/Surface.java#L518)
- [ChromiumのHW/SW描画分岐](https://github.com/chromium/chromium/blob/128.0.6613.84/android_webview/browser/aw_contents.cc#L1058)
- [Chromiumのoverlay利用条件](https://github.com/chromium/chromium/blob/128.0.6613.84/android_webview/browser/gfx/hardware_renderer.cc#L719)
- [WebView visual-state callbackのvideo除外](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/core/java/android/webkit/WebView.java#L1043)
- [HardwareRendererのconsumer要件](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/graphics/java/android/graphics/HardwareRenderer.java#L55)
- [SurfaceTextureのGL thread/UV契約](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/graphics/java/android/graphics/SurfaceTexture.java#L32)
