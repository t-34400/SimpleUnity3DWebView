# 技術的負債と解消条件

2026-11-08 JST。完了は今回の修正範囲に対するもので、製品完成を意味しない。

| ID | 状態 | 課題 | 解消条件 |
|---|---|---|---|
| D01 | 今回対応 | READMEに複数世代の「次の試験」が混在 | CurrentStatusを入口にし、旧試行手順へ凍結表示 |
| D02 | 今回対応 | captureごとのタグ/フィルターで必要なログが消える | 共通collectorへ委譲。別プローブの行も保存し、対象不一致を明示 |
| D03 | 今回対応 | 空ファイルやADBエラーを採取完了と誤認 | 接続確認、raw log/summary/stderr保存、欠落・0バイトを非0終了で報告 |
| D04 | 今回対応 | 観察・ログ・仮説・実装予定が混在 | EvidenceLedgerで区別。OpenXR focus/API値から実表示を推定しない |
| D05 | main統合済み | WSL consumer CLIとmainの機能が分裂 | fc65deaをmainへ反映済み。全277件とmain上の互換40件を確認し、StandardWorkflowへ通常手順を集約 |
| D06 | 判定/時間条件を分離・GPU状態処理を準備 | 製品Runtimeへ検証UI/Activity/旧互換名が混在 | GPU状態処理は未接続。実Editor確認後のadapter接続、assembly/Android境界、GUID/JNI/scene互換検証後に移動 |
| D07 | 未解決 | 同一Unity WindowでWebView直接IMEが出ない | 直接経路または条件付き代理接続の採用判断。必要な実機試験を別途限定 |
| D08 | 観測改善・実機未解決 | IME後のレイ非表示の原因が混在 | nativeTrial等のgateと実入力復帰不良をログ・実観察で区別 |
| D09 | 起動基盤追加・実Editor未確認 | Unity Editorでの継続compile/import検証がない | EditorValidationの共通コマンドを追加。実Editorでのimport/compile・EditMode成功を確認してから完了とする |
| D10 | 共通化済み・実環境未確認 | probeごとのlauncherにADB/設定処理が重複 | 共通ADB解決/接続確認とlaunch-host/launch-activityへ統合。旧引数・Intent extrasを合成ADBで確認。実機実行は停止中 |
| D11 | 未確定 | GLES/Vulkanを製品対応として扱う範囲 | 実機の描画・色・寿命・性能条件を確定。GLESの実装済みと動作確認済みを分ける |
| D12 | 未着手 | 既存BrowserSessionと実backendが未接続 | backendの成立後に一本の寿命・要求経路へ接続 |
| D13 | 上位モデル実装・Unity表示未接続 | Browser UIの状態とSession/表示所有権の境界 | 任意sampleのtab管理と表示借用bindingを純C#で実装。実dispatcher/view/factory/providerへの接続とEditor確認は残る |

## 今回実行するローカル検証

取得器について、0バイト、対象タグ欠落、ADB接続不可、採取失敗、属性不一致、boot不一致、旧capture入口の両方、raw行の保全を検証する。端末操作は合成ADB fixtureに限定する。既存パッケージのGUID・JSON・配布物の整合性も確認する。

HC2の判定と観測を切り出し、抑止・gaze・Cancel条件を維持する。Android/native、AAR/.soとprovenanceは保持する。ローカル検証からQuestやUnity Editorの成功を追加しない。

Editor確認待ちの間に、自動開始/Unity keyboard timeoutの時間条件をHostTrialTimingへ分離した。既存3秒待ち・readiness・一度だけの取り出し・20秒上限を維持する。これはtrial action/host寿命の全面分離ではない。launcherも共通化し、旧run入口はPythonの共通経路へ委譲する。

GPU資源については[GpuLifetime.md](GpuLifetime.md)の世代/退役ledgerを実装・合成検証した。既存のList/Texture/pumpへの接続はまだ行っていない。state成功からnative fence完了や実Textureの安全な解放を確認済みとは呼ばない。

## 実機試験を再開する前の条件

- 統合済みmainのCLIへ揃え、古い一時CLIや修正ブランチ由来のものと取り違えない。
- collectorが必要な診断行の欠落を検出し、試行後に情報不足が判明する頻度を減らす。
- 一試験ごとの判断対象・終了条件・失敗時の次の判断・必要観察を文書化する。
- 診断コード自身が止めているレイと、本当の復帰不良を区別できるようにする。

このフェーズでは新しい実機試行を依頼しない。過去のコマンドや「Next」記述は再開指示にしない。


D06以降の具体的な分離順序と資源所有権は[RuntimeSeparation.md](RuntimeSeparation.md)。main統合は完了したが、Unity/Androidの責務分離やIME/レイの問題まで解消した意味ではない。


実Editorの検証起動と成功判定は[EditorValidation.md](EditorValidation.md)。合成実行ファイルのテスト成功と、実Unityのテスト成功を区別する。
