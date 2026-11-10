# Rich browser features — preview.50

2026-11-10。preview.49のURL欄の同期は利用者が確認済み。今回も標準InputFieldと同じ互換修正を使う。新機能のQuest動作はまだ未確認。

## 更新

1. Unityを閉じ、通常のpatch-exchangeから最新 `jobs/vrwebview/h1-scroll-001` をfetch/applyする。
2. 今回はサンプルコードも変更したため、下のコマンドで既存Import先を更新する。既存.metaのGUIDとシーン参照を保ち、上書き前のコピーをDownloadsへ保存する。サンプルに加えた独自変更は、そのバックアップから差分を移行する。新しいversionを別フォルダーへ重複Importしない。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
python3 Tools/update_browser_sample.py --backup-dir /mnt/c/Users/tmiyoshi/Downloads --apply
```

複数のBrowserサンプルが見つかった場合は変更前に停止する。実際に使う一つを `--target 'Assets/Samples/VRWebView/<version>/Browser UI and Model'` で指定する。未Importの場合だけPackage Managerから今回版をImportする。
3. 既存のRichBrowserシーンを再利用する。シーンの作り直し、U1/U2の再試験、Editorの繰り返し起動は不要。Build ProfileのScene Listがこのシーンであることだけ確認する。
4. 成功済みのOpenXR/GameActivity/ARM64/graphics/keyboard設定を維持する。GameActivity IME Compatibilityの既存ON設定を維持する。

## 一回の確認

1. URL欄で `日本語 VR` を入力して検索、続いて `example.com` を入力してページへ移動する。既存URLの上書きと追加入力の同期も確認する。
2. ☆でページを保存し、＋で新規タブを開く。Bookmarksに保存したページがあり、開けることを確認する。メニュー ☰ のHistoryから既訪問ページへ移動し、不要な項目を×で削除する。
3. メニューのText −/＋で文字サイズを変え、Findで表示中ページ内の語を検索する。件数と前後移動を確認する。Find欄も同じ標準Unity入力を使う。本文内の日本語入力・動画の通常再生も一度確認する。
4. ページ内リンクを長押しし、Open in new tabとCopy addressを試す。コピーしたURLをURL欄へ貼り付けられるか確認する。画像の長押しは画像URLを対象にする。保存画像/ダウンロード機能は今回含まない。
5. 2ウィンドウでタブを作り、並べ替え、選択タブ・サイズ・位置・向きを変える。閉じたタブをメニューのReopen tabで戻す。移動したウィンドウはRecall windowsで頭の前へ戻せることを確認する。
6. 最後の操作から2秒程度待ち、アプリを終了・再起動する。ウィンドウ・タブ順・選択・配置・文字サイズが戻ることを確認する。非表示タブは選択時に読み込まれる。フォームやページ内の戻る履歴は復元対象外。

保存先はアプリ専用の `Application.persistentDataPath/vrwebview-browser-v1.json`。URLには個人的な検索文字列が含まれることがある。診断への全文添付は不要。履歴はClear history、セッションはForget sessionで消せる。Forget session後は現在の起動中の配置・タブを再保存せず、ブックマークと履歴だけ保存する。Cookieやログイン情報を消す操作ではない。

エラー時だけ従来の一回の採取を使う。UI readyだけで新機能の成功とは判定しない。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-BS1-Features-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect browser-ui --label BS1-Features --log-dir "$vrwebview_logs"
```

Ctrl+Cで止め、同じシェルで `python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"`。問題が出た手順番号と見えた症状だけでよい。

## 開発者向けAPI

BrowserController.Sessionを初期化後、Unity owner threadから操作する。

```csharp
string json = await controller.Session.EvaluateJavaScriptAsync("document.title");
await controller.Session.SetTextZoomAsync(120);
controller.Session.FindResultChanged += result => { /* revision/count/active */ };
await controller.Session.FindAsync("Unity", revision: 1);
await controller.Session.FindNextAsync(true);
await controller.Session.ClearFindAsync();
```

結果はWebViewのJSON表現（文字列なら引用符付き）。Promise待機やJS例外の検出、任意サイトとの双方向メッセージ機能ではない。キャンセルは実行済みJSを巻き戻さない。Coreはブラウザの保存場所や検索サービスを決めない。

ネイティブ全画面動画、ファイル選択/ダウンロード、サイト権限、独立したプライベートプロファイル、自動popup/JSダイアログは別の実装課題として残る。
