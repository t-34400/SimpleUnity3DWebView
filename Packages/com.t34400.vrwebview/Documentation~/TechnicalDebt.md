# 技術的負債と解消条件

履歴表の基準は2026-11-08 JST。現在の入口はCurrentStatus.md。旧IP1等の試験指示は再開指示ではない。最新追記: 2026-11-09。

| ID | 状態 | 課題 | 解消条件 |
|---|---|---|---|
| D01 | 今回対応 | READMEに複数世代の「次の試験」が混在 | CurrentStatusを入口にし、旧試行手順へ凍結表示 |
| D02 | 今回対応 | captureごとのタグ/フィルターで必要なログが消える | 共通collectorへ委譲。別プローブの行も保存し、対象不一致を明示 |
| D03 | 今回対応 | 空ファイルやADBエラーを採取完了と誤認 | 接続確認、raw log/summary/stderr保存、欠落・0バイトを非0終了で報告 |
| D04 | 今回対応 | 観察・ログ・仮説・実装予定が混在 | EvidenceLedgerで区別。OpenXR focus/API値から実表示を推定しない |
| D05 | main統合済み | WSL consumer CLIとmainの機能が分裂 | fc65deaをmainへ反映済み。全277件とmain上の互換40件を確認し、StandardWorkflowへ通常手順を集約 |
| D06 | 判定/時間条件を分離・GPU状態処理を準備 | 製品Runtimeへ検証UI/Activity/旧互換名が混在 | GPU状態処理は未接続。実Editor確認後のadapter接続、assembly/Android境界、GUID/JNI/scene互換検証後に移動 |
| D07 | IP1代理接続の検証実装・実機未確認 | 同一Unity WindowでWebView直接IMEが出ない | IP1一往復でWebView本来のconnection生存を判断。API acceptedではなく日本語実編集とUnity操作復帰を要求 |
| D08 | 観測改善・実機未解決 | IME後のレイ非表示の原因が混在 | nativeTrial等のgateと実入力復帰不良をログ・実観察で区別 |
| D09 | 実EditMode/PlayMode全成功・IP1追加分は未確認 | Unity Editorでの継続compile/import検証がない | 実EditMode 14/14・PlayMode 3/3と終了コード0を確認。IP1追加menu/sceneの実compile/buildは残る。任意sample検証は保留 |
| D10 | 共通化済み・実環境未確認 | probeごとのlauncherにADB/設定処理が重複 | 共通ADB解決/接続確認とlaunch-host/launch-activityへ統合。旧引数・Intent extrasを合成ADBで確認。実機実行は停止中 |
| D11 | 未確定 | GLES/Vulkanを製品対応として扱う範囲 | 実機の描画・色・寿命・性能条件を確定。GLESの実装済みと動作確認済みを分ける |
| D12 | 未着手 | 既存BrowserSessionと実backendが未接続 | backendの成立後に一本の寿命・要求経路へ接続 |
| D13 | model・Unity部品・基本UI sampleを実装 | Browser UIの状態とSession/表示所有権の境界 | tab/URL UI、draft保護とpage寿命を実装。実Android factory/producer/sinkとの接続、実Editor確認、history等のUI機能は残る |

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

現在の次の実機試行は[RichBrowserSample.md](RichBrowserSample.md)のBS1。IP1/IP2は不採用とし、RT1基本動作とhoverは確認済み。過去のHC2/AG3のコマンドや「Next」記述は再開指示にしない。


D06以降の具体的な分離順序と資源所有権は[RuntimeSeparation.md](RuntimeSeparation.md)。main統合は完了したが、Unity/Androidの責務分離やIME/レイの問題まで解消した意味ではない。


実Editorの検証起動と成功判定は[EditorValidation.md](EditorValidation.md)。合成実行ファイルのテスト成功と、実Unityのテスト成功を区別する。

## 今後のタスク（利用者メモ、2026-11-09）

現在進行中のBS1リッチサンプル統合テストの準備と実機確認を先に進める。以下は保留タスクであり、今回完了したものではない。

- JavaScriptの実行などWebView操作のinterfaceを整える。公開操作・非同期結果・失敗・取消と寿命の契約を整理する。公開JS実行APIは未実装。
- 配布形式を整える。
  - パッケージを整理し、Core機能とSamplesの切り分けを決める。
  - 検証用の機能を通常の配布パッケージで露出させない。配布除外・別Development package等の具体方式は検討する。現状の本体から検証コード/XR必須依存を分離し、最小consumerへの導入を確認する。
  - READMEは他パッケージを参考に、利用者が必要な導入・対応範囲・最小の使い方に絞る。開発/検証の経緯は別文書へ分ける。
  - 添付画像とYouTubeデモの要否・掲載場所・撮影/編集素材を検討する。素材の用意前に完成したデモとして扱わない。
  - ライセンス変更の要否を検討する。MITの簡潔さとApache 2.0の明示的な特許許諾・再配布条件等を比較し、第三者コードの条件と合わせて判断する。選択は未決定で、現ライセンスを今回変更しない。
  - リリースノートの形式・粒度と、CHANGELOG/タグ/GitHub Releasesの分担を検討する。利用者向けの変更点・移行・既知制約を整理する。

## preview.41 implementation

BrowserDesktop owns sessions across windows; BrowserDesktopPanel/WindowView add common browser controls, tab reorder/detach/dock and planar window move/resize. Fixed row flexible heights. Android pages now outlive transient output queues; only visible bindings allocate producer Surface/AImageReader/RenderTexture. Switching creates a new generation and retires the old one after producer/GPU acknowledgement. Physical target pooling is not implemented. WebView media/background execution policy is unchanged by presentation demand.

Unity address keyboard startup now awaits page editing relinquishment, uses explicit boot ON in the scene generator, checks stale OFF at build, reads packaged boot/manifest metadata and reports keyboard timeouts. Root cause resolution and all new integration behaviors remain pending Unity/Quest validation. Existing RT1/WF1 user confirmations are historical evidence, not a preview.41 regression pass.

Public navigation observations/commands are added; JavaScript execution API, production package diagnostic exclusion, README/media/license/release distribution work remain pending. See RichBrowserSample.md for the current short device trial; do not resume old probe matrices.
