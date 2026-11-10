# Browser UI revision — preview.51

2026-11-10。利用者がpreview.50で検索、ブックマーク、文字サイズ、Find、window操作、再起動時の復元を確認した。リンク操作も成功との報告だが、その操作は添付ログ外。
今回のUI変更は実機未確認。音声については利用者の指示で調査を保留し、音声経路や設定を変更していない。

## 変更

- **URL欄**: 文字列由来の希望幅をレイアウトへ混ぜず、残り幅を使う。Back/Forward/Reload/Go/☆/メニューは最小幅と希望幅を揃える。全文は保持し、欄の中でclip/横スクロールする。
- **右上メニュー**: ボタンの下に縦メニュー。Bookmarks/Historyを別一覧へ、データ削除をSettingsへ整理する。文字サイズは `− 100% ＋` で、操作中もメニューを保持する。
- **Find**: 本文上端右に検索バーを重ねる。開閉でページ領域を縮めず、GPU出力を作り直さない。
- **リンク**: 長押し位置付近に対象のドメイン/省略URLとOpen/Copyだけのメニュー。位置はサンプル側で押下時に保持し、Core/JNIを増やさない。
- **ブックマーク**: ☆で保存して名前編集、★は編集を開く。削除はDeleteを明示する。一覧はタイトル/ドメインと項目メニューを持つ縦スクロールで、ページ送りを廃止。新規タブには検索案内と最大6個の保存ページの入口を表示する。
- **姿勢**: 標準YawOnly。回転は水平のみ、移動は3軸。固定傾斜は初期5度、Settingsで0〜10度に変更できる。5度が最適と検証したわけではなく、配置高さによる見やすさを実機で確認する。

## 所有と入力

windowごとのBrowserPopupHostが一つのメニュー、アンカー、境界内への補正、スクロール、外側クリックによるcloseを所有する。外側クリックは本文へ通さない。本文全体を別画面へ置き換えず、同じwindowのCanvas上で表示する。

タブ切替・ナビゲーション・window破棄・明示キャンセルで閉じる。OSキーボードによるfocus lostだけでは編集中のメニューを閉じない。名前の編集は既存のBrowserAddressInputと編集所有権の引き渡しを使う。

旧BS1の既定FaceViewer=0は実行時にYawOnlyへ移行する。過去のscene/patchは書き換えない。新規作成、Restore、Recallでroll/pitchを正規化し、保存されたyawは維持する。固定傾斜の設定は既存schema1へ任意のposeVersion/値を追加して保存する。旧データでは5度を初期候補とし、明示的な0度も保存可能。

Coreのブラウザ機能、Native GPU、GameActivity IME互換AARとAndroid AARは今回変更しない。

## 更新

Unityを閉じて最新job `jobs/vrwebview/h1-scroll-001` を通常のpatch-exchangeでfetch/applyした後、既存sampleを更新する。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
python3 Tools/update_browser_sample.py --backup-dir /mnt/c/Users/tmiyoshi/Downloads --apply
```

更新前のsampleはDownloadsへ保存される。既存.metaを維持するので、既存のRichBrowserシーンをそのままBuild And Runできる。Assets/Samplesのフォルダー名が旧versionのままでもよい。重複Importや新シーン作成は不要。サンプルに独自変更がある場合はバックアップから差分を移行する。成功済みのIME設定を維持する。

## 一回の実機確認

1. 以前問題が出た長いURLを表示/編集し、周りのボタンが押せる幅を維持するか確認する。URL入力の日本語変換と同期も一度確認する。
2. 右上メニューで本文を見ながら文字サイズを変える。Findを開閉し、本文領域の大きさが変わらず、検索・件数・前後移動が機能するか確認する。
3. リンクを長押しして、押した場所の近くにOpen/Copyの小さなメニューが出るか確認する。右/下端でもwindow内に収まるか、外側クリックで閉じた際に本文を誤操作しないか確認する。
4. ☆で保存、名前を日本語に変更してDone、★で再度編集を開く。一覧をスクロールし、項目の⋯から編集/削除できるか確認する。
5. windowを上下左右と奥行きへ移動し、pitch/rollが勝手に変わらないか確認する。Settingsで傾斜を0度と5度に変え、Recallとアプリ再起動後にも方針・設定が保たれるか確認する。

音声の比較試験は今回の確認項目に含めない。

問題が出た場合は従来どおり一回の採取を使う。ui_readyにはversion=51を記録する。表示成功や操作感は利用者の観察で判断し、readyだけで合格にしない。

```bash
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-BS1-Chrome-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect browser-ui --label BS1-Chrome --log-dir "$vrwebview_logs"
# Ctrl+Cで採取を止めてから:
python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"
```
