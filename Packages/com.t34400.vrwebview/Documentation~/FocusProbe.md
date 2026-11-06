# HI2: Unity / WebViewのフォーカス診断

この追加は原因を特定するための診断であり、キーボード表示の修正完了を意味しない。描画backendはGX2のまま、入力表示が`input=HI2`になる。

## これまでのQuest実機結果

HI1で表示方向、ピンチによる動画の再生/停止、スクロールが成功した。入力欄を選ぶとキーボードは見えずレイが消える。Questのシステムメニューは手で操作できる。選択後は`handAim R=True/False`、`viewFocus=false`、`windowFocus=true`、`immActive=false`、`inputConnections=8/2`。Unity framesの進行は未報告。

`handAim`の後半は物理追跡そのものではなく、Computed/Valid/SystemGestureも検査したアプリ用rayの利用可否。`8/2`は接続作成要求/非null応答の累計で、現在の接続が生きているという意味ではない。WindowがfocusedでもWebView自体にfocusがなければ正常な入力開始条件は揃わない。View focusが外れた先とイベント順序を調べる。

## 実機手順

1. Unityを閉じて追加パッチを適用する。既存のGPU/Vulkan XR Probeシーンを開き、Pointer Mode=Auto、Flip Image Vertically=Onを維持する。Vulkan/ARM64等は変更せず再ビルド・インストールする。
2. `input=HI2`を確認する。まず復帰タイマーを押さずに入力欄を選び、表示とログを記録する。
3. `focusOwner`、`focusMove`、`appFocus`、`paused`、右手の`enabled / rawTracked / flags`を見る。ログではviewのidentityを付け、同じクラスの別viewも区別する。
4. アプリを起動し直し、下部の **Focus +5s** を押してから5秒以内に入力欄を選ぶ。5秒後にキーボードを閉じ、WebViewのfocusを外し、生成時に保存した元のViewへfocusを返す。手のrayが使えなくてもAndroid UI Handlerで実行する。
5. 5秒後にrayが戻るか、もう一度ページを操作できるかを見る。これは明示的にarmした時だけ動く復帰実験で、通常のIME表示を自動で中断しない。元focusがnull/既にdetachしている場合、そのViewへの復帰はできないこともログで判別する。

タイマーは再度押すと5秒へ再設定され、Focusボタン・host close時には解除される。closeされたhostへ遅延アクセスしない。SystemGesture/無効なAimの除外と追跡復帰時の押下抑制は維持する。Input Systemのbackground behaviorは変更しない。

## 画面とログの意味

- `focusOwner`: Activity.getCurrentFocus()のViewクラスとidentity。DOMのactiveElementとは異なる。
- `focusMove`: Android View treeの直近のglobal focus遷移。`returns`はこのprobeが明示的にfocusを返した回数。
- `imeRequests`: probeのIMEボタンによる明示要求の回数。WebView/Chromium内部の要求回数は含めない。`imeAccepted`も明示要求の戻り値であり実表示の証明ではない。
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

Android libraryのcompile/lintとAARの整合、既存のC#基盤テストを実行する。これらはUnity EditorコンパイルやQuestでのfocus/IME動作の保証にはならない。特に移動先Viewとcallback順序は実機ログが必要。Javaのlistener/timerはclose時に解除する。native GPU処理、manifest、XR設定、WebViewのInputConnection本体は変更しない。
