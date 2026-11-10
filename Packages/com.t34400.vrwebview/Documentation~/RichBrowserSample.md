preview.48のURL入力試験は[GameActivity IME互換修正](GameActivityImeCompatibility.md)を参照。既存BS1シーンを再利用し、U2の追加showを使わない。サンプル再Importは不要。

# Rich Browser sample — preview.44

利用者がpreview.43のヒット修正を確認した。preview.44は角の判定拡大と配置時の姿勢変更を追加する。URL IMEは未解決で、標準Unity入力を維持して[独立対照U1](UnityAddressInputInvestigation.md)を追加した。U1でも失敗したため、次は[U2の順序対照](UnityInputOrderProbe.md)へ進む。BS1のURL操作を何度も繰り返す段階ではない。

## 更新

Unityを閉じ、最初に転送refを更新する。applyは自動fetchしない。

```bash
cd "$HOME/ai-patch-exchange"
git fetch origin refs/heads/jobs/vrwebview/h1-scroll-001:refs/remotes/origin/jobs/vrwebview/h1-scroll-001
```

通常のmain版patch-exchange CLIからjobs/vrwebview/h1-scroll-001の追加パッチを適用する。applyはdirtyな対象を拒否するため、作業中の変更は通常のGit運用で保全する。過去のstashを勝手にpop/dropしない。

1. Package ManagerでVRWebViewの **Browser UI and Model** を今回版へImportする。旧版をimport済みなら、編集内容と.metaを含む旧BrowserコピーだけをDownloads等のAssets外へ退避し、同名asmdefを重複させない。
2. preview.41/42のRichBrowserシーンはそのまま使える。今回版sampleのスクリプトでUIが実行時に再構築される。preview.40以前の旧Builderシーンだけは **VRWebView > Create Rich Browser Sample Scene** で更新する。
3. シーンを新しく生成した場合は、Unity 6のアクティブBuild ProfileのScene Listも今回のRichBrowserになっていることを確認する。
4. Vulkan/ARM64/GameActivity/OpenXR/ハンド設定とRequires System Keyboardは従来の成功設定を維持する。生成メニューはUnity URL入力用のkeyboard bootを明示的にON(1)にする。旧診断OFFを残してビルドすると停止する。

初期画面には一つの空タブがあり、上にタブ行、戻る/進む/再読み込みとURL欄がある。最後のtabの隣の＋で新規tab、tab内の×でそのtab、右上の×でwindowを閉じる。□+は別のUnity内windowを開く。最後のwindowを閉じた場合は新規の空windowを作る。

## 一回の採取

Build And Runの前にWSLで開始する。以前のEditorテスト一式を先に実行する必要はない。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-BS1-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect browser-ui --label BS1 --log-dir "$vrwebview_logs"
```

今回のRichBrowserをBuild And Runする。外部ページにはインターネット接続が必要。

1. rayがウィンドウの手前でも見え、ヒット位置にcyanのリングが出ることを確認する。ray終点とリングは同じ判定位置。UIに当たらないrayは灰色、押下中は黄色、クリック/drag完了は短く緑になる。
2. リングをURL欄へ合わせてpinchする。押した時点で編集開始し、離す位置が多少変わっても開始要求を失わない。キーボードが開いたら`example.com`と入力してキーボードの確定またはGoで移動する。キーボードが出なければ、ここで停止する。設定の総当たりはせずログを回収する。
3. 表示できたらWikipedia等へ移動し、ページ内の日本語変換・確定、キーボード終了後のレイ復帰を確認する。ページ内リンクからの戻る/進む、再読み込みも確認する。
4. ＋で別tabを作る。tabを列内でdragして並べ替える。外へdragし、Release to create a windowの表示で離して分離する。別windowのtab列へdragし、挿入マーカーで離して結合する。
5. 2windowを同時に表示して各選択tabを操作する。上部の空白（右側には常設の空白あり）をpinch dragし、左右上下と奥行きへ移動する。四隅にレイを当て、L字がcyanに強調されることを確認してdragでresizeする。対角の角が固定され、ページ比率・入力位置・もう一方の表示が保たれるか確認する。tabが多いときは下の横scrollbarでスライドする。操作中に追跡を失った場合は移動確定せず元へ戻ることが期待値。
6. tab/windowを閉じ、移動済みのtabが残ることを確認する。動画/音声は通常のページで確認する。非表示tabの音声停止は今回の転送最適化とは別ポリシー。

問題が出た最初の手順と実際の症状だけを添える。HMDの数値転記は不要。Ctrl+Cで採取を止め、同じシェルでZIPを作る。

```bash
python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"
```

## 実装と診断

- URL入力はUnityのTouchScreenKeyboard Default、ページ入力はWebViewの元のInputConnection。URL押下時にEventSystemの選択を明示し、Android UI threadでのページ編集終了をmain-thread coroutineで待つ。旧要求はfieldの世代で無効化し、後から別のUIを選んだ場合は再選択しない。proxy借用やDOM全文同期は使わない。
- APK内の実boot値とmanifestのoverlay宣言はevent=keyboard_configurationで記録する。Unity keyboard timeoutを採取summaryの問題に含める。event=address_editでprepare/aborted、event=address_keyboard_requestで表示要求、失敗時はreasonを記録する。入力文字・URL・titleはログへ出さない。
- tab/sessionはBrowserDesktopPanelが共有所有し、各windowは所属・順序・選択と表示bindingを持つ。Move/Detachで同じWebViewを保ち、UI破棄だけでは移動したpageを閉じない。
- 専用Resources shaderはOverlay queue/高いsorting order/ZTest Alwaysとstereo macrosを持つ。これはUI用の診断rayであり、UIでrayを隠さず、当たった位置で線を止める。rayとmarkerは入力と同じworld hitを使う。一般シーンのPhysics occlusionはこのsampleの判定対象に含めない。
- world hitはGraphicRegistryの描画中Graphicと実RectTransform平面を使う。マウスのdisplay routingは経由しない。Graphic.RaycastでuGUIのMask/CanvasGroup等を維持する。通常のscreen-based入力もBrowserPointerSourceで引き続き使える。
- event=activationはpress/release/click/dragを区別し、ray snapshotにhit種別と距離を記録する。Upのhover先だけでクリック成功を推測しない。event=pointer_visual_failedはsummaryへ問題として集計する。
- tab dragはpreview後のreleaseで確定する。tracking/focus取消では元の所属を保持。tab列端では自動scrollする。windowは上部の空白から3D移動し、四隅で対角を固定してresizeする。試験レイはwindowごとの平面へ実際のhand/controller rayを当て、異なる奥行きを区別する。アプリ本体のXR UI moduleには任意のBrowserRayPointerEventData/IBrowserSpatialDragHandlerを提供する。通常のPointerEventDataでは平面上のdragへfallbackする。
- 表示中tabだけにproducer Surface/AImageReader/Unity textureを割り当てる。非表示tabに出力queueを常設しない。入力focusを失っても、表示中なら転送は継続する。
- 切替/resizeは新しい出力世代を作り、古いproducerの停止確認後にGPU完了まで回収する。出力textureをタブ間で直接上書きして使い回さない。定常時は表示tab数に応じた出力数、切替中は退役待ちの旧世代が一時的に残る。世代単位のpool再利用は未実装。
- event=presentation_statsにpage/target/visibleとGPU copies等、event=window_layoutに実ページ領域の寸法を出す。UI/host readyだけでキーボード・動画や転送最適化の成功と判断しない。
- pageのURL/title/history/loading/errorは正式なCore capabilityを通す。コマンドのTaskは要求受理であり、ページ読込完了ではない。検索サービス、favicon、履歴UI、bookmarks、window.open/popups、OS window.openとUnity windowの自動接続は未実装。[配置仕様](SpatialWindowInteraction.md)のFaceViewer/FollowHand/TranslateOnlyを選べる。

クラウドでは純モデルの所有/移動/API、C#構文、依存境界、Python採取、Android build/Lint/R8と配布物を確認する。新規の実uGUI寸法テストは同梱しているが、実Unity実行済みではない。QuestではURL欄の修正効果を最初に判断し、失敗時は以降の統合結果を合格にしない。


角の判定は標準96pxで主に外側へ広げる。移動は標準FaceViewerで向きも調整する。手の回転へ直接追随させるにはBrowserDesktopPanelのPlacement ModeをFollowHandにする。実機の操作感は未確認。詳細は[空間ウィンドウ調査](SpatialWindowInteraction.md)。
