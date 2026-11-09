# 現在の状態と整理フェーズ

更新: 2026-11-09 JST。preview.35のWF1で、利用者がIME入力と入力終了後の動画操作の成功を確認した（E15）。この構成の基本ループは確認済み。preview.36では同じ方式を使う製品用Android backend、共有render pump、BrowserController/BrowserView、Browser sample factoryを接続した。preview.37までのRT1基本動作は利用者が確認した（E16）。hover送信は未実装だったためpreview.38で追加したが、追加後の実機確認は未実施。[Android backend](AndroidBackend.md)を次の入口とし、古い比較試験の総当たりへ戻らない。

## 現時点の判断

製品用Android backendを実装し、Core/Unity/UIへ接続した。WF1はE15のログと報告、RT1基本動作はE16の利用者報告で確認。最新hover追加後の結果は別に確認する。

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
| 同じUnity ActivityのGPU表示 | Vulkan描画、手による動画操作・スクロールを確認 | preview.35でIME・動画操作の成功を確認。任意サイトの保証ではない |
| 同じUnity ActivityのEditText | キーボード実表示とUnity画面更新を確認 | ブラウザーの標準編集へ接続できる証明ではない |
| 別ActivityのWebView | Unity画面との同時表示、日本語変換を確認 | 前面Windowが通常のUnity操作を妨げ、製品主方式として不採用 |
| 手のreset同期 | HC1復帰後のレイを一度確認 | XR Hands 1.7.3向け実験。全ライフサイクルの保証ではない |
| HC2 OFF | 指定順序で2番native・4番native-webinfoにキーボード表示、入力後レイ非表示と利用者が報告 | 属性値一致、実APKのboot値、レイ抑止理由の診断行が欠落 |
| HC2 ONとの比較 | 未確認 | OFFの結果だけでboot設定仮説を終了しない |
| GLES consumer | 実装とローカル検証あり | Questでの同等性・対応保証は未確定 |

証拠の種類・ログhash・未確認事項は[証拠台帳](EvidenceLedger.md)を参照。

## 現在のコードをどう扱うか

`AndroidBrowserBackend`がIBrowserBackend、HTML読み込み、pointer/scroll、編集終了と障害通知を実装し、`BrowserTextureSource`を供給する。`BrowserController`が生成・セッション・表示を構成する。Core/Unity/UnityUIは具体Android実装を参照しない。Browser sampleにはAndroidBrowserPageFactoryを追加した。

render pumpとnative宣言はGUIDを保ったままRuntime/Renderingへ移し、既存probesも同じpumpを使う。GPU scene launcherは診断assemblyへ移した。import済み旧IME Sampleとの互換性のため、静的なAndroidImeProbe/AndroidGpuProbe入口は従来assemblyに残す。Android runtime AARには検証ページ/Activityを含めないが、現UPMには別probe AARとXR依存がまだ同梱されており、最終配布分離は未完了。

## 次に判断するAndroid側の前提

FO1と実機providerのDEX解析で、Window喪失→WebView内部reset/hide→null接続→Window復帰→GameActivityのUnity View取得という順序を確認した。UnityへのViewフォーカス移動が先行するIME終了を起こしたわけではない。

[WF1](WindowImeGuard.md)は、実機解析済みprovider・表示意図の直後・有効なWebView編集接続に限定し、Window喪失のprovider通知を保留する互換回避。通常のpause/非表示/Viewフォーカス喪失/終了では解放する。実際のWindow/XRフォーカスは偽装しない。別Activity移管やエンジン変更は今回実装していない。

IP2はキーボードを維持できたがDOM編集は成立せず、Native EditText対照だけは編集できた。動画のRange修正はFO1で後続streamのskip/read成功とページ側の旧停止位置超えを確認した。動画の全HMDピクセル確認とは区別する。

WF1のsystem記録で、IME表示中の入力先がQuestのFocusPlaceholderActivityへ移ることを確認した。Meta内部のポリシー実装は未特定。preview.35で入力中の回避と終了後の動画操作が成立した。製品backendへの接続を実装し、任意ページの編集終了は一般DOM操作へ置き換えた。RT1基本動作はE16で確認し、最新hover追加は同じRT1で確認する。

## このフェーズの成果と残作業

- 現在情報の入口を本書へ一本化し、過去の「次の試験」指示を凍結する。
- [設計の見直し](ArchitectureReview.md)で、実装済み・提案・保留を分ける。
- [技術的負債台帳](TechnicalDebt.md)で完了条件を定める。
- [実装分離](RuntimeSeparation.md)の最初の判定/観測を実装し、従来の抑止条件を保持する。
- [GPU寿命](GpuLifetime.md)の世代/退役状態処理を実装・合成検証した。既存Probe/Pumpには未接続で、Texture破棄の動作は変更していない。
- [上位レイヤー](UpperLayers.md)の表示借用契約とBrowserWorkspace/BrowserTabを実装した。サンプルモデルは既存BrowserSessionへ接続している。製品Android backendとsample factoryへ接続済み。RT1基本動作はE16、Browser sample factoryの実UI確認は保留。
- [Unity表示部品](UnityView.md)としてdispatcher、Texture公開口、RawImage view、pointer/scroll接続を実装した。実Unity 6000.4.5f1でEditMode 14/14、修正後PlayMode 3/3と終了コード0を確認した。WF1旧版の実APK動作は利用者ログあり。preview.35の実APK/Quest結果はE15。preview.36でAndroidからTexture/入力を供給する接続を実装し、preview.37のRT1基本動作はE16で利用者が確認。最新hover追加後の確認は未実施。
- [Browser UI sample](BrowserUI.md)にタブ列・URL欄・Go/New/Closeとモデル/表示の接続を実装した。実EditorでのUI確認は未実施。AndroidBrowserPageFactoryを追加した。未設定時にfakeページを表示しない。
- [レイヤー設計](LayeredArchitecture.md)を記録し、新しい純C# assemblyの依存方向を静的検査する。package全体のXR依存除去は未完了。
- [Editor検証](EditorValidation.md)を共通ツールから起動できるようにした。利用者ログでEditMode全14件、PlayMode全3件とプロセス正常終了を確認した。importしたsampleは保留。前回の終了停滞の根本原因は未確定。
- ログ採取と起動は共通の`Tools/probe.py`へ集約。従来のcapture/runシェルは互換入口として委譲する。全case/modeの引数と失敗経路を合成ADBで検証し、実機起動は未確認。[診断仕様](Diagnostics.md)を参照。
- patch-exchangeのWSL修正はmainへ統合済み。[統合結果](FoundationIntegration.md)と[標準手順](StandardWorkflow.md)を参照。新しいmain版CLIで既存ジョブのresumeも確認した。

実機試験の再開条件として、ログの取得・失敗検出、配布CLIの統一、一試験の判断と打ち切り条件を定めた。次は同じRT1で未押下hoverのEnter/Move/Exitを一回確認する。WF1の基本ループは確認済みとして扱い、HC2/AG3へ戻らない。任意サイト・GLES・全lifecycleへ成功を一般化しない。
