# Vulkan初期化と早期ロード（GX2）

## 実機で報告された状態

GX1で`backend=none`、`Required extensions not enabled: restart with preloaded native plugin`が報告された。この状態はWebViewを作る前の失敗で、動画描画・IMEの成否を示さない。

旧実装は「今回のhookで拡張を有効化したデバイスが確認できない」という条件を、一つのエラーにまとめていた。遅い読み込み・hook不発・本当の拡張不足・デバイス作成の失敗を区別できなかったため、アプリの再起動だけで直るとは判断できない。

## 修正

- グラフィックス初期化前の読込に対応する`GfxPlugin`命名へ変更し、`libGfxPluginVRWebView.so`をpreload指定する。JavaのSystem.loadLibrary、C#のP/Invoke、CMakeの出力、配布物を同じ一つのライブラリ名へ揃える。旧`.so`を重複同梱しない。importerのGUIDは維持する。
- V2がある場合は`AddInterceptInitialization(..., priority=0)`で登録する。従来のV1は唯一の最高優先度slotを占有・置換する仕様なので、V1しかない場合だけ使う。V2登録が拒否されたときにV1へ切り替えてほかのpluginを置き換えない。
- 戻り値を確認し、登録・callback実行・vkCreateDevice呼出・元設定へのfallbackを表示する。
- 必要な拡張が有効になったVkDeviceを個別に記録する。別pluginがデバイスを作っても、最後の一台だけを見て判断しない。
- 端末の拡張一覧に必要項目がない場合と、追加要求を伴うvkCreateDeviceが失敗した場合を分けて表示する。

Vulkanで利用する`VK_ANDROID_external_memory_android_hardware_buffer`と`VK_EXT_queue_family_foreign`、GLES側の取り込み・入力経路は同じ。初期化失敗を無視して転送を開始する変更は行っていない。

## 再検証

1. Unityを閉じて追加パッチを適用し、再度開く。
2. 今回はnativeライブラリ名が変わるため、Unityの**Clean Build**で生成物を作り直す。プロジェクトのAssetsやLibrary全体を手動削除する必要はない。
3. 既存シーン・Vulkan・ARM64のままでビルドし、APKをインストールしてアプリを完全終了後に起動する。
4. `GX2`であることを確認する。`backend=Vulkan`なら動画・入力の検証へ進む。`backend=none`なら、その下の理由と`vkHook`の行を記録する。

Unityのworker終了エラーでBuild Failedになっても、APKが今回のビルドで生成・コピーされている場合は、そのAPKを手動でインストールして比較できる。ただしUnityのビルド結果自体を成功扱いにはしない。

## 検証と根拠

Unity公式NativeRenderingPlugin revision `522254181faf188efa8b50c3e3bf6fce720b26e4`の[RenderAPI_Vulkan.cpp](https://github.com/Unity-Technologies/NativeRenderingPlugin/blob/522254181faf188efa8b50c3e3bf6fce720b26e4/PluginSource/source/RenderAPI_Vulkan.cpp)はV2のAddInterceptInitializationをpriority=0で使い、V1のみの場合に旧APIを使う。

同revisionのIUnityGraphicsVulkan.hには、登録をデバイス初期化前に行うこと、V1が唯一の最高優先度slotを置き換えることが明記されている。

[公式サンプルrepoのissue #24](https://github.com/Unity-Technologies/NativeRenderingPlugin/issues/24)には、Load on Startup指定でもビルド済みアプリではVulkan instance作成後にロードされる事例と、GfxPlugin / XR PreInitへの言及がある。これは利用者報告であり、今回のQuest上での読込順を実測した証拠ではない。GX2のカウンターで実際の経路を確認する。

模擬テストではV2併用登録・拒否時にV1を上書きしないこと・旧interfaceでのfallback・未実行callbackの診断・拡張不足・デバイス作成fallback・複数デバイスの追跡を確認する。ビルド済みライブラリの名前・SONAME・Java/PInvoke名・preload metadataの整合性も確認する。Unity/Quest上の実際の読込順と解消は未検証。
