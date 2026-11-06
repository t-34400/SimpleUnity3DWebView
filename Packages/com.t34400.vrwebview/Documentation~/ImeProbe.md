# IME Probe

## 観測するもの

同じ静的HTMLを使い、Android標準WebViewとVirtualDisplay上のWebViewを比較するための検証用実装です。別EditTextやDOM全文置換による入力中継は使いません。

画面に表示するのはView focus、Window focus、IMM.isActive、Display ID、IME表示要求の受付結果、取得フレーム数です。`showRequestAccepted`は表示成功を意味しません。IMEが見えるか、入力できるか、変換・選択が正しいかは別に確認します。本文はLogcatやUnityへ送りません。ページ内のイベント表示も要素名・範囲・イベント種別だけです。

## モード

表示ラベルは`viewFocus`、`windowFocus`、`immActive`、`viewDisplayId`。以前の`served`／`display`表記を統一した。動画とGPU取り込みを比較するときは、サンプルImport不要の`VRWebView > Create GPU Probe Scene`を使用する。手順は[GPUプローブ](GpuProbe.md)を参照。

- **Standalone / Host Window**: 独立したAndroid Activity内のWebView。標準の入力接続を確認する基準。
- **Standalone / Virtual Display**: 独立ActivityにCPU画像プレビューを表示し、タッチをVirtualDisplayのWebViewへ渡す。IME自体はプレビューへ取り込む設計ではない。
- **Unity Activity overlay**: 実際のUnity Activity上へ同じ仕組みを接続する実験用入口。XRでは通常のAndroid Viewが見えない可能性がある。その場合も独立Activityでの結果と区別する。表示/操作が成立するVR入力UIはまだ実装していない。

サンプルのStartはStandaloneを自動起動するため、最初の検証にはVRコントローラーUIの配線が不要です。CloseでUnityへ戻ります。再起動はアプリを再起動するか、サンプルの公開メソッドをUIボタンに接続します。OnGUIはEditor/通常画面の補助で、VR内の操作手段として保証しません。

## 手順

1. Runtime/Plugins/AndroidのAARが存在することを確認し、サンプルをimportする。
2. メニューでシーンを生成し、Startup Modeを設定してAndroidへビルド。
3. テキスト欄へ日本語を入力し、変換中の文字、文中挿入、選択削除・置換、前後の文字を含む編集を確認。
4. 複数行、引用符・バックスラッシュ、password、contenteditable、フィールド切替を確認。
5. ページ側の値変更と入力対象の削除を確認。Reload testで初期状態へ戻す。
6. IMEの候補位置、確定/キャンセル、閉じた後のフォーカスを確認。
7. Close後の再起動、アプリ休止/復帰、作成/破棄反復を確認。
8. 同じ操作をVirtualDisplayモードで行い、状態と症状の差を記録する。
9. Unity Activityへ接続する試験は別記録にする。レイ操作、コントローラー入力、Unityフォーカスとの共存を確認。

検証表には端末、OS、WebView provider、Graphics API、モード、操作、期待値、実結果を記録してください。合否は実結果で付けます。空欄をpassとして扱いません。

## 意図した制約

- CPUプレビューは10fps程度の診断用。画像のコピー/確保を伴い、本番性能を測るための実装ではない。
- VirtualDisplayの画像は1024x768固定。プレビューの余白とアスペクト比を計算して座標を変換する。
- 一度に一つのプローブ。新規作成で前のプローブを破棄する。
- Web権限は拒否し、JavascriptInterfaceを公開しない。WebViewリモートデバッグを勝手に有効にしない。
- フィールドを選択していなければ、IME要求が失敗しても正常な観測結果となり得る。
- 新しいプローブActivityはWindow/IMEの基準試験であり、独立Activityへの切替中はUnity側が休止し得る。
- 自動スクリーンショットや入力本文の収集は行わない。
- ホスト切替・再生成・エラー回復、GPU経路の正式実装はこのプレビューの完成範囲に含まない。
