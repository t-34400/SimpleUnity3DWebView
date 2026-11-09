# Android backend — preview.36 / RT1

WF1 preview.35の実機成功を基に、製品用hostと公開レイヤーを接続した。新しい製品経路のUnity Editor型コンパイル・APK・Quest確認は未実施。旧WF1の基本試験を未確認へ戻さず、次はこの統合経路を確認する。

## 所有権と使い方

`AndroidBrowserBackend`は、同じUnity Activity内のWebViewとGPU targetを所有する。`BrowserSession`が非同期の初期化・HTTP(S) navigation・任意のHTML読み込みを扱い、`BrowserView`は公開Textureを借用して表示し、標準pointer/scrollを送る。アプリのXR rayやEventSystem moduleは本体に固定しない。

```csharp
// Unity main thread。enabledなBrowserDispatcherとBrowserViewを用意する。
var backend = new AndroidBrowserBackend(dispatcher);
var session = new BrowserSession(backend, dispatcher.Scheduler);
view.Bind(backend.Surface, backend, dispatcher);
await session.InitializeAsync();
await session.NavigateAsync("https://example.com/");
// 自分のHTMLを使う場合。ページのDOM、文字列同期bridgeは不要。
await session.LoadHtmlAsync("<input><textarea></textarea>");
// 明示的に入力を終了する場合。
backend.EndEditing();
// 所有threadで終了。GPU完了を同期waitしない。
view.Unbind();
session.Dispose();
```

初期化はAndroid hostの生成完了を待ち、Textureを公開してから完了する。初期化成功は最初のGPU frameの実表示を示さない。navigation/HTMLのTaskはUI threadへのload要求の受理で完了し、ページ読込完了を意味しない。取消しは待機を止め、既に投入したnavigationを巻き戻さない。HTMLは2 Mi UTF-16 code unitsまで。base URLはHTTP(S)、URL内credentialは禁止。

`BrowserController`は任意のscene用構成部品。InspectorのBrowserViewへ接続し、初期URLとサイズを指定する。unsupported platform/Editorをfakeで代用せず、初期化エラーにする。リッチなBrowser sampleには`AndroidBrowserPageFactory`を追加した。sample内のBrowserPanelへこのfactoryを指定する。既にimportした古いSampleは自動更新されない。

Textureの借用解除は同期、producerのcloseはAndroid UI threadで処理する。producer停止のackとnative retirementが済むまでrender pumpがtargetを保持し、その後にTextureを破棄する。ackが失われた場合は利用中のGPUメモリーを破棄せず保持し、エラーを記録する。Disposeはidempotent。Activity pauseではguardを解放し、入力を終了してproducer/WebViewをpauseする。再開時に自動IME要求やfocus奪取はしない。致命的host/GPU障害はpending Taskを終了し、surfaceを撤回し、SessionをFaultedへ移す。

## IMEと一般ページの編集終了

InputConnectionは元のWebViewのものをそのまま返す。文字列・選択・変換状態を別editorへコピーしない。Window喪失の互換回避はWF1と同じ`ImeWindowFocusGuard`を共有し、確認済みprovider/version、明示的なpointer操作直後、有効な編集接続/served状態だけに限定する。`enableKnownProviderImeWorkaround=false`で無効化できる。未知providerには適用しない。

実Viewフォーカス喪失とfocused Windowの条件下で、標準DOM scriptを一度実行する。document.hasFocus()がfalseで編集可能なactiveElementが残る場合だけblurする。新しい入力に復帰していれば何もしない。本文・選択は読まず、input eventを合成しない。通常のblur/changeの意味は保つ。open shadow rootと同一origin frameは最大16段まで辿り、cross-origin iframeとclosed shadow rootは触らない。ページ独自のfocus管理に対する一般対応保証ではない。入力欄選択時にIMEを手動で再要求するループもない。

Activity内の複数hostは編集所有者を共有し、新しいhostへのpointer-downで前の編集を終了する。非選択hostを閉じても現在のhostからfocusを奪わない。これは境界の実装で、複数ページの実機保証は未確認。

JavaScriptとDOM storageはブラウザー用に有効。file/contentアクセスは無効、HTTP(S)以外への通常navigationは拒否し、証明書エラーのoverrideはしない。camera/microphone等のpermissionとWeb fullscreenは現段階では拒否する。HTTPはアプリ側のAndroid network policyにも従う。全ブラウザー機能の対応を示さない。

## 配布境界

- `vrwebview-runtime.aar`: BrowserHost、BrowserWebView、共有guard/JNI、編集終了script。検証Activity・固定ページ・動画を含めない。
- `vrwebview-probe.aar`: 比較Activity、固定ページ/動画とRT1 fixture。runtime AARに依存する。
- 共有.so: Vulkan/GLES consumerとqueue。ABIはGpuBridgeへ集約。
- Runtime/Renderingのpump/native宣言を既存probesと共有する。移動した既存scriptのGUIDは保持。

現UPMには別probe AAR・診断assemblyとXR package依存がまだ含まれる。製品AARの分離と、UPM全体を最小構成にする作業は別であり、後者は未完了。

## 次の一回の実機確認

通常の[patch-exchange apply](StandardWorkflow.md)で更新する。Editorでは **VRWebView > Create Runtime Browser Scene** を一度選び、生成したRuntimeBrowser sceneだけがBuild Settingsに入っていることを確認する。実機成功時のGameActivity/Vulkan/ARM64/OpenXR/keyboard設定は保持する。旧WF1 sceneは比較用に残してよい。

起動前にWSLで採取する。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-RT1-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect runtime --label RT1 --log-dir "$vrwebview_logs"
```

1. Build And Runし、画面にRT1 production backend / Readyとページが出ることを確認。
2. ピンチでTextを選び、日本語変換・確定し、Done/Backで閉じる。その後native動画controlで再生/停止し、不要にIMEが出ないか確認。
3. 別の入力欄とtextareaへ再入力し、動画とスクロールも操作する。成立したら一度だけシステムメニューから復帰を確認。異常が出た時点で止め、設定違いの総当たりはしない。
4. Ctrl+Cで採取終了し、DownloadsのフォルダーをZIPにする。実際のIME表示・入力・復帰の結果を添える。画面の数値を転記する必要はない。

RT1のページには旧probe専用のpointer blur処理や編集終了関数を入れない。製品hostの標準DOM処理を通す。動画は診断AARの8秒fixtureをdata URLで渡し、ネットワーク/Rangeとは独立に再生を確認する。一般URLの読込、cross-origin iframe編集、GLES、sleep/resume、長時間・複数ページ・最小Git consumerへの導入は別の残作業。

`--expect runtime`はhost/Readyの証拠とfatal failureを検査する。InputConnectionやHOLD_LOSSのログだけでIMEの実表示・日本語編集成功とは判定しない。診断はRT1でopt-inにし、本文/周辺文字/選択/URLを出さない。
