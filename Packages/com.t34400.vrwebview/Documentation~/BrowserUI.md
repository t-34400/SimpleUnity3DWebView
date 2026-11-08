# 任意importのBrowser UI sample

preview.25。タブとURLを操作するuGUIサンプルを実装した。**Unityでのimport/compile/実行は未確認。製品Android factoryも未実装で、これだけではWebページを開けない。** fakeページや既定の成功backendを入れて動作済みとは扱わない。

## 含まれる実装

| 型 | 役割 |
|---|---|
| BrowserPanelBuilder | 既存Canvas配下にタブ列、URL欄、Go/New/Close、BrowserView、statusを生成 |
| BrowserPanel | BrowserWorkspaceとuGUIを接続し、選択中pageの表示・入力先をBrowserViewへ渡す |
| BrowserPage / BrowserPageFactory | 実装側のbackend・surface source・入力先・追加寿命をsampleへ接続する役割 |
| BrowserAddressDraft | タブ固有の編集文字列と送信receipt。古い応答が新しい入力を上書きしない |
| BrowserBackendLease | workspaceへ所有を渡すbackendと任意connectionの、一度だけの論理終了 |

純粋なmodel assemblyはCoreだけに依存したまま。uGUIはSamples~/Browser/UnityUIの子assemblyへ分け、RuntimeからSampleを参照させない。新しいUI assemblyの参照方向も静的検査する。

## UIの動作

- Newは注入factoryからページを一つ作って初期化する。URLを勝手に開かず、初期化後に利用者がGoで要求する。
- タブ選択ではbackground Sessionを保持する。activeをCloseすると隣を選択する。Close時は表示・pointerを先に外してからbackendを終了する。
- URL文字列は各タブに保持する。tab切替前に共有InputFieldの編集を終了し、新しいタブのdraftへ切り替える。
- Goだけでnavigationを要求する。InputFieldのblur/onEndEditを送信へ結び付けないため、タブ切替だけで移動を要求しない。Enterでの自動送信も今回追加しない。
- navigation要求中の編集は新しいrevisionになる。古い要求が完了しても、その文字列を上書きしない。別タブ・別scope・再送receiptも拒否する。
- URL欄は要求・編集用であり、redirect後の確定locationや履歴ではない。"Navigation request sent"はAPI応答で、ページ読込完了を意味しない。
- タブbuttonは同じmodelの状態更新で作り直さない。入力先のBindも選択先が変わるときだけ行い、URL編集やbusy通知でgestureを毎回取消さない。
- factory未設定では"Browser not configured"を表示し、ページ生成・移動を無効にする。ダミーのページやTextureを表示しない。

戻る/進む/reload、確定URL・title・loadイベント、履歴/ブックマーク、永続化は今回未実装。Coreの現在のbackend契約に存在しない情報をUIで捏造しない。

## UIを生成する入口

Package ManagerのBrowser UI and Modelを任意importする。Canvas配下のRectTransformにBrowserPanelBuilderを追加し、Context MenuのBuild Browser UIまたはPlay時のStartで生成できる。Buildは生成済みpanelを再利用する。

Canvasのrender mode、サイズ/scale、Camera、EventSystem、Input System/XR用UI moduleは利用アプリが用意する。builderはカメラrigや入力moduleを作らず、プロジェクト設定も変更しない。World Space Canvasでも標準uGUI入力を使う。見た目・配置は実Editorで確認が必要。

URL欄は標準InputField、SingleLine、TouchScreenKeyboardType.Default。Questでのキーボード実表示・入力後のレイ復帰を確認した成果ではない。IME/入力許可の所有者はSetPageInteractionAllowedでpage入力を制御でき、ここでfocusを強制復元しない。

## 実backendを接続する契約

Inspectorではアプリ側のBrowserPageFactory派生componentをbuilderへ渡せる。プログラムからは次の形で注入する。下記は接続形で、Android factoryを実装したコードではない。

```csharp
var panel = builder.Build();
panel.Configure(dispatcher => {
    // アプリ/製品adapterが生成する実装。これらのfactoryは今回未実装。
    var page = createRealPage(dispatcher);
    return new BrowserPage(page.Backend, page.Surface, page.Input, page.ConnectionLifetime);
});
```

生成ごとに独立したbackendとsurface sourceを渡す。失敗前に確保した資源はfactoryが終了し、戻した後の論理寿命はworkspaceがBrowserBackendLease経由で所有する。Runtimeの製品実装がsample型へ依存するのではなく、アプリadapterがroleを束ねる。

BrowserPage.Disposeはbackendと任意connectionを一度ずつ終了する。同じobjectを両役割へ渡した場合は一度だけ呼び、一方の終了が失敗しても他方を試してAggregateExceptionで報告する。connectionLifetimeにin-flight GPU資源の強制解放を登録しない。実GPU解放は[GPU寿命の契約](GpuLifetime.md)に従う。

同じdispatcher scope内でBrowserPanel componentだけを無効にする場合、UI/inputを外してmodelを保持する。Canvas/GameObject全体を無効にしてbuilder配下のBrowserDispatcherも止める場合はscope終了となり、全タブを閉じる。再開時に古いSessionを復活させない。backgroundを残す構成では、所有dispatcherの配置と寿命をアプリ側で明示する。

## 検証と未確認事項

純C#にdraftの遅延/別タブ/再送応答とleaseのcommand/終了/例外/同一object所有を10シナリオ追加し、既存と合わせ105件を実行する。UI sample assemblyからmodelへの依存方向を静的検査し、全package C#は構文解析する。これらはUnityの型/参照解決ではない。

Samples~/Browser/Tests/Editorに6つの結合テストを定義した。UI未設定、tab選択/終了、URL draft、blurで送信しないこと、遅延応答とscope差し替え、factory失敗を扱う。テスト用backend/TextureはこのEditor専用test fixture内でだけ生成し、既定factoryやPlayerへ組み込まない。

sampleの結合テストはimport後のT34400.VRWebView.Browser.Sample.Editor.Tests assemblyで、共通editor-checkの既定Runtime test assemblyとは別。Unity Test Runnerで選択できる想定だが、実Editorでのimport・compile・discovery・実行は未確認。既存RuntimeのEditMode/PlayModeテストも引き続き未確認。

今回Android~/native/AAR/.so、既存Runtime、GPU probe/pump、SceneやProjectSettingsは変更していない。製品backend/factory・描画/入力adapter・IME・実GPUの統合と、実機確認は残る。追加のHMD試験や再ビルド依頼ではない。
