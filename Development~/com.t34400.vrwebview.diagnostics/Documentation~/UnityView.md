# Unity dispatcher・Texture公開口・表示部品

preview.24。Unity側の部品を実装し、既存のsurface bindingと標準EventSystem入力へ接続した。**コードは実装済みだが、実Editorでの型解決・import・EditMode/PlayMode実行は未確認。製品Android backendにも未接続。** 実機操作の依頼ではない。

## 実装した経路

```text
BrowserWorkspace / BrowserSession
  -> BrowserDispatcher.Scheduler -> 所有threadのFIFO queue -> Updateで進行

実際のTextureを所有する側（Android backendとの接続は未実装）
  -> BrowserTextureSource -> BrowserSurfaceBinding<Texture>
  -> BrowserView -> RawImage

EventSystem -> BrowserPointerSource -> BrowserViewのUV/原点変換
  -> BrowserPointerRouter -> IBrowserPointerSink / IBrowserScrollSink
  -> 実backendへの入力転送（未実装）
```

Texture2DやRenderTextureの参照を本物のUnity型で受け取る入口であり、WebViewを生成するfactoryやproducerではない。Runtime/Samplesにfakeページ、成功済みと見せる既定backend、ダミーのAndroid実装を入れていない。

## BrowserDispatcher

GameObjectに置くMonoBehaviour。Awake/OnEnableで所有threadを決め、Updateでqueueを進める。通常のUnity実行では自動初期化される。EditModeの呼出し元等はInitializeOnOwnerThreadを明示できる。

- Schedulerは、そのqueueの寿命へ固定したAction<Action>。workerからPostでき、実行は所有thread上。Disable/Close後の古いSchedulerは新しいqueueへ流用されず拒否される。
- queueは既定4096件、Updateは既定256件まで。上限超過を黙って捨てず例外で返す。所有thread以外からのDrain/Disposeと再帰Drainは禁止。
- Track(IDisposable)で論理寿命を登録する。返るregistrationをDisposeすれば、その所有物を一度だけ終了して登録を外す。
- componentのDisable/DestroyまたはCloseでは登録した寿命を逆順に終了し、queueを閉じ、残っていた有限の処理を実行する。新規Postは受け付けない。一つの処理や終了が失敗しても他を進める。
- 再Enableは新しいscope。古いSessionやproviderを復活させるのではなく、新しい構成が必要。

Core/Sampleへの接続例は次の形。backend factoryそのものはまだ未実装。

```csharp
var workspace = new BrowserWorkspace(createOwnedBackend, dispatcher.Scheduler);
var lifetime = dispatcher.Track(workspace);
```

Trackは表示・Session等の論理寿命用。GPU退役サービスを即時停止するために使わない。Sessionを閉じた後のnativeStop/画像保持/GPU完了の進行は別のrender serviceで継続する契約。

## BrowserTextureSource

IBrowserSurfaceSource<Texture>のUnity向け実装。所有dispatcherへ自動登録される。Publish、Withdraw、DisposeとCurrentの読取りは所有thread上で行う。

- Publish(texture, origin)は参照・寸法・原点を通知する。同じTexture/寸法/原点の繰り返しは通知しない。
- これはframe heartbeatではない。動画等で同じTextureの画素が更新されても再Publishは不要。RawImageは同じ参照を表示し続ける。
- WithdrawとDisposeは表示参照を撤回する。TextureのDestroy/Release、WebView停止、GPU解放は行わない。
- Unityのdestroy済みTextureをPublishしない。GPU資源所有者は撤回と[GPU寿命の契約](GpuLifetime.md)に従い、providerのDisposeを物理解放と扱わない。

通常のUnity TextureはBottomLeft、既存のtop-first hardware Canvas経路を接続する場合はTopLeftを明示する。特定のgraphics API名から原点を推定しない。

```csharp
var source = new BrowserTextureSource(dispatcher);
view.Bind(source, backendInputSink, dispatcher);
// 実際の資源所有者が作成済みtargetを渡す。以下はproducerの実装ではない。
source.Publish(targetTexture, BrowserSurfaceOrigin.TopLeft);
```

## BrowserView

Canvas配下のRawImageとBrowserPointerSourceを使うMonoBehaviour。Canvas・Camera・EventSystem・XR rigはアプリが用意する。SessionやTextureの所有者ではない。

- Bindはsourceと入力先の契約を受け取る。入力先nullなら表示のみ。sourceのCurrentを表示し、以降の通知へ接続する。
- 所有thread上のPublish/Withdrawは同期的にRawImageへ反映し、worker由来の通知はqueueへ戻す。遅い通知はbinding版/revisionで無効化する。
- RawImageのtextureとenabledを管理する。既定では原点に応じてuvRectを設定して上下を揃える。autoOrientTextureを無効にすればアプリのUV設定を使う。
- pointer位置は表示中のuvRectと原点を通してブラウザーの左上原点へ変換する。crop/mirrorやcapture後の範囲外dragを扱い、二重にYを反転しない。
- source/targetの交換、撤回、Disable、Unbindでは、旧入力先へ一度Cancelを要求してから参照を外す。新しいtargetへ保持中のgestureを移さない。
- viewだけのDisableは接続設定を保持し、同じscope内の再Enable時にsource.Currentから復帰する。Unbindとdispatcher scope終了は接続設定も外す。
- 非表示中や非active objectへのBindもscopeへ登録する。scope終了後の再Enableで古いprovider/入力先を再利用しない。IsConfiguredは表示中かどうかではなく接続設定の有無を示す。
- SetInteractionEnabled(false)は入力を取消し、以後のTexture交換で勝手に入力を再開しない。trueへの復元は所有者が明示する。
- binding解除やview破棄でsource/Session/TextureはDispose/Destroyしない。

Backend入力先はIBrowserPointerSink、scrollを扱う場合はIBrowserScrollSinkを実装する。scroll deltaはlogical stepで正Xが右、正Yが上。CSS pixelやAndroid MotionEventへの変換は実backendの責務。メソッド呼出しの成功はDOMやHMDで入力された証拠ではない。入力転送例外はLastInputErrorとログへ、表示例外はLastPresentationErrorとログへ記録する。

SetInteractionEnabledは許可状態を適用する入口で、IME/Window/OpenXR focusを自動判定する実装ではない。現在のIME未成立を解決したとは扱わない。

## 検証の範囲

| 対象 | 状態 |
|---|---|
| queue・router・原点/UV変換 | 新規15シナリオを純C#で実行。既存と合わせ95件 |
| 依存境界・起動/採取/Editor launcher | Pythonで検証。Unity/UnityUIの許可参照も検査対象へ追加 |
| Unityを含む全C#ソース | SDKのRoslynでC#9構文を検査。空/不正ソースを成功にしない |
| EditMode | 実Texture、RawImage、EventSystem座標、scope終了等のテストを追加。未実行 |
| PlayMode | view/dispatcherのDisable/再Enableを扱う2テストを追加。未実行 |
| Android/Quest | この変更での実行検証なし |

構文検査はUnityの型/参照解決ではない。.NETテストへUnityの偽stubを入れてcompile済みと報告しない。このクラウドのEditor doctorは未検出。新しいUnity classのEditorコンパイルとテストdiscoveryも未確認。

Editor検証入口は[EditorValidation.md](EditorValidation.md)。共通editor-checkに--test-platform EditMode/PlayModeを追加し、既定EditModeを維持した。PlayModeでもEditor内実行で、Android Player buildやHMD実行を指示するものではない。

## 残っている実装

製品Android backend/factory、生成したGPU target/入力先を上記へ接続するadapter、GpuRetirementLedgerの実pumpへの接続、IME成立条件、リッチなブラウザーのUIは残る。今回の部品の実装から、全ブラウザー経路が完成し実機検証だけを待っているとは結論しない。
