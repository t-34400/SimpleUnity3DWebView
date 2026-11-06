# VRWebView

Unity 6000.4 / Meta Quest向けWebViewの新規実装です。配布物は埋め込みUPMパッケージ `Packages/com.t34400.vrwebview` に集約しています。

このプレビューには、セッション・要求応答・ポインタ入力の基盤と、Androidの動画・GPU・IME比較プローブがあります。**VR空間へWebページを表示する製品用GPUバックエンドは未実装**です。キーボード・日本語IME・XRフォーカスの成立も実機で検証してください。

## 構成

```text
Packages/com.t34400.vrwebview/
  Runtime/Core/          Unityに依存しないセッション・ポインタ・応答管理
  Runtime/UnityUI/       uGUIからの座標変換・ポインタ入力
  Runtime/Android/       独立ActivityのIME/GPUプローブへのJNI入口・自動起動
  Runtime/VulkanProbe/   同一Unity ActivityのVulkan/GLES/XR検証・レイ操作
  Runtime/Plugins/Android/  ビルド済み検証用AAR・arm64 Vulkanプラグイン
  Editor/                検証シーン作成メニュー
  Tests/EditMode/        Unityで実行するテスト
  Samples~/ImeProbe/     Package Managerからimportする検証用スクリプト
  Documentation~/       使用方法・検証項目
Android~/                独立したAndroid LibraryのソースとGradle wrapper
Tools/                   ビルド・テスト・unitypackage出力
```

Meta SDKはルートの検証プロジェクトの依存で、本体Runtimeの必須依存ではありません。既存の旧WebView/AAR・古いシーン/Meta64サンプルを取り除き、Unity/OpenXR/Metaの更新済み設定を基準として残しています。旧カスタムAndroid Manifest/Gradleは無効化し、検証AARの最小Manifestをマージします。

## 次の実機検証

H1のWindow draw offでスクロール・入力・動画音声が動くことはQuest 3で利用者が確認済みです。次は **VRWebView > Create GPU XR Probe Scene** で、UnityのVulkan/GLESテクスチャ、同一ActivityでのIME、XR継続動作を検証します。既定のAndroid Vulkan / ARM64は維持しています。OpenGLES3も選択でき、[GLES条件と設計](Packages/com.t34400.vrwebview/Documentation~/GlesSupport.md)に比較時のSDK設定をまとめています。[手順と未確認事項](Packages/com.t34400.vrwebview/Documentation~/GpuXrProbe.md)を参照してください。

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

動画とGPU取り込みの比較には **VRWebView > Create GPU Probe Scene** を使用します。サンプルImportは不要です。Quest上のH0/H1/V1ボタンで、通常WebView、同一Windowからのhardware Canvas描画、VirtualDisplayからのGPU取り込みを切り替えます。手順と診断ラベルは[GPU検証手順](Packages/com.t34400.vrwebview/Documentation~/GpuProbe.md)、根拠は[AOSP調査](Packages/com.t34400.vrwebview/Documentation~/AospRendering.md)を参照してください。

Python 3と.NET SDK 8を使用します。Unityの依存は不要です。

```sh
python3 Tools/run_checks.py
```

Unity側のテストはTest RunnerのEditModeから実行します。純C#テストとUnity実行・実機試験は別の結果として扱います。

GPUプローブの補助処理、同梱動画のdecode、AARへの同梱内容は、JDKとFFmpegを用意して`python3 Tools/run_probe_checks.py`で確認できます。これはGPU描画やIMEの実機成功を示す検証ではありません。動画素材の再生成は`python3 Tools/generate_probe_video.py`です。再生成後はAARも再ビルドしてください。

## Androidソースの再ビルド

JDK 17以上、Android SDK Platform 35、Build Tools 34.0.0、NDK 27.2.12479018、CMake 3.22.1を準備し、`ANDROID_HOME`と`JAVA_HOME`を設定します。Gradle 8.9 / AGP 8.7.3を固定しています。

```sh
python3 Tools/build_android.py
```

Java側にKotlin stdlib・Unityのclasses.jarは不要です。Vulkan経路はNDKでビルドし、arm64ライブラリをUPMへ同梱します。ネイティブの一時ビルド先はプロジェクト外です。

## 配布

主形式はUPMです。現在のプロジェクトではPackages内の埋め込みパッケージとして読み込まれます。別プロジェクトではPackage Managerの「Add package from disk」でpackage.jsonを指定できます。

unitypackageが必要なら:

```sh
python3 Tools/export_unitypackage.py
```

WindowsではユーザーのDownloads、今回指定されたWSL環境では`C:\Users\tmiyoshi\Downloads`へ出力します。`--output`で変更できます。Runtime/Editor/サンプル/文書を`Assets/VRWebView`に集約するアーカイブを生成し、GUIDとファイル本文を照合します。元プロジェクト内にコピーを作らないため、export中に重複GUIDをimportしません。利用先ではUPM版とunitypackage版の片方だけを導入してください。unitypackage版ではUGUI・Input System 1.19.0・XR Hands 1.7.3をPackage Managerで先に導入してください。Unityでの実importは別途確認が必要です。

## 実装範囲

- 実装済み: HTTP(S) URL方針、セッション初期化/破棄、遅延通知排除、要求ID/世代照合、単一ポインタ捕捉/キャンセル、pivot/Overlay Canvas対応の座標変換。
- 検証用実装済み: 通常ホスト/VirtualDisplayのWebView、IME要求、状態表示、複数行・日本語変換・contenteditableを含むテストページ。同一Windowのhardware Canvas／VirtualDisplayからSurfaceTextureへのGPU取り込み比較と、合成H.264/AAC動画。同一Unity ActivityからAHardwareBuffer経由でUnity Vulkan/GLES RenderTextureへ描くGX1検証、XRコントローラーレイ・診断・pause/resize/recreate。
- 未実装: 製品ブラウザバックエンド、IME接続の独自中継、製品用XR UI、fullscreen/MSE/DRMの比較経路、履歴/JS通信等の製品API。

旧版のAPI/Prefabとの互換性は維持していません。自作部分はMIT License。Unity Native Plugin API headersの既存ライセンス表示はそのまま保持しています。

## ハンドでの実機検証

コントローラーがない場合は、XR HandsのMeta Hand Tracking Aimを使ってピンチでクリック・ドラッグできます。基準プロジェクトの手の追跡設定も有効にしています。[HI1手順](Packages/com.t34400.vrwebview/Documentation~/HandInputProbe.md)を参照してください。既存シーンではFlip Image Verticallyをオンにし、画像とクリック位置の上下を合わせてください。Nativeの表示はGX2、入力の表示はHI1です。
