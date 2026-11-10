> 最新の直接入力修正は[WF1](WindowImeGuard.md)。以下のHI/FO1記録は履歴で、修正手順はWF1を使用する。

# HI4: Unity / WebViewのフォーカス診断

> **現在は整理フェーズで実機試験を停止しています。** 本書は過去の検証手順です。現在の判断は[CurrentStatus.md](CurrentStatus.md)、共通の取得仕様は[Diagnostics.md](Diagnostics.md)を参照してください。以下の「次の試験」は現在の実行依頼ではありません。

HI4はWindow復帰時の限定的なfocus復元に加え、手入力resetの再同期と編集接続後のIME表示要求を含む。キーボード表示の修正完了を意味しない。描画backendはGX2のまま、入力表示が`input=HI4`になる。

## これまでのQuest実機結果

HI1で表示方向、ピンチによる動画の再生/停止、スクロールが成功した。入力欄を選ぶとキーボードは見えずレイが消える。Questのシステムメニューは手で操作できる。選択後は`handAim R=True/False`、`viewFocus=false`、`windowFocus=true`、`immActive=false`、`inputConnections=8/2`。Unity framesの進行は未報告。

`handAim`の後半は物理追跡そのものではなく、Computed/Valid/SystemGestureも検査したアプリ用rayの利用可否。`8/2`は接続作成要求/非null応答の累計で、現在の接続が生きているという意味ではない。WindowがfocusedでもWebView自体にfocusがなければ正常な入力開始条件は揃わない。View focusが外れた先とイベント順序を調べる。

## 実機手順

1. Unityを閉じて追加パッチを適用する。既存のGPU/Vulkan XR Probeシーンを開き、Pointer Mode=Auto、Flip Image Vertically=Onを維持する。Vulkan/ARM64等は変更せず再ビルド・インストールする。
2. `input=HI4`を確認する。Restore ON / Hand sync ONのまま、復帰タイマーを押さずに入力欄を選んでログを記録する。キーボードとrayの実際の動きを確認する。rayが戻るがIMEが出ない場合は下部IMEも一度押す。Hand sync OFFとの比較は必要な場合にプロセスを再起動して行う。
3. `focusOwner`、`focusMove`、`appFocus`、`paused`、右手の`enabled / rawTracked / flags`を見る。ログではviewのidentityを付け、同じクラスの別viewも区別する。
4. アプリを起動し直し、下部の **Focus +5s** を押してから5秒以内に入力欄を選ぶ。5秒後にキーボードを閉じ、WebViewのfocusを外し、生成時に保存した元のViewへfocusを返す。手のrayが使えなくてもAndroid UI Handlerで実行する。
5. 5秒後にrayが戻るか、もう一度ページを操作できるかを見る。これは明示的にarmした時だけ動く復帰実験で、通常のIME表示を自動で中断しない。元focusがnull/既にdetachしている場合、そのViewへの復帰はできないこともログで判別する。

タイマーは再度押すと5秒へ再設定され、Focusボタン・host close時には解除される。closeされたhostへ遅延アクセスしない。SystemGesture/無効なAimの除外と追跡復帰時の押下抑制は維持する。Input Systemのbackground behaviorは変更しない。

## 画面とログの意味

- `focusOwner`: Activity.getCurrentFocus()のViewクラスとidentity。DOMのactiveElementとは異なる。
- `focusMove`: Android View treeの直近のglobal focus遷移。`returns`はこのprobeが明示的にfocusを返した回数。
- `imeRequests`: probeからのIME要求の回数（manualとfocusRecovery）。WebView/Chromium内部の要求回数は含めない。`imeAccepted`は最後の要求の戻り値であり実表示の証明ではない。
- `touch`: 最後にdispatchしたDown=0 / Up=1 / Cancel=3。Moveの連続ログは抑制する。
- `appFocus / paused / focus/pauseEvents`: Unity側のfocus、休止、callback回数。pauseで既存仕様どおりhostを閉じた場合もログが残る。
- `R enabled`: Input System deviceのenabled。`rawTracked`: MetaAimHandのisTracked値。`flags`: MetaAimFlagsの16進値（Computed=0x1、Valid=0x2、SystemGesture=0x40）。
- `background`: device.canRunInBackground。enabled=falseとUnityのfocus喪失が同時ならInput Systemの一時無効化を調べる。flags不成立やrawTracked=falseとの区別ができる。

フォーカス・接続・非Moveのtouch遷移は`VRWebViewFocus`タグへ記録する。Unity側も`[VRWebViewFocus]`を付けてcallbackと手状態の変化を記録する。入力本文、hint、周辺文字列、URL、EditorInfo/接続オブジェクトは記録・保持しない。AndroidのIMM状態は描画ループで観測し、InputConnection生成callbackからIMMへ再入しない。

## WSLからログを保存

Windows Unity付属adbを使用する。以下を実行してからアプリを操作し、終わったらCtrl+C。保存先は利用者指定のDownloads。タグとprefixで今回の診断行だけを保存する。`rg`が未導入なら先にWSLへripgrepを導入する。

```bash
(
set -euo pipefail
adb_win="/mnt/c/Program Files/Unity/Hub/Editor/6000.4.5f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
log_file=/mnt/c/Users/tmiyoshi/Downloads/VRWebView-focus.log
"$adb_win" devices
"$adb_win" logcat -v threadtime -T 1 -s VRWebViewFocus:I Unity:I '*:S' \
  | rg --line-buffered 'VRWebViewFocus' \
  | tee "$log_file"
)
```

## 検証の境界

Android libraryのcompile/lintとAARの整合、既存のC#基盤テストを実行する。これらはUnity EditorコンパイルやQuestでのfocus/IME動作の保証にはならない。特に移動先Viewとcallback順序は実機ログが必要。Javaのlistener/timerはclose時に解除・無効化する。native GPU処理、manifest、XR設定、WebViewのInputConnection本体は変更しない。

## HI2実機ログで確認したこととHI3の変更

利用者の2026-11-07ログでは、00:27:50.646に非null接続とIMM active、00:27:50.845にUnity focus喪失、00:27:50.970に復帰、その直後にProbeWebViewからGameActivitySurfaceViewへView focusが移りIMM inactiveとなった。00:27:54.889〜55.285にも同じ流れが再現した。これらの期間にpause/host closeはなくUnity framesは進行。host closeは後の00:28:00.092。どのコードがsurfaceへrequestFocusしたか、別WindowがIMEだったかはこのログだけでは未確定。

HI3ではページへのDownまたは明示IME要求をbrowser所有の意図として記録する。WebViewがfocusを持った状態でWindow focusを失い、Windowが戻ったときだけUI callbackの後へ復元をpostする。既にWebViewがfocusを持てば処理不要。試行は明示操作ごとに最大1回。新しいWindow cycle、操作、Restore OFF、Focus、closeは古いticketを無効化し、無限のIME/focus往復を防ぐ。showSoftInputやrestartInputを自動で繰り返さない。

**Restore ON/OFF**はその場の比較用ボタン。InspectorのRestore Browser Focusが初期値（既定オン）。ログに`focusRecovery enabled=...`、実際の復元では`focusRecovery request=true/false`とその時点のownerが出る。request=trueだけではIME表示の証明ではない。

手入力はHI2でenabled=TrueでもrawTracked=Falseが観測された。ただし0.5秒間隔の観測なので、短いdisable/resetを見逃した可能性がある。HI3ではInput SystemのMetaAimHand device change（SoftReset/Disabled/Enabled等）をイベントで記録し、XRHandSubsystem自身のrightHand/leftHand.isTrackedも`roots R=... L=...`として併記する。XR Hands 1.7.3のMetaAimHandはisTrackedやaimFlagsを内部の前回値との差分で更新するため、Input Systemのreset後の再同期は候補だが、実機のroot状態とresetイベントが揃うまではSDK不具合と断定しない。入力ポリシーやSDK private fields、追跡validityゲートには手を加えない。

確認するのは、Restore ONでWebView focus/IMM接続が維持されるか、実際にIMEが表示・入力できるか、レイの復帰、OFFとの差である。画面の転記は不要で、同じlogcatコマンドとDownloadsのログを使う。

## HI3比較ログの結果

2026-11-07利用者ログの最初のプロセスはRestore OFF、次がON。OFFではGameActivitySurfaceViewへ戻ったまま。ONでは00:54:03.023にfocusRecovery request=true、非null接続6/2が作成され、.025にviewFocus/windowFocus/immActiveが全てtrueとなった。ただし利用者はキーボード非表示・ray消失が同じと報告。フォーカス復元の成功はキーボード表示成功ではない。

両方でMetaAimHandのSoftReset/Disabled→SoftReset/Enabledが記録され、復帰後もXRHandSubsystem roots=Trueに対してMetaAimHand.rawTracked=Falseが続く。これは物理追跡が失われたという説明ではなく、Input SystemのresetとXR Hands側の差分更新キャッシュの整合を調べる根拠となる。

## HI4: 手resetの再同期

XR Hands 1.7.3のMetaAimHand.UpdateHandは追跡・flags・pinchの前回値を保持し、変化分だけをInput Systemへqueueする。Input System 1.19.0のSoftReset/HardResetはcontrolを初期化するがSDK側の前回値を初期化しない。この状態では次の本物のsampleが同値だと追跡true等を再送しない。

このprobeに限定し、AndroidでMetaAimHandのreset通知を受けた時だけ、public UpdateHandへuntracked/flags None/pinch zeroのreset sampleを渡す。次の本物のprovider sampleが状態を再送できるようにする。SDKのprivate fieldsやソースには触れない。SDKは通常UpdateHandの直接呼出しを推奨しておらず、これは確認した1.7.3のreset経路向けの限定互換処理である。通常のpose/追跡sampleは引き続きSDKだけが供給し、追跡trueやValidを捏造せず、background policyも変えない。SDK更新時はこの互換処理を再評価する。

**Hand sync ON/OFF**ボタンとInspectorのSynchronize Hand Reset（既定オン）で比較できる。ログにhandResetSync sample=resetが出る。reset対象が現在の操作元なら押下もcancelし、復帰時の押しっぱなしを新しいクリックにしない。実際のray復帰はQuestで確認が必要。

## HI4: 編集接続後のIME要求

focus復元後100msに、同じfocus世代が有効で、View/Window focus・attachment・直近の非null編集接続が揃う場合だけshowSoftInputを1回送る。再試行ループはない。新しい操作、Window喪失、Focus、close、Restore OFFで古い要求は無効になる。

`imeRequest reason=manual/focusRecovery accepted=...`はAndroidが要求を受けたか。`imeResult code`はResultReceiverの応答（0=UNCHANGED_SHOWN、1=UNCHANGED_HIDDEN、2=SHOWN、3=HIDDEN）。いずれもQuest内で実際に見えた・文字入力できた証明にはならない。ResultReceiverが返らない場合もある。Bundleや入力本文はログへ出さない。

起動時の`imeCapabilities`はインストール済みアプリのoverlay_keyboard宣言、Activity focusaware、固定asset内のxr-keyboard-overlay-enabled=0/1、Android SDK、WebView provider、window flags/softInputModeを記録する。`defaultIme`は設定されたIMEのcomponent名。これらは環境メタデータであり、入力内容は読み出さない。boot configが別配置等で読めない場合はunavailableとなり、設定欠落とは断定できない。
