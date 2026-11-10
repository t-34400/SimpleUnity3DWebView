# Unity Editor検証の入口

2026-11-08 JST。責務分離で必要となるEditor import/compile確認のための起動経路。現在の実機試験停止は継続し、この資料は新しいHMD試行の依頼ではない。

## 一つのコマンド

Unityを閉じた状態で、同じ`Tools/probe.py`から実行できる。

```sh
python3 Tools/probe.py editor-doctor
python3 Tools/probe.py editor-check
```

`editor-doctor`はインストールされたEditorとテスト設定を確認するだけで、Editorを起動しない。`editor-check`はUnity Test Frameworkをbatchmodeで起動し、Androidをactive buildTargetとしてpackageのEditor内テストを実行する。既定はEditMode、`--test-platform PlayMode`でEditor内PlayModeを選べる。Player/APKのビルド、HMD試験、製品シーン生成、IME方式変更は行わない。PlayMode test内では一時GameObjectを作る。

EditModeは従来どおり`-nographics`を使う。PlayModeは描画を有効にして起動し、専用引数`-vrwebviewEditorTestsWithoutXR`を付ける。通常のEditor PlayやAPKにはこの引数を渡さない。

PlayMode test assembly内のEditor限定helperが、batchmode・runTests・PlayMode・このpackageのassembly名・専用引数の全一致時だけ動く。`SubsystemRegistration`でEditorの`XRGeneralSettings.Instance`参照を一時的にnullにする。XR Management 4.5.2は`AfterAssembliesLoaded`/`BeforeSplashScreen`でこの参照を確認するため、自動initialize/startを行わない。serialized設定・Android設定・loader一覧は編集も保存もしない。通常のドメイン再ロードではXR ManagementがEditor参照を再構築し、再ロードなしの場合はEnteredEditMode/終了時に旧参照を戻す。別ownerが新しい参照を設定していれば上書きしない。

`BeforeSceneLoad`でも参照が外れていることを確認し、`VRWEBVIEW_EDITOR_TESTS: XR auto-start isolation verified before scene load`を記録する。PlayMode成功には、この観測行も必須とする。helperの実Editor実行順・挙動は今回の修正後にはまだ未確認で、合成launcher成功を代用にしない。これはXR機器不要のlifecycleテスト環境であり、XR統合の検証にはならない。

Editorは`--editor PATH`または`VRWEBVIEW_UNITY_EDITOR`で指定できる。未指定ではProjectVersionの版に対応するWindows/WSLのUnity Hub配置、PATHのUnityを確認する。WSLからWindows Unity.exeを呼ぶ場合は、project/results/logのパスをwslpathでWindows形式へ変換する。PATHへshellコマンド文字列を渡す形式ではない。

出力先は既存の`VRWEBVIEW_LOG_DIR`または`--log-dir`。WSLでは既定のDownloadsを使う。実行ごとに新しい`VRWebView-UnityCheck-...`フォルダーへ次を保存する。

- `Editor.log`
- `editmode-results.xml`
- `summary.json`

PlayMode選択時のXML名は`playmode-results.xml`。summaryには選択したtest_platformとtest_assemblyを記録する。両modeは別の新規フォルダーを使う。

Editorの起動は1回だけで、自動再試行・再起動は行わない。全体の既定timeoutは1200秒、`--timeout`で変更できる。30秒ごとに経過時間、段階、ログのbyte数を表示する。NUnit XMLが読み取れた時点で成功・失敗件数と失敗名を表示し、`summary.json`へ記録する。XML出力後のプロセス終了待ちは既定60秒で、`--shutdown-timeout`で変更できる。全体の残時間が短ければそちらを優先する。

summaryは起動前から作成し、`status=running`と`phase`を更新する。テスト結果は`test_report`へ、プロセス終了は`exit_code`/`process_exit_observed`へ別々に保存する。テスト失敗と終了待ちtimeoutが同時に起きても、XMLの失敗を隠さない。中断・timeout・起動エラーも成功として報告しない。Unityの起動による通常のLibrary/import処理と、テスト内の一時GameObject作成は発生する。

## 成功と判定する条件

- Unityの終了コードが0。
- 今回の新しい出力先へNUnitのtest-run XMLが生成された。
- `T34400.VRWebView.Tests.`内のtest-caseが1件以上あり、すべてPassed。
- rootの結果と件数が整合し、Skipped/Failed/Inconclusiveのケースがない。
- Editor.logが存在し、空ではない。ログにEditor版が記録された場合はProjectVersionと一致する。
- Native Crash Reportingが記録されていない。PlayModeでは専用helperのXR抑止確認行が存在する。

過去のXML、0件、他namespaceのテストだけ、終了コード0だけでは成功にしない。XMLがすべてPassedでも、終了が観測できない場合は失敗とする。timeoutでは自分が起動した子プロセスだけにkillを要求し、全Unity/Hub/Java/ADBをまとめて終了しない。WSLからWindows exeを起動した場合、WSL側の子の終了だけでWindows Editorの終了は保証できず、summaryと端末にその限界を明示する。再度プロジェクトを開く前に、そのbatch Editorが閉じていることを確認する。summaryのEditor版は期待値と観測値を分け、観測できなかった版を確認済みとは呼ばない。

packageは既にmanifestのtestablesへ登録され、Test Framework 1.6.0が解決されている。選択するassemblyは既定`T34400.VRWebView.EditMode.Tests`、PlayModeでは`T34400.VRWebView.PlayMode.Tests`。独自Editor entry pointは追加していない。実行は`-runTests`側の終了を使い、`-quit`は付けない。

## 実Editorで得た証拠と今回の修正

2026-11-08の利用者提供`summary.json`/`editmode-results.xml`/`Editor.log`は、Unity 6000.4.5f1でpackageの実EditMode assemblyがコンパイル・実行された証拠。18:54:47 JSTに14件中13件成功、1件失敗で終了した。失敗は`PointerTests.OverlayCoordinatesAndDisableCancelCapturedPointer`、`PointerTests.cs:30`でCancel数の期待1に対して0。

旧テストは通常MonoBehaviourの`OnDisable`がEditModeでも実行される前提を置いていた。preview.26では、座標と明示的InputEnabled解除をEditModeで検証し、GameObject/コンポーネント無効化による自動CancelはPlayModeへ移す。Downが成立したことも先に検証する。テストを通す目的でRuntimeへExecuteAlwaysを追加しない。実Runtime部品は変更していない。

ログには`Exiting with code 2 (Failed)`、Input System/Mono/Package Managerの終了処理がある。一方、Pythonはプロセス終了を受け取れず、起動後1200秒でtimeoutした。Unity/プラグイン/WSL interopのどれが最終終了を妨げたかは断定できない。繰り返し起動した証拠はない。終了待ち対策は診断と待ち時間の改善であり、その根本原因の解消を主張しない。

起動引数、XML整合、失敗と終了待ちtimeoutの併存、Passed XMLだけでは成功にしないこと、1回だけの起動、進捗保存・heartbeat、WSLパス変換を合成実行ファイルで検証する。これは本物のUnity compile/importではない。今回修正したテストの実Editor再実行、PlayMode、import後sample assemblyはまだ未確認。

CLIの参照資料は[公開パッケージミラー1.4.5の固定commit](https://github.com/needle-mirror/com.unity.test-framework/blob/117ede6d83ffc332b51c90d40df38451b914aabf/Documentation~/reference-command-line.md)。プロジェクトは1.6.0を解決している。

Unity dispatcher/view部品は[UnityView.md](UnityView.md)に実装範囲を示す。Core・C#構文検査をUnity型検証の代用にせず、修正後の各テスト結果を確認する。Android backend接続やQuest試験はこのEditor検証とは別。

受領ファイルSHA-256（生ログ本文は配布しない）:

- `summary.json`: `84fdbe54f172b3aa6361000ab0ee23d62546b308869906c888a267fb32b0027b`
- `editmode-results.xml`: `e47f21db565b649116f26d87f761d6fe68ab1c992ca28ea95cb4a3a91ac0ee6f`
- `Editor.log`: `cdb788388e7f832065883599d09fd4b7cdda000a47c9b42c31f343effeefeb24`

## その後の利用者ログ

2026-11-08 19:50:28–19:52:21 JSTのEditMode再実行は14/14 Passed、終了コード0、process_exit_observed=true、113.444秒。summary/XML/logを照合した。前回の終了停滞は再現しなかったが、その根本原因の確定とは異なる。

20:25:46–20:28:21 JSTの旧launcherによるPlayModeは終了コード5、154.535秒、結果XMLなし。ログにはNullGfxDevice、XR Managementの自動初期化、MetaのUnsupported Graphics API: 4、xrCreateSessionのXR_ERROR_GRAPHICS_DEVICE_INVALID、続いてOpenXR message loop内のNative Crash Reportingがある。3テストの成功・失敗を示すXMLはなく、テスト開始前のXR起動でクラッシュしたと扱う。

preview.27ではPlayModeの-nographicsを外し、前述のテスト限定XR抑止を追加した。テスト本体3件や製品Runtime、Android/native設定は変更しない。ログにnative crashとXR graphics errorがある場合はsummaryへ別々に残し、XML欠落だけを主因として表示しない。修正後PlayModeは利用者環境での確認待ち。

受領ZIP SHA-256:

- EditMode ZIP: `ceab97e8d30a68353e20d41c95af1802d1e9122cd88c2e9dec2526e865d14012`
- PlayMode ZIP: `34752802e91ae571f944a64417041614eb10950f9d2bef8e15292129d5599300`

XR Managementの根拠は利用者提供のdependency sources（4.5.2、fingerprint f589c7ba49ff63cadb66fd77cdfb09f538674e1a）。`Runtime/XRGeneralSettings.cs`の2つの自動初期化methodと、`Editor/XRGeneralSettingsPerBuildTarget.cs`のStandalone参照構築を確認した。実行ログのpackage cache fingerprintとも一致する。
