# Browser UI and Model sample

Package ManagerのSamplesから任意にimportするタブ/SessionモデルとuGUI。UnityUI/BrowserPanelBuilderが既存Canvasの下にタブ列・URL欄・Go/New/Close・ページ枠を生成する。完成シーンは含まない。Android/AndroidBrowserPageFactoryをBrowserPanelへ指定すると実backendを生成でき、未設定なら操作を無効にして表示する。既定のfakeページやbackendは同梱しない。

詳細はpackageのDocumentation~/BrowserUI.mdを参照。Unity Editorでのcompile/import/実行、QuestのIMEや操作復帰は未確認。Tests/Editorのテスト用factoryはEditor専用fixtureで、Playerや通常のsample起動には使わない。

Canvas/Camera/EventSystem/XR UI moduleは利用アプリが用意する。BrowserPanelBuilderのContext Menu「Build Browser UI」、またはPlay時のStartで生成し、BrowserPageFactory派生componentかBrowserPanel.Configureのdelegateで実実装を注入する。model単体は引き続きUnity非依存で、別assemblyにある。

サンプルはimport時にAssets/Samplesへコピーされる。更新時は利用者の変更を保全して移行し、同名asmdefを持つ複数versionのコピーを同時にcompileしない。自動で古いコピーを削除・上書きする処理は追加していない。

`BrowserWorkspace`へ、呼出しごとに新しく所有権を渡す`IBrowserBackend`のfactoryと、所有threadへ直列配送するdispatcherを注入する。既存のBrowserSessionを使って初期化・ナビゲーション・終了する。preview.36でAndroidBrowserPageFactoryを追加した。製品経路の実Editor/APK/Quest確認は未実施。

```csharp
var workspace = new BrowserWorkspace(createOwnedBackend, postToOwnerThread);
var tab = workspace.CreateTab();
var initialization = tab.InitializeAsync();
await initialization;
// 所有threadへ復帰する呼出し元で実行する。
await tab.NavigateAsync("https://example.org/");
// 表示切替ではbackgroundのSessionを閉じない。
workspace.SelectTab(tab.Id);
// 明示的な終了だけがSessionを閉じる。
workspace.CloseTab(tab.Id);
workspace.Dispose();
```

上記はasyncメソッド内の使用例。Initをawaitする呼出し側も、次のAPI呼出しを所有threadへ戻す必要がある。Unity main threadのSynchronizationContextを保つ呼出し元、または明示的な所有threadへの配送を使う。dispatcherはFIFOで直列に配送し、全Sessionが閉じるまで使える必要がある。Unityの所有thread/Updateへの実接続は別途必要。

`Tabs`は読み取り専用のlive collection、Changedは「現在のmodelを読み直す」通知。過去の状態snapshotや一通知一操作のイベントログではない。UIは選択中のtabと各tabの状態から再描画する。公開メソッドとmodelの読取りは所有threadに限定する。

- `CreateTab`はfactoryを一度呼び、Sessionを所有する。factoryの再入によるtab作成は禁止。
- `InitializeAsync`はtab内で共有。失敗したtabは残してLastErrorを表示できる。再初期化を勝手に繰り返さない。
- `NavigateAsync`はReadyが必要で、同じtabの重複commandを拒否する。不正URLは状態変更前に拒否する。
- `RequestedAddress`は利用者が要求したURL。redirect後の確定URL・タイトル・履歴を示さない。
- `IsBusy`はAPI commandの応答待ち。ページ読込中、動画準備中、実表示済みを意味しない。
- 選択だけでは背景tabを破棄しない。選択中を閉じると右隣、なければ左隣を選択する。
- Closeは論理commandをすぐ取消し、古い結果を無効化する。取消しは既に始まった実navigationを巻き戻さない。
- Disposeは各Sessionの終了を一度ずつ試す。一つの失敗で他tabを放置せず、最後にAggregateExceptionを返す。
- observer例外はLastNotificationErrorへ記録し、他listenerと所有権処理を継続する。

戻る/進む・reload・確定URL・タイトル・履歴・ブックマークはまだ実装していない。これらを実装するときも、SessionやGPU資源の寿命をUIへコピーしない。
