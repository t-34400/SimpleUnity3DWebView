# VRWebView

Unity内にAndroid WebViewを表示する開発中のパッケージです。Quest 3・Unity 6000.4.5f1・OpenXRを主な検証環境としています。

- **Core / Runtime**: WebViewの生成、HTTP(S)ナビゲーション、GPU表示、ポインター入力、ネイティブIME、JavaScript実行、文字サイズ、ページ内検索。
- **任意のBrowserサンプル**: タブと複数ウィンドウ、検索、ブックマーク、履歴、ローカルのセッション・配置復元。

Package Managerの **Browser UI and Model** をImportし、**VRWebView > Create Rich Browser Sample Scene** で試せます。既存サンプルを更新する場合は[更新・実機確認手順](Documentation~/BrowserChrome.md)を参照してください。

[現在の確認状況](Documentation~/CurrentStatus.md) · [APIと設計](Documentation~/BrowserFeatures.md)

Questの入力にはoverlay keyboard設定が必要です。Unity/GameActivityの既知の入力問題には、バージョン照合付きの[任意の互換修正](Documentation~/GameActivityImeCompatibility.md)を用意しています。

ネイティブ全画面動画、ダウンロード/ファイル選択、サイト権限UI、独立したプライベートプロファイルは未対応です。新機能の実機確認と配布パッケージからの検証コード分離は継続中です。

MIT License。互換AAR内のGoogleコードは元のApache-2.0ライセンスを保持します。
