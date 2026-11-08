# 現在の状態と整理フェーズ

更新: 2026-11-08 JST。現在は情報整理・設計見直し・技術的負債の解消フェーズ。追加のQuest試験、HC2の再試行、AG3のレイ試験は停止している。この更新のためにUnityを再ビルドする必要はない。

## 現時点の判断

製品用のVR WebViewバックエンドはまだ成立していない。描画・IME・Unityの入力を別々の構成で確認できたことと、利用者が一つのVR画面を通常操作できることを区別する。

- 通常閲覧はUnityが操作主体となる。IME表示中はシステムへ入力を渡し、終了後にUnity操作へ戻ることを要求する。
- 別Activityを常時前面に置く方式は、現在の製品主候補から外す。描画・IMEの比較資料として残す。
- 第一候補は同じUnity Activity内のWebView直接入力。代理InputConnectionは成立条件を調べる候補であり、採用済みの設計ではない。
- EditTextの全文をDOMへ反映する旧方式は、汎用編集の主方式へ戻さない。
- AHardwareBufferとVulkan/GLES consumerの境界は再利用候補。現在のプローブ全体を製品APIに昇格させない。

## 確認できた範囲

| 対象 | 状態 | 限界 |
|---|---|---|
| H0/H1の通常Activity | 動画・音声・日本語入力を利用者が確認 | Unity/XR統合の証明ではない |
| H1のWindow draw OFF | スクロール・入力・動画・音声を利用者が確認 | attach/VISIBLEを保った構成。detachやWindow除去の成功とは異なる |
| VirtualDisplay V1 | 動画・音声は成功、日本語入力は失敗 | 製品IME方式として未成立 |
| 同じUnity ActivityのGPU表示 | Vulkan描画、手による動画操作・スクロールを確認 | WebViewへの直接システムIMEは未成立 |
| 同じUnity ActivityのEditText | キーボード実表示とUnity画面更新を確認 | ブラウザーの標準編集へ接続できる証明ではない |
| 別ActivityのWebView | Unity画面との同時表示、日本語変換を確認 | 前面Windowが通常のUnity操作を妨げ、製品主方式として不採用 |
| 手のreset同期 | HC1復帰後のレイを一度確認 | XR Hands 1.7.3向け実験。全ライフサイクルの保証ではない |
| HC2 OFF | 指定順序で2番native・4番native-webinfoにキーボード表示、入力後レイ非表示と利用者が報告 | 属性値一致、実APKのboot値、レイ抑止理由の診断行が欠落 |
| HC2 ONとの比較 | 未確認 | OFFの結果だけでboot設定仮説を終了しない |
| GLES consumer | 実装とローカル検証あり | Questでの同等性・対応保証は未確定 |

証拠の種類・ログhash・未確認事項は[証拠台帳](EvidenceLedger.md)を参照。

## 現在のコードをどう扱うか

`Runtime/Core`は状態・要求・座標等の基礎実装。`IBrowserBackend`の製品用Android実装はまだ存在せず、Coreと現在のGPU/IMEプローブは接続されていない。`Runtime/UnityUI`は入力アダプタの基礎。`Runtime/Android`、`Runtime/VulkanProbe`、`Android~`の各Probeは検証実装を含む。

この境界を明示することが今回の整理であり、Unityの参照・GUID・既存シーンを壊すファイル移動はまだ行っていない。

## このフェーズの成果と残作業

- 現在情報の入口を本書へ一本化し、過去の「次の試験」指示を凍結する。
- [設計の見直し](ArchitectureReview.md)で、実装済み・提案・保留を分ける。
- [技術的負債台帳](TechnicalDebt.md)で完了条件を定める。
- [実装分離の具体計画](RuntimeSeparation.md)で、現行メソッド・資源所有者・変更順序を対応付ける。
- ログ採取は共通の`Tools/probe.py`へ集約。従来の二つのcaptureシェルは互換入口として委譲する。[診断仕様](Diagnostics.md)を参照。
- patch-exchangeのWSL修正はmainへ統合済み。[統合結果](FoundationIntegration.md)と[標準手順](StandardWorkflow.md)を参照。新しいmain版CLIで既存ジョブのresumeも確認した。

実機試験を再開する条件は、ログの取得・失敗検出がローカルで検証され、配布CLIの版が統一され、次の一試験が決める設計判断と打ち切り条件が明文化されていること。残るIMEやレイの問題を「解消済み」と宣言しない。
