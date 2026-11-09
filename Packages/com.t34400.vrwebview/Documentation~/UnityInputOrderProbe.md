# U2: 標準InputFieldの表示順序対照 — preview.45

U1の4回とBS1の2回で、表示要求に記録されたtypeはTYPE_NULL(0)、その1–2ms後のrestartInputは0x80001だった。U1 logcatでもIMEの表示拒否の後に入力開始通知が来る。順序競合が有力だが、Questの拒否条件そのものや原因箇所は未確定。

U2は同じ標準InputField・TouchScreenKeyboard・GameActivityの入力接続で、通常のOpen後に準備状態を観測し、同じViewへ追加のshowを一回だけ要求する診断。製品のIME対策ではない。U1単独、BS1のURL、WebViewページIMEには追加要求を接続しない。

## 更新とシーン

同じjobs/vrwebview/h1-scroll-001をfetchして通常の追加applyを行う。転送先と元commitは従来どおり。Unityを閉じ、未コミット変更は保全する。Browser sampleの再ImportはU2には不要。

1. Unityで **VRWebView > Create Unity Input Field Order Probe Scene** を選ぶ。従来U1/BS1のassetは削除しない。
2. Unity 6のアクティブBuild ProfileのScene Listが今回のUnityInputFieldOrderProbeであることを確認する。起動画面のタイトルは **U2: InputField / one prepared show**。
3. 従来のGameActivity/OpenXR/Vulkan/ARM64/Requires System Keyboard設定を維持する。生成メニューはkeyboard boot ONを選び、既存U1のbuild guardも適用する。SDK/renderer/entry pointを同時に変更しない。

Editor batchテストは実機試験の前提にしない。

## 一回の試行

WSLでBuild And Run前に開始する。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-U2-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect unity-input-order --label U2 --log-dir "$vrwebview_logs"
```

U2をBuild And Runし、空の入力欄を一度選んで約2秒待つ。何度も連打しない。

- キーボードが出たら日本語を変換・確定し、Done/Backで閉じる。rayが戻るか確認し、同じ欄をもう一度選んで再編集できるか確認する。
- 出なければ、ここで停止して回収する。旧比較の総当たりには戻らない。

試行後、できればUnity画面のままCtrl+Cを押す。captureはlogcatを止めた後、15秒上限で読み取り専用のdumpsys input_methodも同じフォルダへ保存する。現在画面が変わっても履歴には検証時刻が残る場合がある。snapshot失敗も空ファイルでなく診断として記録し、元のlogcatは保持する。

```bash
python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"
```

ZIPと、実表示・日本語変換/確定・終了後のray・再編集の観察を添える。画面内の数値転記は不要。

## 追加要求の条件と限界

- 通常のInputFieldがkeyboard instanceを作った後だけ動く。新しいinstanceごとに一つの試行とし、同じinstanceをフォーカス復帰で再armしない。
- Android UI threadで現在のFocus Viewを取得し、UnityがSurfaceへ割り当てるunitySurfaceView resource IDで対象を確認する。内部クラス名の難読化に依存しない。
- GameActivityの公開getImeEditorInfoでlocal inputType/imeOptionsを読む。stock Openが返った後にだけ呼び、typeを自分で設定しない。Googleのgetterは未初期化ならdefault EditorInfoを生成する仕様なので、これはIME側EditorInfoの直接観測ではない。
- 同一Viewのattached/View focus/Window focus/IMM activeとTYPE_CLASS_TEXTを確認する。50ms以上間隔を空けた二回のUI-thread観測で同じView/type/optionsだった場合だけ、showの権利を一度消費する。読取期限は2秒。固定待ち後に無条件でshowする方式ではないが、IMEが受信済みというACKでもない。
- 追加showのUI-thread実行直前にも同じ条件/identity/type/optionsとキャンセルを再確認する。changed/unavailableならその一回を見送り、それ以上showを繰り返さない。
- 追加要求はInputMethodManager.showSoftInput(existingView, 0)。新規EditText、InputConnectionの代理、明示的restartInput、requestFocus、EditorInfo書換え、全文コピーを行わない。
- フィールド編集終了、選択解除、フォーカス喪失、pause、disable/destroyで待機を取り消す。キーボード表示後にUnity入力フォーカスを奪い返さない。HMD実表示やIME終了をvisibleプロパティだけで判断しない。

U2は標準Openの前後を組み替えるものではなく、最初の失敗後、既存接続への一回の追加要求という対照。成功しても、製品対応に固定待ちや常時再試行をそのまま入れる根拠にはしない。次にUnity/Google nativeの順序と利用可能な修正版を検討する。

## ログの読み方

U1 ready/state/text_changedはstock fieldの診断として残る。U2 ready/armed/observation/show_dispatch/show_result/cancelled/ready_timeoutで追加経路を区別する。observationはlocalInputType/localImeOptions/viewFocus/windowFocus/immActiveを記録し、入力内容を記録しない。

最初のstock Openが既知のUnity timeoutを出しても、U2ではその数をunity_keyboard_timeoutsとunity_order_notesに残して追加showと分ける。初回timeoutだけで追加要求の成否を決めない。show_resultのissued/acceptedは要求の実行/受理であり、HMD実表示・文字編集の成功ではない。

追加showがtype0x80001で記録され表示/入力まで成立すれば、先行するTYPE_NULLでのshowが問題となる仮説を支持する。追加showでも拒否されれば、履歴の実typeとIME側状態を確認する。local getter値だけからサーバーやIMEへ反映済みとは断定しない。

## 配布物とローカル検証

追加コードは診断assemblyに閉じたC#のみ。Google GameActivityの公開getter名をR8で保持するconsumer ruleをprobe AARへ追加した。GradleでAARとLintを実行し、Java class内容/asset/native.soが従来と同じであることを確認。native libraryは再ビルドせず同じものを保持した。

pure C#で単発要求・安定観測・取消・古い応答・timeoutの試験、Pythonで採取/集計の試験を実行する。packaged consumer ruleはR8の合成ABI fixtureでも確認する。Unity型解決/Editor build/Quest実表示・IME動作はクラウドでは未確認。
