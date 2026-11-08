# 実装を分離する順序と所有権

2026-11-08 JST。現行ソースを読んで決めた分離計画。以下の仮称は新規APIとして実装済みではない。今回はクラス・namespace・assembly・GUID・JNI・シーンを変更しない。

## 現在混在している責務

| 現行の場所 | 現在の責務 | 分離先の役割 |
|---|---|---|
| GpuWebViewProbe.Open/Close/OnApplicationPause | Texture作成、native ID、Java host、retire、pause時再生成 | GPU targetの所有者とhostの寿命調整 |
| GpuWebViewProbe.UpdateRay/ResetInput/OnHandDeviceChange | 入力元の選択、座標変換、Cancel、SDK reset互換処理 | 入力アダプタ、許可状態、互換処理 |
| ActivityGpuWebViewProbe.Open/Close/RefreshDisplay/UpdatePointer | GPU targetに加え、別Activityの状態とXR focusを判定 | 比較用host adapterと実験シナリオ |
| HostComparisonProbe.RunCase/Update/UpdatePointer | シナリオ、keyboard寿命、20秒gate、ハンド入力、ログ | Trial runner、入力の抑止理由、観測 |
| 各ProbeのBuildPanel/Label/Button | UI生成、レイ可視化、試験ボタン | 検証用presentation |
| VulkanProbe.Host | WebView/View/Surface、IME、focus実験、producer | UI thread上のhostとIME binding、frame producer |
| ActivityGpuProbe.Session | 別Activity接続、編集状態、GPU producer、120秒return | 比較用Activity hostとtrial runner |
| VulkanProbePump | render event発行、retire中Textureの保持 | GPU完了までのresource retirement。寿命規則を維持 |

ファイルの行数や名前だけで移動せず、誰が資源を所有し、どの条件で破棄できるかを先に固定する。

## 保つ所有権

1. Unity側がRenderTextureを作成する。表示部は参照を借りる。
2. Java側はUI threadでWebViewとproducerを停止し、nativeStopを通知する。
3. visible componentを閉じても、pumpはretire中の世代へrender eventを発行し続ける。
4. nativeのVrAliveが0になるまでTextureを破棄しない。
5. フォーカス喪失とアプリのpauseを分ける。キーボードへの入力移譲だけでWebView/接続を破棄しない。
6. 遅いcallbackはsession/trialの世代を照合する。現在の所有者を古い処理から閉じない。

GPU targetの共通化で、この順序を短いDispose呼び出しへ置き換えない。UIのDestroyとGPU完了は別の時刻になる。

## 小さく分ける順序

### 1. 判定と観測

NativeTrial、Application focus、pause、Unity keyboardの状態、入力元の追跡を別々に記録する。現在の20秒gateは診断上の事情であり、製品の編集終了条件ではない。判定を移す場合は既存の許可/抑止条件とジェスチャCancel規則を維持し、方式の改善と混ぜない。

### 2. 検証UI

同じLabel/Button等の生成を検証presentationへまとめる。既存MonoBehaviourとserialized fieldは入口として残す。レイの色・ヒット座標・UV・入力経路を同時に変更しない。

### 3. GPU資源の所有者

Open/Closeで重複するtarget生成とretireの責務を共通化する。単に両Probeを同じ基底クラスへ押し込まず、GPU資源の所有と画面の状態を分ける。VulkanProbePumpのretirement契約は残す。

### 4. Unity/Android host

host生成・UI threadへの要求・閉じる通知を、試験シナリオのtimeoutから分ける。同一Activityと別Activityの差はadapterとして残し、動的に同じWebViewをActivity間移動できるとは仮定しない。

### 5. 配布境界

責務を分けた後で、製品runtimeと検証assembly/Androidモジュールの物理的な分離を行う。Runtime/VulkanProbeのフォルダー名変更だけを分離完了と扱わない。XR/Metaの検証依存をCoreへ持ち込まない。

## 着手の条件と検証

純粋な状態処理はUnityなしで意味のある寿命・世代・取消の検証を行う。Unityオブジェクトを作る変更では6000.4.5f1でimport/compileを確認する手段を先に用意する。現在のクラウドにはUnity Editor実行環境がなく、Coreテスト成功で代用しない。

変更のたびにIME方式まで変えず、構造変更と新方式の実験を別パッチ・別の判定対象にする。実機試験は停止中のまま。まずUnity compileを確認できる基盤を整え、利用者へ同じ手作業を繰り返し依頼しない。
