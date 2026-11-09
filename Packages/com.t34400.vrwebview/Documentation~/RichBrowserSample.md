# Rich Browser sample — preview.41

preview.40のBS1はURL欄でキーボード表示に失敗し、高さ配分も不正だった。preview.41は操作行を固定高にし、共有tab所有モデルと複数window、表示tabのみの出力を実装した。Unity型コンパイル・新しいuGUIテスト・APK/Quest統合はクラウドでは未実施。IME表示の実機成功を先に主張しない。

## 更新

Unityを閉じ、通常のmain版patch-exchange CLIからjobs/vrwebview/h1-scroll-001の追加パッチを適用する。applyはdirtyな対象を拒否するため、作業中の変更は通常のGit運用で保全する。過去のstashを勝手にpop/dropしない。

1. Package ManagerでVRWebViewの **Browser UI and Model** を今回版へImportする。旧版をimport済みなら、編集内容と.metaを含む旧BrowserコピーだけをDownloads等のAssets外へ退避し、同名asmdefを重複させない。
2. **VRWebView > Create Rich Browser Sample Scene** で新しいシーンを作る。既存BS1シーンは古いBuilderを保持するため再利用しない。
3. グローバルScene Listに加えて、Unity 6のアクティブBuild ProfileのScene Listも今回のRichBrowserだけになっていることを確認する。
4. Vulkan/ARM64/GameActivity/OpenXR/ハンド設定とRequires System Keyboardは従来の成功設定を維持する。生成メニューはUnity URL入力用のkeyboard bootを明示的にON(1)にする。旧診断OFFを残してビルドすると停止する。

初期画面には一つの空タブがあり、上にタブ行、戻る/進む/再読み込みとURL欄、下にMove/Resizeがある。New tabは＋、各tabの×はそのtab、右上の×はそのwindowを閉じる。New windowは別のUnity内windowを開く。最後のwindowを閉じた場合は新規の空windowを作る。

## 一回の採取

Build And Runの前にWSLで開始する。以前のEditorテスト一式を先に実行する必要はない。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-BS1-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect browser-ui --label BS1 --log-dir "$vrwebview_logs"
```

今回のRichBrowserをBuild And Runする。外部ページにはインターネット接続が必要。

1. タブ/操作行が細く、ページが画面の大部分を使うことを確認する。
2. URL欄を選び、`example.com`と入力してキーボードの確定またはGoで移動する。キーボードが出なければ、ここで停止する。設定の総当たりはせずログを回収する。
3. 表示できたらWikipedia等へ移動し、ページ内の日本語変換・確定、キーボード終了後のレイ復帰を確認する。ページ内リンクからの戻る/進む、再読み込みも確認する。
4. ＋で別tabを作る。tabを列内でdragして並べ替える。外へdragし、Release to create a windowの表示で離して分離する。別windowのtab列へdragし、挿入マーカーで離して結合する。
5. 2windowを同時に表示して各選択tabを操作する。Move/Resizeをdragし、ページ比率・入力位置・もう一方の表示が保たれるか確認する。操作中に追跡を失った場合は移動確定せず元へ戻ることが期待値。
6. tab/windowを閉じ、移動済みのtabが残ることを確認する。動画/音声は通常のページで確認する。非表示tabの音声停止は今回の転送最適化とは別ポリシー。

問題が出た最初の手順と実際の症状だけを添える。HMDの数値転記は不要。Ctrl+Cで採取を止め、同じシェルでZIPを作る。

```bash
python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"
```

## 実装と診断

- URL入力はUnityのTouchScreenKeyboard Default、ページ入力はWebViewの元のInputConnection。URL入力前にAndroid UI threadでのページ編集終了を待つ。旧要求はfieldの世代で無効化する。proxy借用やDOM全文同期は使わない。
- APK内の実boot値とmanifestのoverlay宣言はevent=keyboard_configurationで記録する。Unity keyboard timeoutを採取summaryの問題に含める。入力文字・URL・titleはログへ出さない。
- tab/sessionはBrowserDesktopPanelが共有所有し、各windowは所属・順序・選択と表示bindingを持つ。Move/Detachで同じWebViewを保ち、UI破棄だけでは移動したpageを閉じない。
- tab dragはpreview後のreleaseで確定する。tracking/focus取消では元の所属を保持。tab列端では自動scrollし、window全体は専用Move/Resizeから操作する。試験レイは共通root下のwindow Canvasを扱う。アプリ本体には自前のXR UI moduleを使用できる。
- 表示中tabだけにproducer Surface/AImageReader/Unity textureを割り当てる。非表示tabに出力queueを常設しない。入力focusを失っても、表示中なら転送は継続する。
- 切替/resizeは新しい出力世代を作り、古いproducerの停止確認後にGPU完了まで回収する。出力textureをタブ間で直接上書きして使い回さない。定常時は表示tab数に応じた出力数、切替中は退役待ちの旧世代が一時的に残る。世代単位のpool再利用は未実装。
- event=presentation_statsにpage/target/visibleとGPU copies等、event=window_layoutに実ページ領域の寸法を出す。UI/host readyだけでキーボード・動画や転送最適化の成功と判断しない。
- pageのURL/title/history/loading/errorは正式なCore capabilityを通す。コマンドのTaskは要求受理であり、ページ読込完了ではない。検索サービス、favicon、履歴UI、bookmarks、window.open/popups、自由な3D回転・奥行き配置は未実装。現在のwindowは共通平面上で移動・resizeする。

クラウドでは純モデルの所有/移動/API、C#構文、依存境界、Python採取、Android build/Lint/R8と配布物を確認する。新規の実uGUI寸法テストは同梱しているが、実Unity実行済みではない。QuestではURL欄の修正効果を最初に判断し、失敗時は以降の統合結果を合格にしない。
