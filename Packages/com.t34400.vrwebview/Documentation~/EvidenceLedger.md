# 証拠台帳

2026-11-08 JST。利用者の実機観察と提供ログを整理した記録。クラウドがHMDの実表示を独立に確認したという意味ではない。数値の一致、APIの返り値、実表示、正常操作を別の証拠として扱う。

機械可読版は[EvidenceLedger.json](EvidenceLedger.json)。source_log_sha256は受領したログの識別用で、ログ本文や入力文字はここへ複製しない。hashが空の項目にも利用者報告はあるが、ここでは対応する生ログhashを付与していない。

## E01 — Standalone H0/H1/V1

根拠: `user_report`。

確認したこと: H0/H1の動画・音声・日本語入力が成功／H1のTARGET動画も動いていた／V1の動画・音声は成功、日本語入力は失敗。

限界: Unityと同一ActivityでのIME成立を示さない。

## E02 — H1 Window draw OFF after scroll transform fix

根拠: `user_report`。

確認したこと: スクロール・入力・動画・音声を確認。

限界: VISIBLE/attachを保った構成。Window除去・detach・GONE化の成功とは異なる。

## E03 — Unity Vulkan/GPU probe, hand input

根拠: `user_report`。

確認したこと: 上下反転修正後、ピンチで動画再生停止・スクロールが可能／入力欄選択でキーボードは出ずレイ消失。

限界: 現在構成での製品IMEは未成立／GLESの実機成功へ一般化しない。

## E04 — HC1 same Unity GameActivity, standard EditText

根拠: `user_report_and_log`。

確認したこと: システムキーボードが実表示／表示中もUnity画面が更新。

限界: 日本語編集の詳細は別途未確認／終了直後にはnativeTrialによるレイ抑止が残り得る。

ログSHA-256: `6015188b412f9ff4ab843fa82a7b318f3c4733a26ebfcaedb14dd63d93dc583a`。

## E05 — HC1 same Unity GameActivity, normal-window WebView

根拠: `user_report_and_log`。

確認したこと: キーボード非表示／Window focus喪失後、WebViewがserved viewではないとしてIME要求を拒否。

限界: 後の試行では非null接続・accepted=trueでも実表示なし。API成功を表示成功としない。

ログSHA-256: `1b9e61faf173d62bddbcb432a8eb229d0f0a0523d2391b389dc4be3982ed4a2d`。

## E06 — HC1 separate opaque/translucent Activity

根拠: `user_report_and_log`。

確認したこと: Unity VR画面とネイティブWebView/keyboardの同時表示／半透明Activityで日本語変換を確認。

限界: 通常のUnity入力が利用できる証明ではない／自動returnは検証timerによるものを含む／詳細な編集全項目は未確認。

## E07 — HC1 preview.13 hand reset sync

根拠: `user_report_and_log`。

確認したこと: 復帰後のレイ表示を利用者が確認／ログでMetaAimのrawTracked復帰とUI選択を確認。

限界: XR Hands 1.7.3向けの版依存実験／全ケース・全SDKの復帰保証ではない。

ログSHA-256: `96090d0b5e6a2bdbfa9c132ea6f8cb93ffd837879eb7f0b413db731b01deed5a`。

## E08 — AG1 separate Activity GPU transfer

根拠: `user_report_and_log`。

確認したこと: GPU描画とIME起動を確認／ネイティブWindow位置でシステムレイが遮られる。

限界: 通常Unity操作の成立を示さない／この構成の動画・音声・日本語全条件は別途未確認。

ログSHA-256: `a48ca0ab3bd372d96e005a963fa34f832df5910e13662277e5b5eec8c28225d6`。

## E09 — AG2 texture / FLAG_NOT_TOUCHABLE passthrough

根拠: `user_report_and_log`。

確認したこと: 両方で白いシステムレイがネイティブ領域に遮られると報告／textureではXR FOCUSEDと新鮮な手関節データを取得した区間がある／passthroughではXR VISIBLEのまま入力を再開できていない。

限界: Unityレイでの実操作は未成立／2構成のfocus結果を同一扱いしない。

ログSHA-256: `f7257de074b0eb63df8700791df617375656d517e0864b7494386bf8d61be623`。

## E10 — HC2 OFF: keyboard → native → webview → native-webinfo

根拠: `user_confirmed_order_and_filtered_log`。

確認したこと: 順序とoff指定を利用者が明示確認／2番と4番でキーボード表示、入力後レイ非表示を報告／1回目にはIME show失敗とUnity timeout／2回目・4回目の入力後にXR 4→5を確認。

限界: HC2/bootCheck/nativeConnection/editorProfile等の行が欠落／実属性値一致とAPK内boot値のログ確認は未完了／nativeTrialの20秒抑止と持続的な復帰不良を判別できない／ONとの比較は未確認／保持ログ回収は0バイトで失敗。原因は未確定。

ログSHA-256: `bca16eca58b32ae0b6c1d5eed09ba5410acad58e90155bc14f1b2825d59ba29e`。

## 公開仕様・仮説の位置付け

- [Android Viewの公開代理入力契約](https://github.com/aosp-mirror/platform_frameworks_base/blob/299fe6f5d6fc6f1af7c3411dcf4e5efdf7217368/core/java/android/view/View.java#L16522)とInputMethodManagerの判定は、代理接続を調査する根拠。QuestでWebView接続を維持できる証明ではない。
- [Chromium 128のフォーカスとIME処理](https://github.com/chromium/chromium/blob/128.0.6613.84/content/public/android/java/src/org/chromium/content/browser/input/ImeAdapterImpl.java#L846)は一般的な実装の参照。実機のprovider 154系の非公開変更まで同一とみなさない。
- [OpenXRのsession focus仕様](https://github.com/KhronosGroup/OpenXR-Docs/blob/18ebc0374da23c020ef4c949f9b144ffae6160ef/specification/sources/chapters/session.adoc#L574)は入力権限と可視性を区別する。Android IMEの実表示を保証しない。
- `xr-keyboard-overlay-enabled`の欠落はUnity OpenXR 1.16.1の公開build hookとの比較から見つかった設定候補。HC2 ON/OFFの結論はまだ出ていない。

## 最新の解釈

E10の試行順を再度疑わない。報告どおりのnative-webinfo成功は入力属性だけを原因とする説明への反証候補になるが、属性値の診断確認は未完了。レイについても、XR focus復帰・検証gate・Input System・実表示を分けて判定する。

追加試験は停止中。既存の観察を「未確認」に戻して何度も同じ操作を依頼せず、足りない証拠を特定してから再開判断をする。

## E15 — WF1 preview.35: 入力・終了後の動画操作

利用者がIMEと動画再生の成功を確認。3回の編集でinput25/compositionend6、Window保留/解放、editing_end cleared=trueを記録した。endedEditors=3、blurredEditors=0なので、効果は実View喪失後の編集終了処理を含めて説明する。入力終了後の動画操作でIMEが再表示されず、再編集と動画再生も併存。動画buffer0:8、frame1036、error0。

ログSHA-256: `211913fb4b73f55e6e4f2e055c6a900774b38d2d568743b9d36f6a936bb16c3b`。この構成の基本ループは確認済み。任意サイト・GLES・全lifecycleや、preview.36の製品経路を確認した証拠にはしない。

## E16 — RT1基本動作とhover不足

利用者はCreate Runtime Browser SceneのRT1について「動作に問題はなさそう」、pointer overは動いていなさそうと報告した。これは製品経路の基本動作に関する利用者の実機観察。今回添付されたZIPはE15のWF1ログと同じSHA256なので、新しいRT1のログ証拠としては使わない。preview.38のhover追加はその後の変更で、まだ実機未確認。

## E17 — RT1 preview.38 hover

hover追加後、利用者が実機で「ちゃんと動いているようです」と確認した。RT1の基本動作（E16）に加えてhoverも利用者報告で確認済みとする。新しいログはなく、全サイト・desktop mouse button/hover menu・全lifecycleへ一般化しない。preview.39のversion条件変更とは別の観察。


## E18 — BS1 preview.40: 高さ配分とUnity URL IMEの失敗

利用者はページが高さの約1/3と報告。rowのflexibleHeight継承による281/281/349pxというコード上の概算と一致。URL欄クリック後、IME側show失敗→約0.4秒のUnity timeoutが5回。raw log SHA256998fd5edd0da634aa70469f383f9e9edbeb112f9a4dcea83d38d7365312c82dc。

## E19 — BS1 preview.41: UI改善と表示前のURL入力失敗

利用者はUI改善を確認したが、IMEは動作しないと報告。添付ZIP内の最新22:39:00開始ログはui_ready version41、5host、bootKeyboard1/overlayDeclaredtrueを確認。URLへのDown9/Up9、addressFocusTrue0、address_keyboard_request0、Unity keyboard timeout0。Native editing_endは記録されるが、待ちのどの条件で開始が中断したかは旧ログで未観測。設定OFFだけでは今回を説明できず、OSへshowを要求した後の拒否としても扱わない。

2windowと複数tabのsnapshot、表示中targetのコピーと非表示target0のstats21行も記録。UI改善の報告と整合するが、全drag/IME/media/負荷を確認済みにはしない。最新raw459263bytes/4142rows、SHA256f960401825115a18d0d76cd680edf4134a19767438774caf9b3783bf35a1ca19。前の2captureは現在のruntime marker/hostを欠くので、新方式の失敗根拠へ混ぜない。

preview.42は明示選択・main-threadの編集終了待ち・中断理由を追加。対策の実機効果はまだ未確認。


## E20 — BS1 preview.42: 移動は可能、ray表示とURL clickが未成立

利用者は移動可能と報告し、画面の手前のrayが見えず奥側は見える、判定が不明でURL入力も開けないと報告。最新raw574360bytes/4839rows、SHA256274fa8338a90aa07b3e16882ccd208b0aa5c0ae4548feecac28e277a1128dfea。ui_ready version42、実boot1/overlayDeclaredtrue、addressDown2/Up1、prepare/request/abort/timeoutすべて0。

addressDownの2回はUpでnone/pageへ移り、addressUpの1回はDownがother。URLのclick成立を示す組がないため、今回をIME側のshow拒否や編集終了待ち失敗としない。ray描画は従来Rendererのorder0、window Canvasはorder1以上で、UIに隠れる可能性がある設定だった。視覚的な根因の断定ではなく、preview43で描画契約とヒットcueを明示して修正する。

preview43は高いorder/Overlay shader/同一world hitのmarker、Graphic geometryに対するphysical ray判定、URLのDownでの編集開始を実装。新しい実Unity/shader/Quest結果は未確認。


## E21 — BS1 preview.43: ヒット修正を確認、Unity URL keyboardはIME側showで失敗

利用者がhit修正を確認。角の判定が小さく姿勢が変わらないため改善を要望した。latest raw 583311bytes/5253rows、SHA2562147e897d4a624f70a9bb49a97e76a820aeac490e412b594f7f3e57e98d3ff56。prepare2/request2、Unity timeout2、boot1/overlayDeclaredtrueを3行で確認。2回ともGameActivitySurfaceViewのshow後にIMEがPHASE_IME_ON_SHOW_SOFT_INPUT_TRUEで失敗した。最新の失敗は選択以前の中断ではない。Unity標準入力の不採用を示すものではなく、[U1対照](UnityAddressInputInvestigation.md)を追加して調査を続ける。preview.44の角/回転/対照実装はQuest未確認。


## E22 — U1 stock InputFieldとIME履歴

標準InputFieldのみでも利用者はキーボード非表示と報告。raw255195bytes/2182rows、SHA256276f803a18aa9925519f218452ac9219bb439d7be08fc632b1cf6f767e989281。Unity6000.4.5f1、Default/Standard、hideInputFalse、inPlaceFalse、4回のIME側show拒否とUnity timeout、text_changed0。UnityのvisibleTrueは実表示と一致しない。

追加input_method dump86281bytes/1208rows、SHA2566eb6daaa207fd8fae38f00c9c276b5f856a075d551f09cad9003233aab6b54c0。現在状態はhomeなのでU1の現EditorInfoとして使わず、logcatと一致するStartInput/ShowHide/Tracker履歴を使用。U1の4回とBS1の2回すべて、show時type0から1–2ms後に0x80001/imeOptions0x2000006へ更新。入力準備前のshowという順序競合を支持するが、Quest内部の拒否条件/責任箇所/修正効果までは未確定。preview.45のU2対照は実機未確認。


## E23 — U2 preview.45: 単発追加showで表示、確定後の文字列は不一致

raw537639bytes/4427rows、SHA25652a0b3c97c684957f3b735e6e368ef673c38749958d76c974de6b2984ba93bc9。追加show4回すべてtype0x80001で要求し、IME onStartInputView/onWindowShownを確認。初回の標準Openは4回ともTYPE_NULLで拒否。追加要求後の文字入力変更は13/14/21/0回（計48）。最初の3試行はDone後のApplication focus復帰もログにあるが、物理rayの見え方までログから確定しない。

利用者は入力できた一方、変換確定後やDoneで閉じた後もシステムキーボードとInputFieldが違うと明示。表示順序仮説は支持されるが、データ整合/完全なIME対応は未成立。既存ログは文字列比較を持たず、どの境界で違うかは未判明。preview.46に既存U2の読み取り専用比較を追加した。


## E24 — P46の値観測と採取されたGoogle GameActivity4.4.0

P46 raw484013bytes、SHA256cdee06aee4988ada48dd9fcceb773e1241b10f6cf073dbc0ee4929428fdaf933。103stable観測でfieldEqualsKeyboard/labelIsFieldSliceがすべてTrue、Done後も一致。利用者はそれでも、既存文字だけキーボード内に見え、追加入力は候補とInputFieldにだけ出ると確認。通常ブラウザでは問題なし。ラベルの42対31文字分という表示範囲だけでは説明できない。

追加採取ZIP22879bytes、SHA2562818ec2db80d0a92a38a3fb362f69ae7f1be11232682d745fc6d88d34230ae1f。生成Gradle宣言とキャッシュはgames-activity4.4.0、13クラスを採取し全hash一致。getExtractedTextのsuper委譲と全文更新通知の不在、showSoftInput→restartInputの順序を確認。参考4.4.2でも全文抽出の点は同じ。最終APK DEXの依存解決やMeta IMEの要求APIまでは未観測。

preview.47は既存接続の全文取得/監視通知を補う診断。post process・確認済みAARの生成/照合・解除を実装した。GameActivityImeCompatibility.mdに手順と制限を記載。実機成功はまだ記録していない。
