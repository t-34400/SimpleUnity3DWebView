# 現在の状態と整理フェーズ

更新: 2026-11-09 JST。preview.35のWF1で、利用者がIME入力と入力終了後の動画操作の成功を確認した（E15）。この構成の基本ループは確認済み。preview.36では同じ方式を使う製品用Android backend、共有render pump、BrowserController/BrowserView、Browser sample factoryを接続した。preview.37までのRT1基本動作は利用者が確認した（E16）。hoverはpreview.38で追加し、利用者が実機動作を確認した（E17）。preview.39ではIME回避策のversion完全一致条件を外し、同じproviderの更新後も保持する。preview.40ではリッチサンプル用BS1シーン生成・uGUI入力接続・採取を追加した。次の入口は[BS1手順](RichBrowserSample.md)。実Editor/Questは未確認で、旧比較試験の総当たりへ戻らない。


2026-11-10追記: preview.43のhit修正を利用者が確認（E21）。URL IMEは実boot1でもUnity GameActivityのshowで失敗し未解決。標準InputFieldを維持し、preview.44にWebViewなしの[U1対照](UnityAddressInputInvestigation.md)を追加した。角の拡大と[姿勢変更](SpatialWindowInteraction.md)も実装し、実Quest効果は未確認。


preview.45: U1の標準InputFieldでも同じIME表示拒否を確認。BS1/U1の6回でTYPE_NULLのshowが文字入力type更新に先行した。原因確定とはせず、[U2](UnityInputOrderProbe.md)に同じ入力接続への単発追加show対照を実装した。標準入力を維持し、製品への回避策は追加していない。実Quest結果は未確認。


preview.46: U2でキーボード表示・入力が可能になったが、確定/Done後の内容不一致が利用者から報告された（E23）。表示の順序対処と、文字列同期の成立を分ける。既存U2にreadonlyのfield/keyboard/label比較を追加し、[値の観測手順](UnityInputOrderProbe.md)で調べる。製品への入力方式切替や全文同期は追加していない。

## 現時点の判断

製品用Android backendを実装し、Core/Unity/UIへ接続した。WF1はE15のログと報告、RT1基本動作はE16の利用者報告で確認。hoverはE17で利用者が確認。version条件の変更は実機観察と分けて扱う。

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

[WF1](WindowImeGuard.md)は、対象provider名・表示意図の直後・有効なWebView編集接続に限定し、Window喪失のprovider通知を保留する互換回避。通常のpause/非表示/Viewフォーカス喪失/終了では解放する。実際のWindow/XRフォーカスは偽装しない。別Activity移管やエンジン変更は今回実装していない。

IP2はキーボードを維持できたがDOM編集は成立せず、Native EditText対照だけは編集できた。動画のRange修正はFO1で後続streamのskip/read成功とページ側の旧停止位置超えを確認した。動画の全HMDピクセル確認とは区別する。

WF1のsystem記録で、IME表示中の入力先がQuestのFocusPlaceholderActivityへ移ることを確認した。Meta内部のポリシー実装は未特定。preview.35で入力中の回避と終了後の動画操作が成立した。製品backendへの接続を実装し、任意ページの編集終了は一般DOM操作へ置き換えた。RT1基本動作はE16で確認し、hoverもE17で利用者が確認済み。

## このフェーズの成果と残作業

- 現在情報の入口を本書へ一本化し、過去の「次の試験」指示を凍結する。
- [設計の見直し](ArchitectureReview.md)で、実装済み・提案・保留を分ける。
- [技術的負債台帳](TechnicalDebt.md)で完了条件を定める。
- [実装分離](RuntimeSeparation.md)の最初の判定/観測を実装し、従来の抑止条件を保持する。
- [GPU寿命](GpuLifetime.md)の世代/退役状態処理を実装・合成検証した。既存Probe/Pumpには未接続で、Texture破棄の動作は変更していない。
- [上位レイヤー](UpperLayers.md)の表示借用契約とBrowserWorkspace/BrowserTabを実装した。サンプルモデルは既存BrowserSessionへ接続している。製品Android backendとsample factoryへ接続済み。RT1基本動作はE16、Browser sample factoryの実UI確認は保留。
- [Unity表示部品](UnityView.md)としてdispatcher、Texture公開口、RawImage view、pointer/scroll接続を実装した。実Unity 6000.4.5f1でEditMode 14/14、修正後PlayMode 3/3と終了コード0を確認した。WF1旧版の実APK動作は利用者ログあり。preview.35の実APK/Quest結果はE15。preview.36でAndroidからTexture/入力を供給する接続を実装し、preview.37のRT1基本動作はE16で利用者が確認。hoverはE17で実機確認済み。
- [Browser UI sample](BrowserUI.md)にタブ列・URL欄・Go/New/Closeとモデル/表示の接続を実装した。実EditorでのUI確認は未実施。AndroidBrowserPageFactoryを追加した。未設定時にfakeページを表示しない。
- [レイヤー設計](LayeredArchitecture.md)を記録し、新しい純C# assemblyの依存方向を静的検査する。package全体のXR依存除去は未完了。
- [Editor検証](EditorValidation.md)を共通ツールから起動できるようにした。利用者ログでEditMode全14件、PlayMode全3件とプロセス正常終了を確認した。importしたsampleは保留。前回の終了停滞の根本原因は未確定。
- ログ採取と起動は共通の`Tools/probe.py`へ集約。従来のcapture/runシェルは互換入口として委譲する。全case/modeの引数と失敗経路を合成ADBで検証し、実機起動は未確認。[診断仕様](Diagnostics.md)を参照。
- patch-exchangeのWSL修正はmainへ統合済み。[統合結果](FoundationIntegration.md)と[標準手順](StandardWorkflow.md)を参照。新しいmain版CLIで既存ジョブのresumeも確認した。

実機試験の再開条件として、ログの取得・失敗検出、配布CLIの統一、一試験の判断と打ち切り条件を定めた。RT1の基本操作とhoverは確認済みとして扱い、次はBS1でサンプルUI/URL欄/複数ページを確認する。WF1の基本ループは確認済みとして扱い、HC2/AG3へ戻らない。任意サイト・GLES・全lifecycleへ成功を一般化しない。

今後の利用者メモは[TechnicalDebt.md](TechnicalDebt.md)末尾へ記録した。JavaScript実行等のWebView公開操作interfaceと配布形式の整理は、BS1と別の保留タスク。今回完了とは扱わない。

## preview.41 — new integration awaiting device validation

preview.40 BS1 failed Unity address-keyboard display (5 native show failures/~0.4s Unity timeouts); reported page height ~1/3 matches inherited flexible row heights. Fixed heights and prepared boot/ownership diagnostics plus shared multi-window browser sample. Added demand-driven transient presentation queues, navigation state/API and drag reorder/detach/dock. These are new implementations; no Unity type compile or Quest result is claimed. Start with the URL keyboard in RichBrowserSample.md, then continue only if that works.


## preview.42 — spatial chrome and address-input handoff

E19: preview41 UI improved on Quest, but URL keyboard request was not reached despite packaged boot1/overlayDeclaredtrue. Implemented explicit field selection/main-thread handoff diagnostics, blank-header 3D dragging, four hover corner resize handles, integrated tab close/trailing plus/overflow scrollbar and world-ray depth picking. preview41 generated RichBrowser scene can be reused after current sample import. New Unity type/geometry tests and Quest operation remain pending; do not claim the IME issue is resolved.


## preview.43 — visible hit cue and address editing on press

E20: preview42 movement was reported working, but ray visibility prevented confident tapping. Two addressDown events ended at none/page; no edit preparation/request occurred. Added dedicated overlay ray/marker rendering, physical per-graphic hit tests and web-pointer coordinates, activation logs and address editing on Down. Android libraries are byte-identical to preview41/42. Actual Unity C#/shader compilation and Quest interaction remain pending. Reuse the current scene, update the sample, then check the ring and URL keyboard first.
