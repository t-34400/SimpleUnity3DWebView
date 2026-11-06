# VRWebView

Unity 6000.4 / Meta Quest向けWebViewの新規実装です。配布物は埋め込みUPMパッケージ `Packages/com.t34400.vrwebview` に集約しています。

このプレビューには、セッション・要求応答・ポインタ入力の基盤とAndroid IME検証プローブがあります。**VR空間へWebページを表示する製品用GPUバックエンドは未実装**です。キーボード・日本語IME・XRフォーカスの成立も実機で検証してください。

## 構成

```text
Packages/com.t34400.vrwebview/
  Runtime/Core/          Unityに依存しないセッション・ポインタ・応答管理
  Runtime/UnityUI/       uGUIからの座標変換・ポインタ入力
  Runtime/Android/       IMEプローブへのJNI入口
  Runtime/Plugins/Android/  ビルド済み検証用AAR
  Editor/                検証シーン作成メニュー
  Tests/EditMode/        Unityで実行するテスト
  Samples~/ImeProbe/     Package Managerからimportする検証用スクリプト
  Documentation~/       使用方法・検証項目
Android~/                独立したAndroid LibraryのソースとGradle wrapper
Tools/                   ビルド・テスト・unitypackage出力
```

Meta SDKはルートの検証プロジェクトの依存で、本体Runtimeの必須依存ではありません。既存の旧WebView/AAR・古いシーン/Meta64サンプルを取り除き、Unity/OpenXR/Metaの更新済み設定を基準として残しています。旧カスタムAndroid Manifest/Gradleは無効化し、検証AARの最小Manifestをマージします。

## 最初の実機検証

1. Unity 6000.4.5f1でルートを開き、パッケージの解決を待ちます。
2. Package ManagerのVRWebViewから **IME Probe** サンプルをimportします。
3. **VRWebView > Create IME Probe Scene** を実行します。保存されるシーンは生成のたびに新規作成されます。
4. IME Probeオブジェクトの`Startup Mode`を選びます。初回は`Host Window`。
5. Androidへビルドして起動します。自動で独立したAndroid Activityを開きます。
6. フィールドをタップし、日本語変換・途中挿入・選択置換・改行・password・フィールド切替を確認します。合成データだけを使用してください。
7. 次に`Virtual Display`で同じ操作を比較します。表示されるCPUプレビューは検証専用です。

独立ActivityはAndroid標準の接続を確認する基準です。**この成功はUnity/XRとIMEの共存成功を意味しません。** Unity Activity上で比較する入口は`AndroidImeProbe.OpenOnUnityActivity`です。詳細はパッケージの[検証手順](Packages/com.t34400.vrwebview/Documentation~/ImeProbe.md)にあります。

## コード検証

Python 3と.NET SDK 8を使用します。Unityの依存は不要です。

```sh
python3 Tools/run_checks.py
```

Unity側のテストはTest RunnerのEditModeから実行します。純C#テストとUnity実行・実機試験は別の結果として扱います。

## Androidソースの再ビルド

JDK 17以上、Android SDK Platform 35、Build Tools 34.0.0を準備し、`ANDROID_HOME`と`JAVA_HOME`を設定します。Gradle 8.9 / AGP 8.7.3を固定しています。

```sh
python3 Tools/build_android.py
```

標準Java APIのみを使う小さな検証ライブラリなので、現時点ではKotlin stdlib・Unityのclasses.jar・NDKは不要です。製品用GPU経路の検証時にC++/NDKを追加します。

## 配布

主形式はUPMです。現在のプロジェクトではPackages内の埋め込みパッケージとして読み込まれます。別プロジェクトではPackage Managerの「Add package from disk」でpackage.jsonを指定できます。

unitypackageが必要なら:

```sh
python3 Tools/export_unitypackage.py
```

WindowsではユーザーのDownloads、今回指定されたWSL環境では`C:\Users\tmiyoshi\Downloads`へ出力します。`--output`で変更できます。Runtime/Editor/サンプル/文書を`Assets/VRWebView`に集約するアーカイブを生成し、GUIDとファイル本文を照合します。元プロジェクト内にコピーを作らないため、export中に重複GUIDをimportしません。利用先ではUPM版とunitypackage版の片方だけを導入してください。Unityでの実importは別途確認が必要です。

## 実装範囲

- 実装済み: HTTP(S) URL方針、セッション初期化/破棄、遅延通知排除、要求ID/世代照合、単一ポインタ捕捉/キャンセル、pivot/Overlay Canvas対応の座標変換。
- 検証用実装済み: 通常ホスト/VirtualDisplayのWebView、IME要求、状態表示、複数行・日本語変換・contenteditableを含むテストページ。
- 未実装: 製品ブラウザバックエンド、GPU共有/NDK、IME接続の中継、完全なXR検証シーン、履歴/JS通信等の製品API。

旧版のAPI/Prefabとの互換性は維持していません。MIT License。
