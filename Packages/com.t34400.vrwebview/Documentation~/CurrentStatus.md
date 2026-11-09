# 現在の状態と整理フェーズ

更新: 2026-11-09 JST。WF1の実機でIME表示・WebViewへの入力が成立した。現在は入力終了後の動画操作でIMEが再表示される問題へ対応し、preview.35で検証ページの非編集対象タップ時に残った編集フォーカスを解除した。新しい修正効果は実機未確認。[WF1](WindowImeGuard.md)の同じシーンと採取手順を使う。IP1/IP2の借用は不採用。

## 現時点の判断

製品用のVR WebViewバックエンドはまだ成立していない。描画・IME・Unityの入力を別々の構成で確認できたことと、利用者が一つのVR画面を通常操作できることを区別する。

- 通常閲覧はUnityが操作主体となる。IME表示中はシステムへ入力を渡し、終了後にUnity操作へ戻ることを要求する。
- 別Activityを常時前面に置く方式は、現在の製品主候補から外す。描画・IMEの比較資料として残す。
- 第一候補は同じUnity Activity内のWebView直接入力。IP1/IP2の代理InputConnectionは、接続や候補表示があってもDOM編集が成立せず不採用。
- EditTextの全文をDOMへ反映する旧方式は、汎用編集の主方式へ戻さない。
- AHardwareBufferとVulkan/GLES consumerの境界は再利用候補。現在のプローブ全体を製品APIに昇格させない。

## 確認できた範囲

| 対象 | 状態 | 限界 |
|---|---|---|
| H0/H1の通常Activity | 動画・音声・日本語入力を利用者が確認 | Unity/XR統合の証明ではない |
| H1のWindow draw OFF | スクロール・入力・動画・音声を利用者が確認 | attach/VISIBLEを保った構成。detachやWindow除去の成功とは異なる |
| VirtualDisplay V1 | 動画・音声は成功、日本語入力は失敗 | 製品IME方式として未成立 |
| 同じUnity ActivityのGPU表示 | Vulkan描画、手による動画操作・スクロールを確認 | WF1で実入力を確認。動画操作時の不要な再表示は追加修正の確認待ち |
| 同じUnity ActivityのEditText | キーボード実表示とUnity画面更新を確認 | ブラウザーの標準編集へ接続できる証明ではない |
| 別ActivityのWebView | Unity画面との同時表示、日本語変換を確認 | 前面Windowが通常のUnity操作を妨げ、製品主方式として不採用 |
| 手のreset同期 | HC1復帰後のレイを一度確認 | XR Hands 1.7.3向け実験。全ライフサイクルの保証ではない |
| HC2 OFF | 指定順序で2番native・4番native-webinfoにキーボード表示、入力後レイ非表示と利用者が報告 | 属性値一致、実APKのboot値、レイ抑止理由の診断行が欠落 |
| HC2 ONとの比較 | 未確認 | OFFの結果だけでboot設定仮説を終了しない |
| GLES consumer | 実装とローカル検証あり | Questでの同等性・対応保証は未確定 |

証拠の種類・ログhash・未確認事項は[証拠台帳](EvidenceLedger.md)を参照。

## 現在のコードをどう扱うか

`Runtime/Core`は状態・要求・座標等の基礎実装。`IBrowserBackend`の製品用Android実装はまだ存在せず、Coreと現在のGPU/IMEプローブは接続されていない。`Runtime/UnityUI`は入力アダプタの基礎。`Runtime/Android`、`Runtime/VulkanProbe`、`Android~`の各Probeは検証実装を含む。

HC2の判定/観測はHostPointerObservation、自動開始とUnity keyboard timeoutの時間条件はHostTrialTimingへ切り出した。Coreとプローブのbackend接続、UI/GPU/hostの分離はまだ完了していない。Unityの参照・GUID・既存シーンを壊すファイル移動は行っていない。

## 次に判断するAndroid側の前提

FO1と実機providerのDEX解析で、Window喪失→WebView内部reset/hide→null接続→Window復帰→GameActivityのUnity View取得という順序を確認した。UnityへのViewフォーカス移動が先行するIME終了を起こしたわけではない。

[WF1](WindowImeGuard.md)は、実機解析済みprovider・表示意図の直後・有効なWebView編集接続に限定し、Window喪失のprovider通知を保留する互換回避。通常のpause/非表示/Viewフォーカス喪失/終了では解放する。実際のWindow/XRフォーカスは偽装しない。別Activity移管やエンジン変更は今回実装していない。

IP2はキーボードを維持できたがDOM編集は成立せず、Native EditText対照だけは編集できた。動画のRange修正はFO1で後続streamのskip/read成功とページ側の旧停止位置超えを確認した。動画の全HMDピクセル確認とは区別する。

WF1のsystem記録で、IME表示中の入力先がQuestのFocusPlaceholderActivityへ移ることを確認した。Meta内部のポリシー実装は未特定。入力中の回避自体は実機で成立したが、入力終了後の非編集操作について追加修正を確認する。製品Android backendへの接続はIME・復帰の成立後とする。

## このフェーズの成果と残作業

- 現在情報の入口を本書へ一本化し、過去の「次の試験」指示を凍結する。
- [設計の見直し](ArchitectureReview.md)で、実装済み・提案・保留を分ける。
- [技術的負債台帳](TechnicalDebt.md)で完了条件を定める。
- [実装分離](RuntimeSeparation.md)の最初の判定/観測を実装し、従来の抑止条件を保持する。
- [GPU寿命](GpuLifetime.md)の世代/退役状態処理を実装・合成検証した。既存Probe/Pumpには未接続で、Texture破棄の動作は変更していない。
- [上位レイヤー](UpperLayers.md)の表示借用契約とBrowserWorkspace/BrowserTabを実装した。サンプルモデルは既存BrowserSessionへ接続している。製品Android backendには未接続。
- [Unity表示部品](UnityView.md)としてdispatcher、Texture公開口、RawImage view、pointer/scroll接続を実装した。実Unity 6000.4.5f1でEditMode 14/14、修正後PlayMode 3/3と終了コード0を確認した。WF1旧版の実APK動作は利用者ログあり。今回preview.35の実Editor compile/buildは未確認。実Android backendからTexture/入力を供給する接続は未実装。
- [Browser UI sample](BrowserUI.md)にタブ列・URL欄・Go/New/Closeとモデル/表示の接続を実装した。実EditorでのUI確認と製品factoryは未確認/未実装。未設定時にfakeページを表示しない。
- [レイヤー設計](LayeredArchitecture.md)を記録し、新しい純C# assemblyの依存方向を静的検査する。package全体のXR依存除去は未完了。
- [Editor検証](EditorValidation.md)を共通ツールから起動できるようにした。利用者ログでEditMode全14件、PlayMode全3件とプロセス正常終了を確認した。importしたsampleは保留。前回の終了停滞の根本原因は未確定。
- ログ採取と起動は共通の`Tools/probe.py`へ集約。従来のcapture/runシェルは互換入口として委譲する。全case/modeの引数と失敗経路を合成ADBで検証し、実機起動は未確認。[診断仕様](Diagnostics.md)を参照。
- patch-exchangeのWSL修正はmainへ統合済み。[統合結果](FoundationIntegration.md)と[標準手順](StandardWorkflow.md)を参照。新しいmain版CLIで既存ジョブのresumeも確認した。

実機試験の再開条件として、ログの取得・失敗検出、配布CLIの統一、一試験の判断と打ち切り条件を定めた。現在はWF1だけを対象とし、元のHC2/AG3の再開指示にはしない。残るIMEやレイの問題を「解消済み」と宣言しない。
