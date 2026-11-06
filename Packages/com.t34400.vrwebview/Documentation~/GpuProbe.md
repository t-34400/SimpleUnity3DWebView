# 動画・GPU・IMEの比較プローブ

このプローブは独立したAndroid Activityで動く。UnityのテクスチャやOpenXRレイヤーへ描く本番backendではない。独立Activityへ切り替わる間、Unity側は休止し得る。

## 起動

1. Unity 6000.4.5f1で`VRWebView > Create GPU Probe Scene`を実行する。サンプルのImportや再Importは不要。
2. 生成された`Assets/VRWebViewPlayground/GpuProbe.unity`を、使用中のAndroid Build ProfileのScene Listへ登録する。
3. `GPU Probe`の`Launch On Start`をオン、`Startup Mode`を`Normal Window`にする。
4. QuestへBuild And Runする。画面のH0/H1/V1ボタンで方式を変更できる。切り替えるたびにページと再生状態は初期化される。

Android側のGLES表示はUnityのGraphics APIから独立している。診断にUnity側のGraphics APIも表示するが、UnityがVulkanでも、このプローブでUnity/Vulkanへの画像共有が検証されたことにはならない。

## 比較する方式

| ボタン | 描画・入力の構成 |
|---|---|
| H0 Normal | ホストWindow内の通常WebView。素材・codec・IMEの比較基準 |
| H1 Host GPU | 同じWindow内のWebViewを`Surface.lockHardwareCanvas()`へ描き、SurfaceTextureをGLESで表示 |
| V1 VD GPU | Presentation内のWebViewをVirtualDisplayへ描き、SurfaceTextureをGLESで表示 |

H1は上段が元のWebView（SOURCE）、下段がGPU取り込み画像（GPU OUTPUT）。下段へのタッチを元のWebViewへ転送する。V1はGPU OUTPUTだけを表示する。余白部分では新しいタッチを開始しない。複数指の操作はキャンセルし、単一指で比較する。

H1の`Window draw: off`は、元WebViewの通常Windowへの`draw()`を止める。WebViewはattach・VISIBLE・サイズを保ち、Surfaceへのhardware Canvas描画を続ける。`GONE`やalpha=0や画面外配置の試験ではない。最初はonで比較し、次にoffにして、GPU画像だけでも動画・入力が成立するかを見る。元領域が即座に完全消去されることや、動画overlayまで停止することは仮定せず、残った表示も観測結果へ含める。

## 最初の試験

1. H0で`Play / Pause`を押す。動画の焼き込み`VIDEO frame`番号と色が動き、音が出ることを確認する。pause、seekも確認する。
2. 動画を再生しながら入力欄へ日本語を入力し、変換・確定する。
3. H1へ切り替え、同じ操作を行う。**下段のGPU OUTPUT内の動画**でフレーム番号が進むことを確認する。上段だけで動いていても合格ではない。
4. 下段から入力欄を選択し、日本語の変換・確定を確認する。必要なら、入力欄を選んだ後で`Request IME`を押す。
5. `Window draw: off`へ切り替え、下段だけを操作して同じ確認をする。
6. V1でも同じ試験をする。`windowFocus=false`のままなら、その値とIME非表示を記録する。IME要求の反復は不要。

動画の`currentTime`、ページの`frameCallbacks`、ネイティブの`gpuFramesAcquired`の増加は、取得画像に動画が入った証拠にはならない。必ずGPU OUTPUTの焼き込みフレーム番号を目視する。

## 診断表示

診断欄は縦にスクロールできる。ラベルは次で固定する。

- `viewFocus` / `windowFocus`：Android ViewとWindowのフォーカス。
- `immActive`：公開APIの`InputMethodManager.isActive(web)`。システム内部のserved viewそのものを取得する値ではない。
- `showRequestAccepted`：IME表示要求の返り値。キーボードが見えた証拠ではない。
- `viewDisplayId` / `contextDisplayId`：ViewとContextのDisplay。API 29では後者を取得せずn/aにする。
- `attached` / `shown` / `visibility` / `size`：WebViewの接続・可視状態。
- `webHardware` / `targetCanvasHardware`：Viewと描画先Canvasのhardware acceleration。後者はH1だけ。
- `connectionRequests` / `nonNullConnections`：InputConnection生成の呼び出しと非nullの回数。接続を保存・ラップしない。
- `canvasSubmissions` / `gpuFramesAcquired` / `textureTimestampNs`：描画指示とGPU consumer側の観測。前者はH1だけ。どれも動画描画の合否ではない。
- Window flags、API level、Android version、WebView provider/version、GLES、Unity graphics、起動元Activity、`failure`。

入力本文・password・周辺テキストをUnityやLogcatへ送らない。ページ上の入力イベント表示も要素名・選択範囲・イベント種別だけ。JavascriptInterfaceを設けず、任意のサイトへ移動しない。

## 検証素材と制約

同梱の8秒・720p/30fps・H.264/AAC動画は、FFmpegのテストパターン、焼き込みフレーム番号、880 Hz音から生成した合成素材（CC0）。ネットワークを使わず、固定HTTPS originの要求をアプリ内で処理する。単一byte-rangeを返すため、seekを比較できる。映像と音を比較する素材であり、精密なA/V同期測定用ではない。

ページにはDOM更新、CSS animation、Canvas2D、WebGL、input、textarea、contenteditable、passwordも含む。ページ側のWebGL利用可否とGPU画像への取り込みは別に判定する。

この段階はinlineの非保護動画を対象にする。fullscreen要求は診断へ記録して拒否する。MSE、DRM、fullscreenの取り込みは未実装／未検証であり、通常動画の成功から対応済みとはしない。

H1は約30Hzの描画指示を送る診断実装である。最終性能、動画全般、provider間の互換性、QuestのIME表示、XR共存は保証しない。GPU consumerの通知は専用HandlerThreadで受け、GL更新は所有Contextのスレッドで行う。休止・方式変更時はproducerを止めてからconsumerを解放する。休止／復帰時はセッションとページを作り直す。

次のUnityテクスチャ／OpenXR統合は、この比較結果を見てから行う。[AOSP調査の根拠](AospRendering.md)も参照。
