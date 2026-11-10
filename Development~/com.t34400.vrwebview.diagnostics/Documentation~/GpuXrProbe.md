# GPU / 同一Unity Activity / XR検証（GX2）

> **現在は整理フェーズで実機試験を停止しています。** 本書は過去の検証手順です。現在の判断は[CurrentStatus.md](CurrentStatus.md)、共通の取得仕様は[Diagnostics.md](Diagnostics.md)を参照してください。以下の「次の試験」は現在の実行依頼ではありません。

H1の`Window draw: off`でスクロール・入力・動画音声が動くことはQuest 3で利用者が確認済み。
GX2はその方式をUnityのVulkanまたはOpenGL ES 3テクスチャへ接続する**新しい実験**であり、H1の成功を引き継いで合格とはしない。
Unity Editorのimport/build、実機GPU共有、同一Unity WindowのIME、XRの継続動作は未検証。

## 構成

- Java: 現在の`UnityPlayer.currentActivity`にattachしたVISIBLEなWebView。独立ActivityやVirtualDisplayは作らない。通常Windowへのdrawを抑制し、同じ合成ページ・動画をhardware Canvasへ描く。
- NDK: `AImageReader`のRGBA8 Surface / AHardwareBuffer。CPUで画素を読み出さない。
- Unity native plugin: 実行rendererに応じてbackendを選択する。Vulkanでは起動時に初期化をinterceptし、対応しているデバイスで`VK_ANDROID_external_memory_android_hardware_buffer`と`VK_EXT_queue_family_foreign`を追加する。非対応・追加失敗時は通常のUnityデバイス作成を維持し、プローブを停止する。
- Vulkan render thread: acquire fenceを非ブロッキングpollし、完了したAHBを専用メモリーへimport。foreign queueからownershipを取得し、Unity所有のRenderTextureへGPU copyしてownershipを返す。Unityの`safeFrameNumber`までAImageとimport資源を保持する。毎フレームのqueue/device wait idleは行わない。
- GLES render thread: 同じAHBをEGLImage経由でGL textureへ取り込み、raw GPU copyでUnity textureへ渡す。EGL fenceが完了するまで画像を保持する。詳細と拡張条件は[GlesSupport.md](GlesSupport.md)。
- World-space uGUI表示とコントローラーまたは手のレイ。入力イベントはWebViewのviewport座標へ渡し、DOMのscroll量を加えない。IMEのInputConnectionはWebView自身に任せる。

最大6枚の未返却画像でproducerを抑制し、Unity休止中も無制限に描き込まない。pause・resize・recreateではJava UI側の描画を止めてからnative側へ停止を通知する。古いRenderTextureはGPUの解放を待って破棄する。
これは最適化前の検証経路。フェンスのCPU側poll、画像ごとのimport、30 fps producerにより遅延が増える可能性がある。外部format/YUV、保護動画、fullscreen custom viewは扱わない。

既存のVulkanXrProbeシーンも互換クラスでそのまま使用できる。シーン再生成は任意。GLESの条件・SDK設定・調査結果は[GlesSupport.md](GlesSupport.md)を参照。

Vulkan初期化の旧エラーへの修正内容と再ビルド手順は[VulkanInitialization.md](VulkanInitialization.md)を参照。

ハンドトラッキングでは[HandInputProbe.md](HandInputProbe.md)のHI1手順を使用する。コントローラーがなくても手のrayとpinchで操作できる検証実装を追加した。

## ビルドと操作

1. Unityを閉じて追加パッチを適用し、6000.4.5f1で開く。
2. **VRWebView > Create GPU XR Probe Scene** を実行する。生成された`GpuXrProbe.unity`だけをAndroid Build ProfileのScene Listへ登録する。古い`GpuProbeLauncher`を同じシーンに置かない。
3. AndroidのGraphics APIは比較する**Vulkan**または**OpenGLES3**だけを選び、Target Architecturesは**ARM64**。既定のVulkan設定は変更していない。GLES比較はLinear色空間を維持し、Meta XR Subsampled LayoutとMeta Quest SupportのOptimize Buffer DiscardsなどVulkan専用設定を無効にする。最初の比較ではfoveation/SpaceWarpも無効にする。OpenXRとQuestのコントローラープロファイルを有効にする。Oculus XR Pluginへの変更は不要。
4. Build And Runする。初回のプラグイン追加後はアプリを完全終了して起動する。ネイティブ`.so`のPlugin InspectorでAndroid ARM64 / Load on startupが有効なことを確認できる。
5. 手のrayはピンチでクリック/ドラッグできる。既存シーンではInspectorのFlip Image Verticallyをオンにして比較する。右コントローラーの水色レイをページへ向け、トリガーでクリック・ドラッグする。スティック上下でスクロール。AはIME要求、BはIMEを閉じてUnityへfocusを返す。
6. 下部のIME / Focus / Reload / Recreate / Resize / Flip Yも同じレイで操作できる。表示が逆ならFlip Yで比較する。入力座標も同時に反転する。

頭とコントローラーのposeはUnity XR InputDevicesから取得する。検証専用の最小構成で、Meta Interaction SDKのUIや手入力はUnity XR HandsのMeta Hand Tracking Aimから取得する。最初に有効なhead poseを取得したとき、パネルを頭の正面に置く。Recreateで再配置できる。頭を動かしたときにパネルが空間内に残るか確認する。

## 合否を切り分ける順序

1. **起動・GPU共有**: ページがVR空間に表示され、`backend=Vulkan`または`backend=OpenGLES3`が期待した値になり、`GPU copies`と`timestamp`が進む。黒画面なら診断表示を先に読む。不足する拡張は診断に表示する。実行中のGraphics API切替やCPUコピーへの自動切替はない。
2. **動画の実画素**: Play/Pauseを押し、Unityパネル内の焼き込み`VIDEO frame`番号と色が更新され、音が出る。pause/seek、画面外へスクロールして戻すことも確認する。`GPU copies`増加や音だけでは動画合格にしない。
3. **XR共存**: 再生中に頭・コントローラーを動かす。`XR running=true`、Unity framesが増加し、パネルが頭に追従せず空間内に残り、レイが動くことを確認する。新しいAndroid Activityへ切り替わっていないことも確認する。
4. **入力・IME**: スクロール後のtext/textarea/contenteditableを選び、日本語変換・確定・途中挿入・フィールド切替を試す。`viewFocus / windowFocus / immActive / inputConnections`も記録する。IMEが出ない場合は入力欄を選んでAを一度押し、同じ値の反復ならその状態を報告する。
5. **Unityへの復帰**: BまたはFocus後にレイと再クリックが動くこと、WebViewへ戻ってIMEを再度開けることを確認する。OSキーボードへfocusが移るだけではWebViewを破棄しない。
6. **ライフサイクル**: 再生中のRecreate/Resizeを各5回。Questメニューやスリープから復帰後にページが再生成され、動画とIMEを再開できることを確認する。再生成でページ・再生位置は初期化される仕様。

報告には、表示可否、VIDEO frameの更新、音、スクロール、日本語、XR動作、再生成/復帰と、停止時の診断欄を含める。入力した本文は不要。

## 診断の読み方

- `backend=Vulkan / OpenGLES3`: Unityが選択した描画API。capabilityはrender threadで確認するため、device selectedだけではimportや画素表示の成功を意味しない。
- `backend=none`: backendの初期化が成立していない。次の理由の行と`vkHook`の行を記録する。
- `vkHook=V2/accepted callback=1 create=1 fallback=0`: V2で登録され、初期化callbackとデバイス作成hookが実行された。カウンターは端末・プラグイン構成で変わり得る。実画素の成功とは別。
- `registration rejected / unavailable`: 初期化hookの登録が拒否された、またはinterfaceがない。遅いロードの可能性を含む。
- `accepted but not invoked`: 登録は受理されたが初期化callbackが呼ばれていない。
- `vkCreateDevice was not intercepted`: 初期化callbackは呼ばれたが、デバイス作成は観測されていない。
- `Missing device extension`: 実際に列挙したデバイス拡張に必要項目がない。再起動だけでの解決を前提にしない。
- `Probe extension request rejected: VkResult=...`: 追加拡張付きの作成が失敗し、Unity側は元の設定で継続した。WebView用backendは使わない。
- `GPU copies / acquired / inFlight / slots`: GPU転送・取得・返却待ち画像数。copiesはcommand記録回数で、画面表示完了の証明ではない。
- `fenceWait / producerSkipped`: フェンス未完了や画像数制限による待ち。増加し続けてcopiesが止まるならその状態を報告する。
- `sameActivity`: 現在のUnity Activityのクラス名。
- `canvas / hardware / windowDraw=off`: source側の描画回数・Canvas方式。WebViewはattach・VISIBLEを保つ。画面外配置やGONEの試験ではない。
- `view / window`: WebViewと実Windowのピクセル寸法。Windowより大きい領域のfocus/IME anchorは特に確認が必要。
- `inputConnections`: 接続要求数/非null返却数。IME表示・変換成功の証明ではない。

## クラウドで確認できること

`Tools/build_android.py`はNDK r27c / CMake 3.22.1 / JDK 17以上 / SDK35でarm64 `.so`とAARをビルドし、source/成果物のSHA-256を更新する。NDKは`--ndk`または`ANDROID_NDK_HOME`、CMakeは`--cmake`で指定できる。`.so`は16 KiB alignment、起動時preloadで配布する。

`Tools/run_native_probe_checks.py --ndk PATH --jdk PATH`はLinuxホスト上の偽Android/Vulkan/GL/EGL endpointsで**実際のnative実装**を動かし、acquire fence、safe frame、画像数制限、停止、import失敗の破棄をASan/UBSan/LSanで確認する。さらにarm64 ELFのexports・alignment・hash・preload metadataを確認する。実端末のAHB対応・Vulkan/EGLの実画像import・Unityプラグイン読込・OpenXR・IMEは確認できない。

描画の上下、色空間、遅延、IME候補位置、fullscreen/MSE/DRMなどは製品化前の別検証項目。Unity Native Plugin API headersの由来とライセンスは`Android~/webview/src/main/cpp/Unity/PROVENANCE.md`を参照。
