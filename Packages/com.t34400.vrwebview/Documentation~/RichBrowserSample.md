# BS1 — リッチBrowser sampleの統合テスト

preview.40で、現在のBrowser UI and Model sampleを実Android factoryへ接続するシーン生成メニューと、試験用uGUIレイ入力を用意した。RT1の基本動作とhoverは利用者の実機確認済み。今回はタブ/URL UI、Unity側のURL欄IME、ページの選択と寿命を確認する。新規BS1のUnity Editor型コンパイル・APK・Quest実行はクラウドでは未実施。

## 更新とSample import

1. Unityを閉じ、作業中の変更を通常のGit運用で保全する。変更が残ればapplyは停止する。自動stashや無条件の復元スクリプトを追加しない。
2. WSLで通常のmain版CLIから追加パッチを適用する。

```bash
cd "$HOME/ai-patch-exchange"
git switch main
git pull --ff-only
git fetch origin refs/heads/jobs/vrwebview/h1-scroll-001:refs/remotes/origin/jobs/vrwebview/h1-scroll-001

GIT_CONFIG_COUNT=2 \
GIT_CONFIG_KEY_0=core.autocrlf GIT_CONFIG_VALUE_0=true \
GIT_CONFIG_KEY_1=core.filemode GIT_CONFIG_VALUE_1=false \
python3 scripts/exchange.py --repo "$PWD" apply \
  --job-ref origin/jobs/vrwebview/h1-scroll-001 \
  --project vrwebview --job h1-scroll-001 \
  --target /mnt/c/source/Unity/SimpleUnity3DWebView \
  --worktree-eol git \
  --source-git-commit 237d94d9c1cf04aa55625f66fef6b09160fe7b75
```

3. Unityでプロジェクトを開く。Package Manager > VRWebView > Samples > **Browser UI and Model** をImportする。以前のBrowserサンプルをimport済みなら、そのコピーを編集内容と.meta込みでDownloads等のAssets外へ退避してから今回版をimportする。同じasmdef名の新旧コピーを同時に残さない。IME Probeなど別サンプルまで移す必要はない。
4. 通常のEditorコンパイル完了後、**VRWebView > Create Rich Browser Sample Scene** を選ぶ。Build Settingsには生成したRichBrowser sceneだけが入る。タイトルは **BS1 - Browser sample**、UIはNew / Close / URL欄 / Go / タブ列。

メニューはimportされた現行sampleのBuilder・AndroidBrowserPageFactory・Diagnosticsを確認する。足りなければImport案内で停止し、fake backendへ置き換えない。試験シーンのCanvas、Camera、EventSystem、レイ、factory、診断を自動接続する。RT1シーンの再生成、graphics API、GameActivity、ARM64、OpenXR、Requires System Keyboard、ハンド設定の変更は不要。

## 採取と実行

Build And Runの前にWSLで開始する。これまで同様、画面の数値を転記する必要はない。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-BS1-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect browser-ui --label BS1 --log-dir "$vrwebview_logs"
```

RT1で成功した設定のままBuild And Runする。次の順で、問題が出た時点で操作を止める。初回から設定変更や旧プローブの総当たりをしない。

1. **BS1 - Browser sample**と水色のレイ、New / Close / URL欄 / Goが見える。初期状態ではタブがなく、Create a tab to beginと表示される。
2. **New**を一度押す。Tab 1ができ、URL欄に`https://example.com/`が入る。初期値はdraftだけで、勝手にnavigationしない。Ready後に**Go**を押し、Example Domainが出るか確認する。外部ページなのでインターネット接続が必要。
3. **Unity側URL欄**を選ぶ。Questキーボードが表示され、`https://example.org/`へ編集できるか確認する。キーボードを閉じ、レイが戻って**Go**を押せるか確認する。ここはWebViewのIMEとは別の入力経路。
4. **New**でTab 2を作り、URL欄を`https://www.wikipedia.org/`へ変更してGo。キーボードを閉じてTab 1 / Tab 2を切り替え、各URL draftと表示先が対応するか確認する。
5. Tab 2のWikipedia検索欄で日本語を変換・確定できるか確認する。キーボードを閉じてからタブを切り替え、レイ・ページのhover・ドラッグスクロールが戻るか確認する。URL欄とWebViewのどちらで問題が出たかだけ区別する。
6. Tab 2を**Close**し、Tab 1へ戻ることを確認する。最後のタブも閉じ、Newでもう一度作成できるか確認する。初回は2タブでよく、連続大量生成や長時間試験はしない。

通常navigationのTask/Readyは読込完了の証明ではない。外部サイトが接続エラーになった場合は、その表示とログで判断し、UI入力の失敗と混同しない。今回のsampleはNew/Close/タブ切替/URL/Goが対象。戻る/進む、確定URL/タイトル、履歴・ブックマークは未実装。

Ctrl+Cで採取を止め、同じシェルでZIPにする。

```bash
python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"
```

DownloadsのZIPと、止まった手順番号・URL欄かWebページ内か・実際にキーボードが出たかを添える。原文URL・入力文字や画面の状態値を転記する必要はない。summaryに問題が出てもraw log/summary/stderrは保存される。

## 実装境界と診断

- 製品BrowserSession/BrowserView/AndroidBrowserBackendと、importされた実Browser UI/Factoryを使う。UIのテスト用fake factoryはPlayerへ入れない。
- BS1レイは試験用PointerInputModule。フラットなWorld Space Canvasへ投影し、通常のuGUI enter/move/down/up/click/drag/scrollを送る。mouse cursor lockをVRレイの可否として扱わない。head移動だけでdrag判定が跳ねないよう、前回位置とpress位置を現在のcameraへ写し直す。
- input focusやtrackingを失ったときはpointerをCancelし、押していないbuttonをクリックしない。BrowserPointerSourceにはCancelを送り、touch-upで確定しない。URL欄の選択は維持してInputField/TouchScreenKeyboardが編集を続けられるようにする。XR Hands reset同期はRT1と同じ検証済みhelperを使う。
- タブ切替・URL欄選択・UI無効化で前ページの編集を終了する。Editing capabilityはpointer sinkと別の役割で保持する。閉じたbackendのEndEditingはno-op。Androidは過去のpointer owner記録だけでUnityへfocusを戻さず、実View focus/IMM servedを使う。非選択hostの終了がUnityのURLキーボードを奪わないための処理で、新しい複数UI構成は実機で確認する。
- native targetが退役したら、診断ONのbackendだけtarget_retiredを記録する。nativeAlive=0とUnity Texture破棄要求を区別し、Destroy要求時点を物理メモリー解放の完了とは扱わない。
- BS1のUI構成完了と実Android host作成を別々に記録する。採取のbrowser-ui判定は両方を要求し、RT1のログやwidgetだけのsceneを成功と扱わない。snapshotはtab数・選択id・状態・addressFocus・frame等のmetadataのみ。URL・draft・入力本文を記録しない。

サンプルEditorテストに初期draft保持と編集終了の接続を追加した。PlayModeテストに、実uGUI buttonのclick/cancelとBrowserPointerSourceへのhover/Cancel配送を追加した。クラウドではこれらの実Unityテストを実行したとは扱わない。依存境界、C#構文、Python採取、Android build/Lint/R8と配布物を検査し、最初の実Editor/Quest結果は利用者の試行で判断する。

## この後の作業

利用者メモの「JavaScript実行などWebView操作interfaceの整理」「配布形式の整理」はTechnicalDebt.mdへ保留タスクとして記録した。BS1準備で完了したものではない。
