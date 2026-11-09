# WF1: 既知WebViewのWindowフォーカス喪失によるIME終了を回避する

更新: 2026-11-09 JST / preview.35。これは実機providerの解析に基づく限定的な互換回避実装。Android libraryの実装とローカル検証を、Questでの修正成功と区別する。

## 根拠

FO1でWindow喪失callback中にclient hide → inputType0/null接続が発生し、その後Window復帰でGameActivityがUnityのSurfaceViewへViewフォーカスを戻した。先行するIME終了をUnityのViewフォーカス取得だけでは説明できない。

利用者から取得した com.android.webview / 154.0.0.55.15.1096209767 / versionCode570701335 のDEXに、Window喪失時の無条件resetAndHideKeyboard呼び出しを確認した。難読化後のonWindowFocusChanged27 → s39 → k99 → t47（restartInput）がFO1の実行stackとも一致する。Native Windowの別Activity検証では、キーボード表示中もWebView所有Windowはfocus=trueで、Unity側appFocus=falseだった。Native EditTextとの比較とは別の証拠。

上流の[導入](https://github.com/chromium/chromium/commit/f04844f7e01375c586b4ea223e6aa133026cd805)と[修正](https://github.com/chromium/chromium/commit/37d5228c9e8b9c9def67a000c4e0a2bdd0b35bff)を参照。上流修正はfocusableなシステムdialog/overlayによる即時IME終了を修正している。実機fork全体のコミット同定ではなく、該当分岐の確認である。

## 変更する動作

既存GpuWebViewProbeに Test Ime Window Focus Guard（既定ON）を追加した。既存シーンでもこのモードが優先され、IP1/IP2の借用・Native EditText・focus復旧再試行を使用しない。固定ページとGPU経路を使い、WebViewの本来のInputConnectionをそのままAndroidへ返す。

ProbeWebViewは次の条件が揃う一回のWindow喪失通知を、providerのsuper.onWindowFocusChangedへ渡すのを保留する。

- 実機解析済みのprovider名・versionNameに完全一致。
- owner Activityがresumedで、Windowが直前までfocused。
- 明示IME要求または実際のpointer downから2秒以内。
- WebViewのViewフォーカスがあり、attach済み・非null編集接続・inputType!=0。
- IMMでそのWebViewがservedかつisAcceptingText。

2秒は最初の喪失を保留できる条件の有効期限であり、入力時間の上限ではない。入力中の自動終了timerやIME再要求は追加しない。Androidの実際のhasWindowFocus、Unity/XR focus、IMMのserved判定を書き換えず、falseはfalseのまま記録する。通知保留なので、通常のWebView契約を完全に保持する一般解ではない。実機結果をもって採否を決める互換回避である。

既にfocusedなrootへattachすると最初のWindow=true callbackが来ない場合があるため、attach後の実hasWindowFocusを初期状態として使う。架空のtrue callbackは送らない。

## 解放と復帰

- Viewフォーカス喪失、Activity pause/stop、Window非表示、detach、Back、Focusボタン、reload、host close、guard OFFで、保留していた実際のfalse通知を一回だけ渡す。
- Unityが入力を失ったときのpointer cancelは、既に保留中のIMEを閉じない。未使用の表示意図だけを解除する。
- 保留中に実Window=trueが戻り、Viewフォーカスが残る場合は、providerはfalseを受け取っていないため重複trueを追加しない。
- GameActivityがWindow復帰時に先にWebViewのViewフォーカスを外す場合は、保留falseを解放して通常のView喪失を通知し、その後の実Window=trueを渡す。
- 元のInputConnectionを代理Viewへ移さず、DOMへ文字列・選択・変換状態を書き戻さない。Done/Backの動作は元のWebView/システムを使用する。

WF1の実機system記録で移動先はQuestのFocusPlaceholderActivityと判明したが、Meta内部の判断理由は未特定。この実装は「喪失がキーボード由来である」と完全に判別するものではなく、上記の狭い条件で適用する。システムメニュー、バックグラウンド、再開、連続編集も確認が必要。

## 一回目の実機確認

既存のIP2/FO1で使ったシーンをそのまま一度Build And Runする。新しいシーン生成、graphics API変更、boot設定変更は不要。画面のWF1とIME guard ONを確認する。IP2 Start/Proxy/Native IMEはこのモードのUIに出さない。

起動前から次を実行する。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-WF1-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect window-ime --label WF1 --log-dir "$vrwebview_logs"
```

1. Text欄を一度選ぶ。キーボードが表示・維持されるかを見る。表示されなければ5秒ほどで追加試行を止める。
2. 入力できれば架空の短い日本語を変換・確定し、DoneまたはBackで閉じる。Unityのレイが戻り、スクロールまたは動画ボタンを操作できるかを見る。
3. 成功した場合だけもう一度Text欄を選び、開閉を繰り返せるか確認する。確認後、システムメニューを開いてアプリへ戻れるかを見る。
4. Ctrl+Cで採取を止める。Downloadsの当該フォルダーをZIPにし、キーボード/入力/復帰の実際の結果と送る。スクリーンの診断値を転記する必要はない。

IME guard OFFは同じAPKでの対照用で、初回からON/OFFの総当たりを要求しない。別providerのsupported=falseが出た場合、一般化して適用せず、そのログで止める。

## 診断と検証範囲

probe=WF1のinstalled、armed、windowのactual/action、releasedのreason、lifecycle、固定ページのDOMイベント数を記録する。入力本文・周辺文字・選択内容は取得しない。HOLD_LOSSやDOMイベント数だけでHMD表示・正しい日本語編集の成功とは判定しない。

window-ime採取では従来タグに加えmain/system/eventsバッファーのWindowManager、InputDispatcher、input_focusを保存する。端末がその情報を出す場合だけ移動先を調べられる。タグ追加で非公開Meta内部の分岐まで必ず分かるわけではない。システムイベント不在を採取失敗や移動なしと決めつけない。

純Javaの状態テストは、初期callback欠落、要求期限、非対象provider、実編集・served条件、重複通知、長時間編集、GameActivityのView喪失先行、pause/再開/解除を検査する。Android compile/lintとR8によるJNI保存、Pythonの診断テストを実行する。Unity Editorでの型コンパイル・APKビルド・Quest実行はクラウドでは行っていない。


## WF1実機結果と、入力終了後の動画タップへの対応

2026-11-09 14:04–14:05 JSTのWF1では、利用者がIME表示とWebViewへの入力を確認した。Window喪失HOLD_LOSS中にDOM inputが0→35、compositionendが0→5へ進み、5回のWindow復帰で保留falseを解放した。これは以前の「候補だけでDOM入力0」から改善した実機結果であり、任意サイト・全編集操作の対応保証ではない。

新しいsystemログは、IME表示に伴いUnityPlayerGameActivityからcom.oculus.vrshell.FocusPlaceholderActivityへAndroidの入力フォーカスが移り、IME終了後にUnityへ戻ることを記録している。移動先は判明したが、Meta内部でこのWindowを選ぶポリシーの実装までは同定していない。

利用者は動画を押すと再生と同時にIMEも開くと報告した。14:05:15.140 touch-up→15.144 WebView自身のshowSoftInput→15.541 video再生状態。前の入力欄はDOM上editable=trueのままで、この時点にprobeのmanual IME要求はない。正確なtap対象・座標を旧ログは記録していないため、Native動画controlと別Playボタンのどちらかを断定しない。

preview.35は管理下の検証HTMLへ、実際のtrusted pointerdownで「対象が編集可能な入力欄・textarea・contenteditable・それらのlabelか」を判定する処理を追加した。対象が動画や通常buttonなどの場合だけ、残っているDOM editorをblurする。入力中のフォーカスと入力欄間の移動を保持し、preventDefault/stopPropagationを使わず、動画再生のユーザー操作をそのまま通す。入力本文・選択・変換状態を書き換える処理はない。

これはこの検証ページの操作方針で、任意サイトへのJavaScript自動注入ではない。Window guardの判定条件、provider制限、InputConnection、GPUコードは変更しない。probe=WF1 event=domにpointerPolicy=1、pointerTarget（editor/media/other）、pointerDown/editorPointerDown/nonEditorPointerDown/blurredEditorsの数値を追加した。実際のelement ID/本文/URLは記録しない。これで旧APKの区別と、動画側で古い編集が解除されたかを確認する。

更新後の確認は同じシーンで入力→Done/Back→動画内controlとPlay/Pauseを操作し、IMEが再表示されないかを見る。次に入力欄を選んだときはIMEが開くことも確認する。採取コマンドは上記と同じwindow-imeで、labelをWF1-pointerとしてよい。pointerPolicy=1がなければ新しいページの証拠がないと判断する。

追加のブラウザー検証は`python3 Tools/check_probe_pointer_focus.py --browser /usr/bin/chromium`。開発用にPlaywrightとChromiumがある環境だけで実行する。実touch・capture段階でのblur、入力欄間/label/contenteditable、native動画control位置、動画実再生、checkbox既定動作、未信頼イベントの除外を確認する。これはLinux ChromiumのDOM/再生検証であり、QuestのIME再表示防止を実機確認したものではない。


ネイティブ動画controlではDOM pointerdownがdocumentへ届かない場合がある。そのため、実際のWebView ViewフォーカスがUnityへ戻り、所有Windowがfocusedのときにも、固定ページへ編集終了を通知する。ページはdocument.hasFocus()がfalseで古いeditorが残るときだけblurする。新しい編集が既に始まっている場合は解除しない。IME表示中のWindow喪失だけではこの終了処理を呼ばない。endedEditorsとediting_end clearedで後始末を区別する。この対策も管理下ページの明示的な編集終了方針で、任意サイトへの自動注入にはしない。

Native controlのpointerイベントがdocumentへ届かない場合、pointerDownは増えずpointerTargetは直前のDOMイベントの値を維持する。この値を全てのnativeタップ対象と解釈せず、native touchログとカウンター増分を合わせて読む。

DOMのdocumentFocusedも数値以外のboolean診断として記録する。ブラウザー検証ではPlaywrightの背景tabへのfocusエミュレーションを無効にし、実際のフォーカス喪失で編集終了を検査する。背景tabではrequestAnimationFrameが停止し得るため、状態待ちは描画に依存させない。

## preview.35の実機確認完了

2026-11-09 16:05–16:07 JSTの利用者ログと報告でIME・動画操作が成功した（E15）。3回の編集終了後にediting_end cleared=true、endedEditors=3。blurredEditors=0なのでDOM pointer判定だけで直ったとはしない。入力終了後の動画操作ではIME再表示がなく、次の編集で再び表示され、動画も継続した。このWF1基本ループを再試験の前提に戻さない。

preview.36はこの知見を製品hostへ接続する実装で、固定ページの関数呼び出しを標準DOMによる一般的な編集終了処理へ置き換える。新hostの実機確認は[RT1](AndroidBackend.md)で行う。
