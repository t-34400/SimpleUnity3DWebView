# HC1: keyboard and Activity host comparison

目的は「Questのキーボード経路が成立しない」のか「GPU用WebViewホスト固有の問題」なのかを切り分け、別ActivityでUnityのVR表示を継続できるかを測ることです。これは製品方式の決定ではありません。

HC1は既存GX2/HI4とは別のシーンです。GPUコピー用WebView、フォーカス強制復元、HI4の手入力リセット補正を開始しません。既存のGPUプローブは残しています。起動方式・Custom Main Manifest・Meta設定の変更もパッチ自体には含みません。

## 準備

1. Unityを閉じてパッチを適用し、Unity 6000.4.5f1で開きます。
2. **VRWebView > Create Host Comparison Scene**。新規シーンを保存し、Build Settingsのシーン一覧をHC1だけにします。元のシーンは削除しません。
3. 最初は現在のGameActivityのまま。ARM64、OpenXR、グラフィックスAPIも前回から変えません。MetaのFocus Aware、Requires System Keyboard、Quest 3対象設定を確認してください。
4. **VRWebView > Host Comparison > Use GameActivity**でビルド前の期待値を記録し、Build And Run。既にGameActivityならManifest/Player Settingsの本文は変更しません。

SDK 207はUnity 2023.2以降でGameActivityを必須にしています。後述のUnityPlayerActivity比較は実験用で、SDKの検証を無効化しません。実行されたActivity名を必ずログで確認します。

## ログ取得と自動試行（WSL）

別のWSL端末で先にログ取得を開始します。保存先は `C:\Users\tmiyoshi\Downloads` です。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
bash Tools/capture_host_probe.sh game
```

もう一つの端末で、次を**1行ずつ**実行します。各回はアプリを再起動し、頭の追跡・XR起動・アプリフォーカスが揃ってから自動で試行します。キーボードが表示された場合は短い日本語の変換・確定を試してください。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
bash Tools/run_host_probe.sh keyboard
# 表示・入力を確認し、キーボードを閉じてから次へ
bash Tools/run_host_probe.sh native
# 約25秒待ってから次へ
bash Tools/run_host_probe.sh webview
# 約25秒待ってから次へ
bash Tools/run_host_probe.sh activity
# 約25秒待ってから次へ
bash Tools/run_host_probe.sh transparent
```

初回は `keyboard` の結果だけでも切り分けに役立ちます。コントローラーは不要です。画面から選ぶ場合はピンチ、手のレイが取得できない場合は頭をボタンへ2秒向けます。一度実行したら視線を外してから再選択してください。

| case | 試す経路 | 戻り方・制約 |
| --- | --- | --- |
| keyboard | Meta公式例の `TouchScreenKeyboard.Open("", TouchScreenKeyboardType.Default)` | Unityループが続く場合は20秒で閉じる。停止した場合はシステムの戻る操作で閉じる |
| native | Unityと同じActivityの標準EditText + IMM | Android UIスレッドで20秒後に除去 |
| webview | Unityと同じActivityの標準Window上WebView + IMM | 自動でHTML入力欄へfocus、20秒後に除去。Window draw ON、GPU転送なし |
| activity | Unityとは別の不透明Activity内のWebView/EditText | HTML入力欄へ自動focus。20秒後にAndroid側でfinish |
| transparent | Unityとは別の半透明Activity内の同じ入力欄 | 同上。透過WindowがUnityのpauseを防ぐとは仮定しない |

同じActivityのネイティブViewはHMDに見えない可能性があります。自動focusとログで確認します。別Activityが2D表示になったりVR表示を置き換えたりすること自体も結果です。別Activityで「EditText / IME」を選べる場合はWebViewとの比較もできます。

ADBパスが異なる場合は `VRWEBVIEW_ADB_EXE`、パッケージIDが異なる場合は `VRWEBVIEW_PACKAGE` を設定します。ログ出力先は `VRWEBVIEW_LOG_DIR` で変更できます。起動スクリプトは標準のUnityPlayerGameActivity/UnityPlayerActivityだけを許可します。

## 記録してほしい結果

画面の文字の書き写しは不要です。ログファイルと、各caseについて以下の観察を送ってください。

- キーボードが実際に見えたか、日本語変換・確定ができたか。
- UnityのVR画面が残ったか、上の回転キューブが動き続けたか。2D画面への切替・暗転も記録。
- 20秒後に戻れたか、戻った後の手のレイが使えるか。

ログは `[VRWebViewHost] HC1` を含み、実際のUnity Activityクラス、ライフサイクル、Window/View/IMM、InputConnection数、Unityフレーム・カメラ描画カウンター、OpenXR状態変更を記録します。Android UI側のwatchdogはUnityが停止してもログを続けます。`unityFrameLast` / `unityRenderLast` / `xrRunningLast` は最後に届いた値で、`heartbeatAgeMs` の増加はUnityからの更新停止を示します。フレーム数やカメラ描画数だけでHMDへの継続提示成功とは判定しません。見えているVR画面の観察も必要です。

`accepted=true` や `immActive=true` はキーボード実表示の証明ではありません。入力文字はログに保存せず、Unityキーボードについては文字数だけ記録します。WebViewのDOMと外部入力欄の文字同期、InputConnectionの独自中継は行いません。

## Unityの起動方式を変える補助比較

これは「別Activityを立ち上げる」caseとは別の実験です。GameActivity側の試行を保存してから実施してください。

1. Unityで **VRWebView > Host Comparison > Use Activity (Experimental)**。
2. Player SettingsとCustom Main Manifestの起動Activityをセットで変更します。VRカテゴリ・keyboard feature・focus aware等は保持します。カスタム起動クラスや複数launcherは自動変換しません。
3. SDKの検証がActivity構成を拒否したら、その時点で比較不可として終了します。Fix All等でGameActivityに戻った場合も、期待値チェックが不一致を検出します。SDKチェックは無効化しないでください。
4. ビルドできた場合はログ取得を `bash Tools/capture_host_probe.sh activity` に変え、同じcaseを試します。ログの `start owner=UnityPlayerActivity` を確認します。
5. **Use GameActivity**で戻します。**Clear Expected Entry Check**は期待値だけを消し、実際の設定を変更しません。

このメニューの切替はManifestを書き換えます。往復後にGit diffで設定を確認してください。現行SDKの製品用構成としてUnityPlayerActivityを推奨するものではありません。

## 結果に応じた判断

- 公式keyboardも失敗：WebView単独の問題と決めず、Unity/Meta/OSの経路・設定を優先して調べる。
- 同じActivityのEditTextは成功、WebViewは失敗：WebViewの編集接続・ホスト条件を調べる。
- 通常Window上WebViewは成功、既存GPUプローブは失敗：Window draw抑制等の差分を調べる。
- 別Activityで入力成功、Unityの更新・描画やVR提示が停止：前面2Dブラウザとしては候補だが、同時にVR空間へWeb画面を表示する方式の成立は未確認。
- 半透明Activityで入力とUnityのVR表示が両立：次段階でそのActivityからのGPU転送・動画・破棄/復帰を検証する。今回のHC1は別ActivityからUnityへのGPU転送を実装しない。

どの結果も、Quest固有の非公開IME連携や他のActivity構成すべての可否を証明するものではありません。
