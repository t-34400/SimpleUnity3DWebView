# Browser UI and Model

任意Importのリッチブラウザサンプルです。BrowserDesktopがページを所有し、BrowserDesktopPanelがタブ・複数ウィンドウとUIを接続します。AndroidBrowserPageFactoryが実WebViewを生成します。

Import後に **VRWebView > Create Rich Browser Sample Scene** を実行するとBS1を作成できます。既存のRichBrowserシーンは、サンプルコードを更新すれば再利用できます。

検索、ブックマーク、履歴、セッション・配置復元、閉じたタブの再開、文字サイズ、ページ内検索、リンクの長押しメニューを含みます。保存データはアプリ内に留まり、Coreには検索サービスや履歴保存の方針を持ち込みません。

更新時はUnityを閉じ、リポジトリの `Tools/update_browser_sample.py` で既存Import先を更新できます。既存.metaのGUIDとアプリ専用ファイルを残し、上書き前に指定先へ完全なバックアップを作ります。変更したサンプルコードはバックアップとの差分を確認して移行してください。

手順はpackageの **Documentation~/BrowserFeaturesQuestCheck.md**、設計は **BrowserFeatures.md** を参照してください。新機能のUnity Editor・Quest検証は未完了です。Tests内のfake backendはテスト専用で、通常のサンプル起動には使いません。単一ウィンドウの旧BrowserPanel/BrowserPanelBuilderも互換のため残しています。
