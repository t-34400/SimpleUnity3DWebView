# Unity Editor検証の入口

2026-11-08 JST。責務分離で必要となるEditor import/compile確認のための起動経路。現在の実機試験停止は継続し、この資料は新しいHMD試行の依頼ではない。

## 一つのコマンド

Unityを閉じた状態で、同じ`Tools/probe.py`から実行できる。

```sh
python3 Tools/probe.py editor-doctor
python3 Tools/probe.py editor-check
```

`editor-doctor`はインストールされたEditorとテスト設定を確認するだけで、Editorを起動しない。`editor-check`はUnity Test Frameworkをbatchmodeで起動し、Androidをactive buildTargetとしてpackageのEditor内テストを実行する。既定はEditMode、`--test-platform PlayMode`でEditor内PlayModeを選べる。Player/APKのビルド、HMD試験、製品シーン生成、IME方式変更は行わない。PlayMode test内では一時GameObjectを作る。

Editorは`--editor PATH`または`VRWEBVIEW_UNITY_EDITOR`で指定できる。未指定ではProjectVersionの版に対応するWindows/WSLのUnity Hub配置、PATHのUnityを確認する。WSLからWindows Unity.exeを呼ぶ場合は、project/results/logのパスをwslpathでWindows形式へ変換する。PATHへshellコマンド文字列を渡す形式ではない。

出力先は既存の`VRWEBVIEW_LOG_DIR`または`--log-dir`。WSLでは既定のDownloadsを使う。実行ごとに新しい`VRWebView-UnityCheck-...`フォルダーへ次を保存する。

- `Editor.log`
- `editmode-results.xml`
- `summary.json`

PlayMode選択時のXML名は`playmode-results.xml`。summaryには選択したtest_platformとtest_assemblyを記録する。両modeは別の新規フォルダーを使う。

既定timeoutは1200秒、`--timeout`で変更できる。中断・timeout・起動エラーも成功として報告しない。Unityの起動による通常のLibrary/import処理と、テスト内の一時GameObject作成は発生する。

## 成功と判定する条件

- Unityの終了コードが0。
- 今回の新しい出力先へNUnitのtest-run XMLが生成された。
- `T34400.VRWebView.Tests.`内のtest-caseが1件以上あり、すべてPassed。
- rootの結果と件数が整合し、Skipped/Failed/Inconclusiveのケースがない。
- Editor.logが存在し、空ではない。ログにEditor版が記録された場合はProjectVersionと一致する。

過去のXML、0件、他namespaceのテストだけ、終了コード0だけでは成功にしない。summaryのEditor版は期待値と観測値を分け、観測できなかった版を確認済みとは呼ばない。

packageは既にmanifestのtestablesへ登録され、Test Framework 1.6.0が解決されている。選択するassemblyは既定`T34400.VRWebView.EditMode.Tests`、PlayModeでは`T34400.VRWebView.PlayMode.Tests`。独自Editor entry pointは追加していない。実行は`-runTests`側の終了を使い、`-quit`は付けない。

## 今回確認した範囲

起動引数、空/古い/不一致XML、失敗・Skipped・timeout、ログ/Editor版、WSLパス変換を合成実行ファイルで検証した。**これは本物のUnityによるcompile/import成功ではない。** このクラウドのeditor-doctorはEditor未検出を返している。Windows/WSL実環境のinterop、ライセンス、packageの実EditMode実行は未確認。

CLIの参照資料は[公開パッケージミラー1.4.5の固定commit](https://github.com/needle-mirror/com.unity.test-framework/blob/117ede6d83ffc332b51c90d40df38451b914aabf/Documentation~/reference-command-line.md)。プロジェクトの1.6.0そのものの仕様・実行を取得できたとは主張しない。

preview.24のUnity dispatcher/view部品は[UnityView.md](UnityView.md)に実装範囲を示す。EditMode/PlayModeはlauncherの合成検証までで実Editor実行は未確認。Core・C#構文検査を代用にせず、Unity import/compileと各テスト実行を残す。
