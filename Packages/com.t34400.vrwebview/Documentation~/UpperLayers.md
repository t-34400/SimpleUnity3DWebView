# 実機待ちと独立して実装した上位レイヤー

preview.23で実装した純C#の上位状態・表示借用契約とサンプルモデルの記録。preview.24で追加したdispatcher/Texture公開口/表示部品は[UnityView.md](UnityView.md)を参照。製品Android backend、IME、実GPUとの接続はまだ未実装。[レイヤー設計](LayeredArchitecture.md)の全移行が完了したわけではない。

## 実際に接続した経路

`Samples~/Browser/BrowserWorkspace -> BrowserTab -> BrowserSession -> IBrowserBackend`を実装した。Tools/CoreTestsのtest backendでこの経路を通して初期化・操作・終了・遅い結果を検証する。fakeはToolsのテスト内だけにあり、RuntimeやSamplesへ動作するAndroid実装として入れていない。

`Runtime/Presentation`はCore方向だけを許可したnoEngineReferences assembly。`Samples~/Browser`も純C#の独立assemblyで、Coreだけを参照する。Samplesをimportしなくても本体はその型を必要としない。

## BrowserSessionの所有thread

以前は非同期Initialize完了時に、continuationが走ったthreadからStateをReady/Faultedへ変更していた。今回、terminal状態の変更とTaskの完了もdispatcher経由で所有threadへ戻す。呼出しAPI、backendの初期呼出し、Disposeは所有threadから行う契約。

初期化Taskの成功時点でStateはReadyになっている。dispatcherへ配送済みでもまだ実行されていなければInitializingのまま。初期化中のDisposeはbackendが取消しを無視しても論理Taskを取消し、遅い結果でReadyへ戻さない。予期しないbackend取消しはFaultedと取消しTaskで表し、Initializingへ残さない。

StateChangedのobserver例外はLastNotificationErrorへ記録し、他listenerとライフサイクルを継続する。dispatcher自体が使えなくなった場合は初期化Taskを失敗にするが、workerからowner状態へ代入して帳尻を合わせない。dispatcherの継続稼働は統合側の前提である。

## 借用する表示面

`IBrowserSurfaceSource<TSurface>`は現在値とChangedを公開する読取り契約。backendやresource所有者が実装する予定。`BrowserSurfaceSnapshot<TSurface>`は参照・寸法・source内で単調増加するrevisionを持ち、null参照/0寸法のsnapshotで撤回を示す。

revisionは表示通知の順序であり、native ID、GPU世代、フレーム更新番号ではない。値があることも動画やHMDへの実表示成功の証拠ではない。

`BrowserSurfaceBinding<TSurface>`は表示先のcallbackへ借用参照を渡す。実Textureを使うときは型引数へUnityのTexture型等を使えるが、今回はそのUnity componentを追加していない。

- 切替時に古い参照を外し、providerのCurrentを読み、新しいsourceだけを購読する。
- workerからの通知を所有threadへ配送し、古いsource/古いrevision/撤回前の遅延通知を無視する。
- Bind(null)/Disposeは購読と表示参照を解除するだけ。providerのDispose、Texture.Destroy/Release、GPU退役は呼ばない。
- presenter例外はLastPresentationErrorへ記録し、bindingの状態と購読の整合性を保持する。

実resourceの所有者は、撤回やsource切替だけを理由にin-flight Textureを解放してはいけない。GPU寿命の契約は[GpuLifetime.md](GpuLifetime.md)のまま。表示側で生の参照を保管し続けることを型だけで禁止できる、という保証でもない。

## ブラウザーサンプル側

BrowserWorkspaceはtabの作成・選択・終了と所有権、BrowserTabは初期化とnavigation要求の状態を扱う。UIの作り方、カメラrig、レイ、JNIを知らない。tabsを切り替えてもbackgroundのSessionは保持する。

サンプル内の実装なので、将来のタブ/URLバー/ブックマークUIと合わせて利用者が変更できる。入力/IME/描画の重要な実装をコピーする場所にはしない。APIと限界は[サンプルREADME](../Samples~/Browser/README.md)。

## 依存境界のチェック

`Tools/check_layer_boundaries.py`をrun_checksへ接続した。Core、Presentation、Browser.SampleのnoEngineReferencesと許可するassembly参照を検査し、package内の名前参照の循環を拒否する。既存のProbeやEditor等は新しいpure-boundary方針の対象外としてreportへ列挙する。

package全体のXR依存除去や製品AARからの検証code除去を完了したというcheckerではない。現在の旧Runtime/Editor、package.jsonのXR関連依存は残っている。GUID形式のassembly参照の解決はこのcheckerでは扱わず、現行の名前参照を対象とする。

## 検証と残作業

所有dispatcher、非協力的なbackend、複数tab、reentrant observer/factory、close失敗、surface切替/撤回/旧通知を合成で検証する。既存のGPU退役等を含め純C#80シナリオ。実際のAndroid、Texture、Unity main thread loopやHMDを動かすテストではない。

次の接続はUnity側の所有thread dispatcherと表示component、実backend factory、実surface source。初期化済みを装う既定backendや実ページを表示したように見せるplaceholderは追加していない。Rich BrowserのUI、history/navigation通知、XR adapterは今後の実装範囲。実機試験の再開や再ビルドは依頼していない。
