# ハンドトラッキングでの検証（入力HI1）

## 対応する操作

手のレイをページや下部のボタンへ向け、親指と人差し指のピンチでクリックする。ピンチを保持したまま上下へ動かすと、通常のWebViewのタッチドラッグとしてスクロールできる。IME / Focus / Reload / Recreate / Resize / Flip Yも同じレイで操作する。手モデルの描画は追加せず、入力用のレイを表示する。

`Pointer Mode=Auto`は追跡中の右コントローラーを優先し、いなければ右手、次に左手を使う。左手を明示的に使う場合はInspectorで`Left Hand`を選ぶ。押下中は追跡できている入力元を保持し、別の手やコントローラーが割り込まない。追跡喪失やsystem gestureでは押下をキャンセルする。再び追跡した時点で既にピンチ中なら、一度離してからつまみ直す。

SDKの`XR_FB_hand_tracking_aim`に基づくMetaAimHandのposeとindexPressedを使う。関節から独自にrayを推定せず、Unity XR Hands 1.7.3 / Input System 1.19.0の入力adapterを追加した。描画backendやAndroid WebView/IMEは同じ単一ポインター経路を使う。

## プロジェクト設定

今回の基準プロジェクトでは次を有効にした。

- Android OpenXR: **Hand Tracking Subsystem**
- Android OpenXR: **Meta Hand Tracking Aim**
- OculusProjectConfigのHand Tracking Support: **Controllers And Hands**

既にインストールされているXR Hands 1.7.3とInput System 1.19.0をUPM packageの明示依存にした。追加でOculus XR PluginやXR Interaction Toolkitを入れる必要はない。

Quest本体のハンドトラッキングも有効にし、手が見える状態で試す。初回の比較はVulkan/ARM64を維持できる。ハンド設定の変更をAPKへ反映するため再ビルド・再インストールが必要。今回のパッチでnativeライブラリ/AARは変更していない。

## 画像の上下とクリック座標

GX2の実機画像で、Vulkan初期化とページ画像の取り込みが成立する一方、ページが上下逆になっていることを確認した。動画は停止中の画像なので、再生やIMEの成功を意味しない。

新規コンポーネントの`Flip Image Vertically`はオンを既定にした。**既存シーンでは保存済みのオフが維持されるため、Inspectorでオンにしてからビルドするか、実機でFlip Yを押す。**

旧クリック処理は表示のUV反転と逆のY座標を使っていた。HI1ではRawImageが実際にsampleするUVを計算して、そのAndroid viewport位置へイベントを渡す。これにより上下反転・非反転とも、見えている位置とクリック位置を合わせる。UVのcropや横反転も同じ座標変換を使い、ページのscroll量は足さない。

## 検証順序

1. Unityを閉じてパッチを適用し、開き直してPackage Managerの解決・コンパイルを待つ。
2. 既存GPU XR ProbeのInspectorで`Pointer Mode=Auto`、`Flip Image Vertically=On`にする。
3. 上記のAndroid OpenXR二項目とControllers And Handsを確認する。設定のローカル変更をstashから戻した際に競合した場合は、その変更を確認する。
4. 再ビルド・インストールする。native表示は`GX2 backend=Vulkan`のまま、入力欄に`input=HI1`が出る。
5. 手をレイが向く姿勢にすると、`source=RightHand`または`LeftHand`、`tracked=True`となることを確認する。頭の近くや掌を顔へ向けたsystem gestureではrayを抑制する。
6. ピンチでPlayを押し、動画の焼き込みVIDEO frameが進むことと音を確認する。続いてピンチ保持のドラッグでスクロールし、同じ入力欄を正しく選べるか確認する。
7. 入力欄を選び、必要なら下部のIMEをピンチで押す。日本語の変換・確定・Focusからの復帰を試す。
8. ピンチ中に手を隠し、再び出しても勝手にクリックされないこと、一度離してつまみ直すと操作できることを確認する。

## 診断

- `input=HI1`: 今回の入力と座標修正を含む。
- `source`: 選択中の入力元。Noneなら有効なray poseがない。
- `tracked` / `pressed`: 選択した入力元の有効性とピンチ/トリガー状態。
- `handAim R=present/tracked L=present/tracked`: Aim deviceが作られたか／UIで使えるposeがあるか。deviceがあっても手が見えていなければtracked=Falseになり得る。
- `controller=False`: コントローラーが手元にない状態では正常。ハンド操作成功の判断にはsourceとhandAimを見る。
- `flipY`: 実際に使用する表示反転。

## 確認範囲

ローカルSDKソースのMetaAim.cs / OpenXR/MetaHandTrackingAim.csでpublic APIとdevice更新経路を確認した。SDKのindexPressed（しきい値0.8）、Computed/Valid/SystemGesture flagsを使用する。Hand Trackingのmanifest生成とMeta SDKのControllers And Hands設定も確認した。

C#基盤テストで、画面上の点とsource UVの対応、反転/crop/範囲外座標、ピンチのdown/move/up、追跡喪失・入力元切替・focus復帰時のghost click抑制を確認する。Unity Editorでのcompile/importとQuestの手追跡・ピンチ・動画・IMEは未実行。
