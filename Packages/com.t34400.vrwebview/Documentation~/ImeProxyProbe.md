# IP1 — Android GPU + WebView本来のInputConnectionを借用する検証

2026-11-08 JST。Android方式の採否を決める最初の関門。製品backendではない。任意のBrowser UI sampleのimport/テストは、この実機検証の前提ではない。

## この一試験で決めること

同じUnity GameActivity・同じWindowで、代理EditTextをIMEの接続先にした後も、WebViewから借りた本来のInputConnectionが編集を受け付けるか。GPU描画は既存AHardwareBuffer→Vulkan/GLES経路を使用する。別Activity、VirtualDisplay、テキストのDOM全文同期は追加しない。

構造は `Quest IME → ProxyEditor → 借用したWebView InputConnection → Chromium編集処理`。代理EditText自身のEditableはページの文字列に同期しない。実フォーカスを代理Viewへ移す。WebViewのfocus/window通知を偽装しない。

既存の同一Activity直接show要求はキーボード非表示だった。その再試行やHC2の4構成を繰り返さず、この代理接続の成立性だけを確認する。

## 実装した境界

- `ImeProxyProbe`: UI threadでの試行制御。明示的なタップ/Startごとに一度だけcapture/handoffする。Window未復帰なら一度待機するが、show要求は一度だけ。毎frameのfocus回収、5秒return、20秒nativeTrialは使わない。
- `BorrowedInputConnection`: Android公開InputConnectionインターフェースをJavaの公開Proxy/Method.invokeで委譲する。非公開Chromium APIは使わない。getHandlerも委譲し、元の実行thread契約を保持する。UI threadへの同期post/waitへ置換しない。
- lease無効化後の新しい呼び出しはfalse/null等で拒否し、古いwrapperを新しい接続へ付け替えない。既にdelegateへ入った呼び出しの取消を保証するものではない。
- proxyのcloseConnectionはそのwrapperだけを閉じ、借用元をcloseしない。元接続はWebView/Androidの所有物で、フォーカス移動に伴い既に失効している可能性がある。それをこの試験で調べる。
- Done/Back、reload、モード切替、host closeで世代を失効させる。Doneは本来のconnectionへperformEditorActionを渡した後、まだ同じ世代ならUnityへfocusを戻す。finishComposingTextだけで編集を終了しない。
- Proxy ONのときだけ代理Viewを作る。同じActivityの既存panel内に320×72pxの透明なEditTextをattachする。GONEのままIMEを要求しない。通常のGPU sceneはIP1を有効にしない。
- EditorInfoのpublicな基本属性、selection、初期周辺文字を転記する。これは全Android版・全入力種別の完全なEditorInfo互換実装ではない。最初は固定の通常inputだけを対象にする。

`connected`/leaseの`isValid`はオブジェクトの存在と論理世代の有効性だけ。Chromium側の編集接続が生存している証明ではない。delegateのtrue、DOMイベント、利用者の実編集観察を別々に記録する。

## 一回の操作手順

修正版をpatch-exchangeの通常applyで反映した後、Unityで **VRWebView → Create Android IME Proxy Probe Scene** を実行する。シーンはAssets/VRWebViewPlaygroundに生成し、そのシーンだけをBuild Settingsへ入れる。変更中のシーンの保存確認が出る。UPM Samplesのimportは不要。

既存のAndroid/GameActivity、Meta XR、ARM64、VulkanまたはOpenGL ES 3を維持する。keyboard bootのON/OFFやgraphics APIは、この試験と同時に変えない。実際のoverlay宣言/focus awareness/boot値/WebView版は`imeCapabilities`行に残る。

WSLで採取を先に開始する。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-IP1-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect proxy --label IP1 --log-dir "$vrwebview_logs"
```

この指定でC:\Users\tmiyoshi\Downloads内の新しいVRWebView-IP1-日時フォルダーへ、log/summary/ADB stderrの3ファイルをまとめる。保存先は開始時にも表示する。採取中は別ターミナルやUnityを使える。logcatは消去しない。

そのままUnityでBuild And Runを一度行う。HMDでは次だけを確認する。

1. IP1見出しのページと水色のUnityレイが見える。ページのPlay/Pauseで動画を再生する。
2. 下の **IP1 Start** を一度選ぶ。固定ページのTextへ実際のDOM focusを設定し、代理接続へ移譲する。入力欄を先にtapする必要はない。
3. キーボードが出たら、架空の短い日本語を入力し、変換・確定できるか確認する。今回のTextはenterkeyhint=done。
4. キーボードのDoneで閉じる。Unityの水色レイが戻り、Play/Pauseやスクロールを再び操作できるか確認する。
5. WSLの採取をCtrl+Cで止め、今回作られたフォルダーをZIPにする。

キーボードが出なければ5秒程度でその結果を確定し、同じボタンを何度も押さない。レイが戻らなければQuestのシステムメニューからアプリを終了できる。試行を増やさず、そのままログを採取終了する。

添える観察は「キーボード表示」「ページ内の日本語変換・確定」「Done後のUnity操作復帰」の3点だけ。画面の診断ラベルの転記は不要。

## ログと採取判定

既存タグVRWebViewFocusへ`proxy=IP1 event=...`を記録する。

- armed/source_connection/handoff/proxy_focus: 試行世代、sourceとproxyのfocus、入力属性。
- proxy_connection/proxy_relation/ime_request: 代理接続の生成、sourceとの公開代理関係、showのAPI戻り値。
- operation: メソッド名とtrue/false/null/stale/delegate_error。文字列引数、返された文字、EditorInfo本文、例外messageはログへ出さない。
- dom: 固定ページ内のinput/composition/selectionchangeの累積件数とeditableのbool。入力文字列やDOM全文は取得しない。
- unity_tick: Unity frames、application focus/pause、実入力sourceの観測。rayの実表示とは区別する。
- done/cancel: 編集終了と世代無効化。

`--expect proxy`ではIP1行とarmed行を必要とし、旧APKのログやUnity heartbeatだけでは採取成立にしない。APIがaccepted=trueでも、summaryはキーボード/rayの実表示を推定しない。IP1 Start直後にfield_not_readyならページ読込前に操作した証拠であり、代理方式の失敗と混同しない。

## 結果から次に進む基準

| 観察/証拠 | 判断と次の作業 |
|---|---|
| 表示・ページ内の日本語入力・Done後Unity操作がすべて成功 | 代理接続を製品候補に残す。Core/Android/GPUの最小backendへ接続し、その後に選択置換・複数欄・再生成等の寿命検証へ進む |
| キーボードは表示、delegateがfalse/失効、DOM入力も増えない | この構成ではWebView接続の生存条件を満たしていない。focus偽装や非公開APIで延命せず、現在の借用方式を採用しない |
| ページ編集は成功、Done後Unity操作だけ失敗 | IME接続と入力復帰を分離する。採取したunity_tick/手のreset/Window focusを使い、復帰処理を次の対象にする |
| proxy接続が作られてもキーボードは非表示 | 接続オブジェクトの存在を成功としない。Window/IME routingの観測を確認し、製品へは接続しない |
| source_unavailable、field_not_ready、IP1/armed行なし | この試行では方式を評価できない。欠落した前提を特定し、同じ操作の無条件な繰り返しを要求しない |

この試験だけで、任意サイト、contenteditable、次の欄への移動、絵文字、password、最新のhandwriting/非同期IC機能まで対応済みとはしない。前提が成立する前に汎用IME bridgeを作り込まない。

## ローカル検証と限界

公開InputConnection interfaceの借用・失効・closeの所有権・Handler委譲・診断の非干渉は、Androidを模倣しない純Java interfaceの実行テストで確認する。Android AARのcompile/lintと収録クラス・asset・provenanceを確認する。Unity C#は構文検査までで、この新しいmenu/sceneの実Editor compile、Quest上のproxy生存、IME実表示・日本語編集・入力復帰は未確認。

参照したAOSP Android 14のcheckInputConnectionProxyとChromium 128のfocus/接続resetは一般実装の根拠であり、実機provider154/Horizonの完全な仕様ではない。[設計の見直し](ArchitectureReview.md)の採用条件を変えたものではない。

## preview.29 — 代理Viewの初期化失敗への対応

利用者提供の22:12の実機ログでは、Unityの追跡・pinchとProxy ON/OFFの切替を確認した。一方、Android側はmode_change直後、mode/host_startedの前にhostClosedとなり、再生成でも同じ経路だった。WebView/IMEの評価前にhost.startの例外処理へ入ったと判断する。旧実装は例外class/stackをlogcatへ出しておらず、受領ログから例外型とthrow位置は断定できない。

コードにはEditTextのsuperclass構築中にも呼ばれ得るonCheckIsTextEditorから、代入前のownerを参照する欠陥があった。このoverrideを外して標準EditTextの判定を使い、ほかのoverrideもowner代入前は安全に返す。これは実機の例外位置を確認したという意味ではない。

今後はinitializingの段階、host_start_failedのstage/例外class/上位8stack frame、host_started、Startボタン由来のstart_requestedとhost不在のime_request_rejectedを記録する。Throwable messageやcause messageは出さない。失敗したhostは登録から外し、closeで失敗理由を上書きしない。

同じIP1シーンを使用し、まずページ表示とhost_startedを確認してから従来の一往復を行う。scene再生成、EditorテストやサンプルUIテストは追加しない。今回の修正後のAndroid widget構築・Quest表示は実機での確認待ち。
