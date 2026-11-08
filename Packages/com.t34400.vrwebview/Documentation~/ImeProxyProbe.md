# IP2 — 同一Unity ActivityでIME接続を分離する検証

preview.31。主目的は、UnityのVR画面に表示するWebViewで日本語を編集し、入力終了後にUnity操作へ戻れる方式の採否を決めること。動画停止の観測も同じログで採る。製品Android backendへの接続はまだ行わない。

## 現時点の証拠と変更理由

IP1の実機ではhost初期化、WebViewの実InputConnection、代理EditTextへのfocus移譲とshow要求が成立した。しかしキーボードは表示を維持できず、DOM input/compositionは増えていない。利用者は一瞬だけ見える場合もあると報告した。APIのacceptedや接続オブジェクトの存在を成功扱いしない。

22:43の記録では、最初のclient HIDE_SOFT_INPUTがWindow focus喪失とUnity Surfaceへのfocus復帰より先に出た。後から起きたUnity focus復帰だけを原因としない。呼び出し元のstackは旧ログにない。

AOSP Android14のInputMethodManager.isActive(view)はserved ViewのcheckInputConnectionProxyも認める。IP1はこの公開代理関係をWebViewとprovider内部Viewへ許可したため、proxyへfocusを移した後もimmSource=trueだった。参照Chromium128のImeAdapterImplはfocus喪失時にresetAndHideKeyboardし、source ViewがisActiveならhideを要求する。これは競合の根拠だが、実機provider154が同じ経路を通った証明ではない。

## IP2の実装

- 同じProxyEditor・同じWindow・同じGPU hostを使用し、source/providerへのcheckInputConnectionProxyはfalseにする。focus/window通知は偽装せず、再showやfocus回収のループも追加しない。
- WebView本来のInputConnectionを公開interfaceで借用する構造と、getHandlerのthread契約を保持する。wrapperのcloseは借用だけを閉じ、WebView所有のsourceをcloseしない。世代失効後の新しい呼び出しを拒否する。
- **重要な制約:** 代理関係を外すと、Chromiumからsource Viewを指定するselection/session更新も受理されなくなる。また、Chromium自身のfocus喪失によるconnection resetは防げない。この分離だけで汎用IME互換が成立したとはしない。
- proxy-query時の呼び出しstackを、一試行16件×各16frameまで記録する。自前のisActive観測は除外する。class/method/lineだけで、ファイル名、引数、例外message、入力文字は出さない。このhookを通らないhide要求のcallerは分からず、全hide呼び出しを傍受したとはしない。
- 自前のhide要求はapp_hideとして区別する。

## 同じ画面のNative IME対照

IP2 Startの後に失敗した場合だけ、Unityレイを使える状態なら **Native IME** を一度選べる。同じProxyEditorに標準EditTextの接続を使わせ、借用接続と区別する。最新のsourceから得たinputType/imeOptions/privateImeOptionsを同じhost内で合わせるが、EditorInfo全属性を同一にする試験ではない。周辺テキスト・selectionはEditText自身のもので、DOMをコピーしない。source flagsが未取得なら理由を記録して拒否する。

これは対照用の独立した入力欄で、Webページを編集しない。入力内容をDOMへ全文同期する製品方式でもない。通常EditTextの接続作成、text変更件数/composing有無、Doneを記録し、本文はログへ出さない。Done/Backで同じUnity復帰処理を通す。対照の成功をWebView入力の成功と混同しない。

## 一回のビルド・採取

通常のpatch-exchangeで最新パッチを適用する。**現在のIP1シーンをそのまま使用できる。** 同じserialized設定からIP2 Start/Native IMEが作られるのでscene再生成は不要。Editor/Sampleテスト、別Activity、graphics API、keyboard boot設定変更も不要。

採取を先に起動し、UnityでBuild And Runを一度行う。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-IP2-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect ime-video --label IP2 --log-dir "$vrwebview_logs"
```

1. IP2ページを確認し、Play/Pauseで動画を再生して約10秒待つ。途中で止まっても、そこで動画だけの試行を増やさず次へ進む。
2. **IP2 Start** を一度選ぶ。固定Text欄へ実DOM focusを設定して借用する。入力欄を先にtapする必要はない。
3. キーボードが表示を維持できたら、架空の日本語を変換・確定し、Doneで閉じる。Webページ内の文字が変わったか、Unityレイ/操作が戻るか確認する。
4. 借用で表示/編集に失敗した場合だけ、操作可能なら **Native IME** を一度選び、キーボードが維持されるか確認してDone/Backで閉じる。レイが戻らず操作不能なら追加試行をせず採取を終える。
5. Unityを操作でき、動画が止まっていた場合はReloadを一度だけ行い、Play/Pauseで再生する。再び止まったら5秒ほど待つ。
6. WSLでCtrl+C。Downloadsに作られた今回のVRWebView-IP2-日時フォルダーをZIPにし、log/summary/ADB stderrをまとめて送る。

キーボードが出なければ5秒程度でその結果を確定し、Startを連打しない。レイが戻らなければQuestシステムメニューから終了できる。画面の診断ラベルや動画番号の転記は不要。添える観察は、借用でキーボード表示/ページ内日本語編集/Unity復帰ができたか、対照を使った場合はそのキーボードが維持されたかだけ。

## ログと判定

互換性のためログprefixはproxy=IP1を維持するが、mode/ime_requestのvariant=isolatedがIP2の証拠になる。借用要求はeditor=borrowed、対照はeditor=native。proxy_query/ime_call_frame、app_hide、native_connection/native_text/native_doneを追加した。既存のoperation/dom/unity_tickも残す。

動画ログはvideo=bundledで、固定MP4の応答status/start/length、ストリームのread/skip/残量/EOF/close、約1秒ごとのcurrentTime/buffered/seekable/readyState/networkState/イベント件数を記録する。動画は8秒/240フレームで、元データの全decodeはクラウドで確認済み。利用者の報告は実再生がbuffered末尾で止まるもので、185前後は固定境界ではない。readBytesはWebViewへの読み取り量であり、デコード/GPU到達の証明ではない。metadata preloadの取消/close単独で障害と断定しない。

--expect ime-videoはisolated variant、実際の借用show要求、動画応答、ページ内media sampleを必要とする。旧APK、Native IMEだけ、Unity heartbeatだけでは採取成立にしない。欠けていてもrawログは保持する。summaryの成立はキーボード/日本語編集/動画再生の成功判定ではない。

| 結果 | 次の判断 |
|---|---|
| 借用で表示維持・ページ内日本語編集・Done後Unity操作が成功 | IME方式の候補に残す。ただしsource selection/session更新を分離で遮断した課題が残るので、汎用backendへ直結せず接続寿命・選択同期の設計を確認する |
| 対照だけが成功 | 同じhostで標準接続の表示は成立。現在の借用/Chromium連携は未成立と判断し、無条件の再showを追加して延命しない |
| 借用も対照も即座に閉じる | source接続だけでは説明できない。Window/Unity/IME routingと自前hide/caller記録を優先する |
| キーボードは維持されるがDOMが変わらない | 表示条件とChromium接続の生存を分離する。日本語編集の成立とはしない |
| 動画だけが止まる | 同じログの応答消費/再取得/メディア状態で調べる。IMEの方式判断を動画だけの再試行で止めない |

Android AAR compile/lint、純Javaの借用・世代・stream・診断の非干渉、ツール判定、R8 JNI保存をクラウドで検証する。UnityはC#構文検査までで、新しいC#の実Editor型解決/APK build、Android widgetの動作、QuestのIME/日本語編集/復帰・動画の実表示は実機確認待ち。
