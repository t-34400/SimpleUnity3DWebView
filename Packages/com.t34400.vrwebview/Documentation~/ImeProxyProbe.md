> 2026-11-09更新: IP1/IP2の借用はDOM編集が成立せず不採用。直接WebViewの終了経路を実機DEXで特定し、現在の回避実装・確認手順は[WF1](WindowImeGuard.md)を参照する。以下は過去の実装・調査記録。

# IP2 — 同一Unity ActivityでIME接続を分離する検証

**preview.32時点: IP2の実機調査は終了し、現在の接続借用方式は不採用。以下のIP2手順を繰り返す必要はない。**

preview.31での主目的は、UnityのVR画面に表示するWebViewで日本語を編集し、入力終了後にUnity操作へ戻れる方式の採否を決めること。動画停止の観測も同じログで採る。製品Android backendへの接続はまだ行わない。

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


## 2026-11-09 03:03 JST — IP2結果と方式判断

Quest 3 / Android SDK34 / provider com.android.webview 154.0.0.55.15.1096209767。利用者は借用でキーボードの表示が維持されるが、キーを押してもキーボード欄もWebViewも変わらず、候補だけ出ると報告した。Native IMEでは入力でき、WebViewが変わらないのは独立対照としての想定どおり。

ログではborrowedのsetComposingTextが15回、commitTextが4回、いずれもtrue。DOMのinput/compositionstart/update/endは0のまま。Native側は7回の文字変更があり、composing=trueからfalseへの変化とDoneを確認した。IMM.showの成否やInputConnectionのtrueを編集完了と混同しない。参照ChromiumのThreadedInputConnectionはUI threadへpostした時点でtrueを返すため、レンダラーでの編集完了を保証しない。

実機のproxy-query stackには、providerのImeAdapterImpl.onWindowFocusChangedからisActive/restartInput、およびupdateSelection/updateExtractedTextへの呼び出しがある。隔離によりこれらはserved sourceとして許可されない。キーボード維持とWebViewの編集・IME状態更新を同時に成立させる契約が、現在の借用方式にはない。現在のIP1/IP2を製品backendへ採用せず、show再試行や代理関係のON/OFFだけを増やす調査は打ち切る。実機providerのnative内部の全状態を特定できたという意味ではない。

次の設計は、レンダラーの編集focus、AndroidのIME用View、selection/extracted-text更新を正式なAPIで結ぶ必要がある。[設計の見直し](ArchitectureReview.md)に、ネイティブ編集画面への一時切替と、公開埋め込みIME APIを持つエンジンの候補を記載した。まだエンジン変更や新方式の動作成功は主張しない。

## preview.32 — 同梱動画のRange処理を一回にする修正

初回の動画はtime=6.169913付近でwaiting、後にMediaError code=2。応答1は1,314,957 bytesを全read/EOFまで返したが、後続31要求はstart=1,048,576/length=266,381の206応答を0 read/0 skipで閉じた。後半の再読込ではbufferedが全8秒になり、ページの再生時間は7秒/8秒まで進んでループした。これはページ状態の記録で、HMD上の全frame到達を証明しない。

参照Chromium128のAndroidStreamReaderURLLoaderは、shouldInterceptRequestから渡されたInputStreamに対して元要求のRangeを解析し、InputStreamReaderがavailable()で範囲検査し、その開始offsetまでskipする。旧ProbeAssetsは既にstart offsetで切ったストリームを渡しており、後続応答はavailable=266,381に対してstart=1,048,576となって検査で失敗する。旧クラウドテストは切り出した配列の一致しか調べず、このconsumer契約を確認できていなかった。

修正版はbyte0から要求終端までのprefixを渡し、開始offsetの適用はWebViewに一度だけ任せる。終端を限定したprefixにより、EOFまで読む実装でも閉じたRangeを越えて返さない。HTTPの206/Content-Range/Content-Lengthは要求した区間を示す。ログのstreamBytesはJavaストリームの長さ、responseBytesはHTTP応答区間長で、skip/readの量を分けて記録する。MP4やGPU描画経路は変更しない。

元MP4を使い、consumer側が範囲検査/skipしてから読む契約テストと、旧二重適用で1MiB後続が拒否される回帰ケースを追加した。Android/実機のネットワーク挙動を模倣したテストではなく、参照ソースから得たストリーム契約の検証。修正後Questでの再生は未確認。この修正のためにIP2の日本語入力試験を繰り返す必要はなく、次の主タスクの実機ビルドへ同梱できる。


## preview.33 — FO1: WebViewからUnityへfocusが移った要求元を調べる

主タスクの順序を、まず既存の直接WebView経路のfocus移動の原因調査へ戻す。Window移管案/GeckoView案の実装は進めず、IP1/IP2の借用も再試行しない。

既存のNative対照ログ（2026-11-09 03:04:17–24 JST）では、ProxyEditorは入力中にfocusを維持した。03:04:17.751にWindow focusを失っても文字変更/compositionが続き、03:04:24.086のnative_done→明示的returnFocusで初めてUnityへ戻った。したがって、この区間に直接WebViewの「入力中の不本意なView focus移動」は再現していない。Window focus喪失自体は両方で起こり得る。stateログのviewFocusはWebViewの値なので、Native EditTextのfocusと混同しない。

FO1は既存のOnGlobalFocusChangeListenerとWebViewのfocus callbackに、class/method/lineのstackを追加する。各hostで最大64イベント、各48frames。Window/View callbackのenter/exitと現在の入力先Viewを記録し、独自returnFocusも区別する。Window.Callbackの置換、focus要求の追加、IME showの再試行、元のcallbackの省略/偽装は行わない。入力本文、例外message、stackのファイル名/パスは記録しない。FO1のevent/frame記録は難読化後のclass/method名でもそのまま保存し、特定のUnityクラス名に依存して切り捨てない。

同期的なrequestFocus/clearFocusなら、その呼び出し経路がfocus_change時のstackに残る。非同期なら実行されたRunnable/native境界までしか分からない可能性がある。その場合はイベント順と該当Unity Java処理を追加で調べる。一回でOS側の要求元まで必ず特定できるとはしない。

現在のIP2シーンをそのまま使う。新シーン生成やEditor/Sampleテスト、graphics API/boot設定変更は不要。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-FO1-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect focus --label FO1 --log-dir "$vrwebview_logs"
```

採取を先に起動し、一度Build And Runする。既存のIP2見出し/ボタン名は維持するので、**IP2 Startは押さない。**

1. **Proxy ONを一度押してProxy OFFにする。** 以後のページtapは元の直接WebView経路となる。
2. 動画も同時に見る場合はPlay/Pauseを一度押す。その後、ページのText入力欄を一度選び、キーボードが表示/維持されるか5秒ほど観察する。
3. 入力できた場合は架空の短い文字を入力して通常の操作で閉じる。できない場合は同じ欄やIMEボタンを連打しない。
4. Unityを操作できる場合に限り **Native IME** を一度選ぶ。このボタンは必要なcontrollerを自動的にONへ戻す。直接経路で得たWebViewのinputType/imeOptions/privateImeOptionsを対照に使い、周辺文字や接続本体は借用しない。通常の文字入力後Doneで閉じる。
5. Unityを操作できなければ追加試行は止める。Ctrl+Cで採取を終え、今回のDownloadsフォルダーをZIPで送る。

直接経路で有効なsource flagsを取得できなければNative対照はsource_flags_unavailableとして拒否する。既にNative成功の証拠はあるため、そこで対照を成立させる追加操作は求めない。

--expect focusはFO1のinstall、Proxy OFF、focusイベント/stackを必要とする。旧APK、proxy-query stackだけでは新診断の採取成立にしない。一方、WebView→Unityへの不本意な移動やIME失敗そのものを採取成功の条件にはしない。再現しない結果も保存する。summaryは原因を自動判定せず、APIの値からHMD表示も推定しない。

旧直接経路の原因は依然未特定であり、FO1は観測追加。元の問題を修正した、新しいIME方式が成立した、という更新ではない。
