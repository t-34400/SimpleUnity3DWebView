# HC2: Unity ActivityでのIME・入力属性比較

> **現在は整理フェーズで実機試験を停止しています。** 本書は過去の検証手順です。現在の判断は[CurrentStatus.md](CurrentStatus.md)、共通の取得仕様は[Diagnostics.md](Diagnostics.md)を参照してください。以下の「次の試験」は現在の実行依頼ではありません。

preview.17。別Activity常時前面の製品案・AG3のレイ調整は保留し、同じUnity Activityからの直接入力の成立条件を調べる。WebView本来の入力接続を維持し、入力接続の代理中継やDOMへの文字同期は追加していない。

## 比較するもの

同じGameActivity、Meta XR Feature、描画APIのまま、`xr-keyboard-overlay-enabled`だけをOFF（0）／ON（1）にした二つのビルドを比較する。新しい診断用OpenXR featureの公開build hookから、Unity OpenXR 1.16.1の公式hookと同じキーを設定する。追加のXR拡張は要求しない。

元の実機ログではこのキーがmissingだった。今回のOFFは明示的な0であり、元のmissing状態の完全な再現とは扱わない。成功したビルドのpostprocessで、OpenXRの標準hookが追加値を片付ける。実際のAPK内の値も別途確認する。

| case | 接続先 | 目的 |
|---|---|---|
| `keyboard` | Unity TouchScreenKeyboard Default | 公式Unity経路がboot値に反応するか |
| `native` | 同じActivityの実EditText | 既存の成功条件が再現するか |
| `webview` | 同じActivityのWebView、Window draw ON | WebView直接入力が改善するか。GPUコピーは停止した比較シーン |
| `native-webinfo` | 実EditText、WebViewのinputType/imeOptionsを適用 | View/接続の違いと、この二つの属性の違いを分ける |

`native-webinfo`がコピーするのは、端末のWebViewが本当に生成した`inputType`と`imeOptions`の二つだけ。EditorInfo全体、接続実装、周辺文字、選択範囲等を同一にする試験ではない。

## Unityの準備

1. Unityを閉じ、最新パッチを適用してUnity 6000.4.5f1で開く。
2. **VRWebView > Create Host Comparison Scene**。既存のHC1シーンを使ってもよいが、Activity GPU Probeシーンとは異なる。新規作成時はBuild Settingsのシーン一覧が比較シーンだけになる。
3. **VRWebView > Host Comparison > Use GameActivity**。既に対応済みならManifest本文は変更しない。ARM64、Vulkan／GLES、Meta XR Featureは前回と同じに保つ。Unity Meta Quest Supportを追加で有効化しない。
4. 最初は **VRWebView > Host Comparison > Keyboard Boot > OFF (0)** を選ぶ。
5. APKを`C:\Users\tmiyoshi\Downloads\VRWebView-HC2-off.apk`等にBuild And Runする。

HC2シーンはboot比較featureの未設定・無効状態やGameActivity以外の構成ではビルドを止める。Editorには生成boot.configの検証結果を出す。Unityのbuild配置によって生成段階でファイルを読めない場合は警告にし、次のruntime確認を使用する。

## 実機試行

WSL端末Aでログを開始する。保存先は`C:\Users\tmiyoshi\Downloads`。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
bash Tools/capture_host_probe.sh off
```

WSL端末Bで、以下を**一行ずつ**実行する。毎回アプリをforce-stopして再起動する。各caseの表示・入力を観察し、約25秒待ってから次へ進む。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
bash Tools/run_host_probe.sh keyboard off
bash Tools/run_host_probe.sh native off
bash Tools/run_host_probe.sh webview off
bash Tools/run_host_probe.sh native-webinfo off
```

caseはXR起動とアプリfocusを待って自動開始する。手のレイがなくてもADBから開始できる。native系は20秒で閉じる。Unity keyboardはUnityの更新が続く場合に20秒で閉じる。必要ならシステムの戻る操作を使う。

`off`／`on`引数はAPKを書き換えない。APK内boot.configの実値が指定と違う場合、ログに`bootCheck ... valid=false`とSKIPを出し、caseの自動実行を止める。シーン内ボタンからの手動試行では期待値チェックを代替しないため、比較時は上記ADBコマンドを使う。

各caseで観察するのは、キーボードが実表示されたか、日本語の変換・確定ができたか、Unityの映像が更新していたか、閉じた後にレイと操作が戻ったか。画面の数値の転記は不要。APIのacceptedやUnityのvisibleだけを成功とは判定しない。

終わったら端末AをCtrl+Cで終了。Unityで **Keyboard Boot > ON (1)** に切り替え、同じシーンを`VRWebView-HC2-on.apk`としてBuild And Runする。ログコマンドの`off`と各起動コマンドの末尾`off`をすべて`on`に変えて同じ四つを試す。アンインストール／アプリデータの消去は不要。

## 入力属性を取得できなかった場合

`webview`の本物の非null接続から、同じテストページの`#text`が編集中であることを確認して属性を記録する。文字列や接続オブジェクトは保存しない。入力接続生成のコールバックが終わってからDOMの要素IDを確認し、試行終了・接続再作成・detach後の遅い応答は使用しない。

属性はアプリ内に保存され、アプリ再起動・OFF/ONの通常上書きインストールをまたいで利用できる。WebView providerのパッケージ・バージョンが変わったら無効になる。記録がなければ`native-webinfo`はSKIPとなり、代わりの推測値で試行しない。

同じActivityで属性が得られない場合に限り、既存の成功済み別Activityを一度だけ属性取得用に使える。現在のビルドに応じて末尾をoff/onにする。

```bash
bash Tools/run_host_probe.sh transparent off
# 約25秒待ち、ログに editorProfile saved ... field=text が出たことを確認
bash Tools/run_host_probe.sh native-webinfo off
```

これは別Activityの操作・レイを再検証する試行ではない。取得後のnative-webinfoは必ずアプリを再起動し、Unityと同じActivityで行う。ONビルドでもSKIPだった場合は、この取得後にOFFビルドのnative-webinfoも追加で確認する。いずれからも属性を得られなければ、その比較は未実施としてログを渡す。

## ログと判断

ログは`[VRWebViewHost] HC2`。以下を記録する。

- `setup`と`bootCheck`：実起動Activity、実APKのboot値、overlay宣言、provider。
- WebViewの`connection`：非nullか、inputType、imeOptions、接続クラス。全文や周辺文字は記録しない。
- `editorProfile saved/using`：取得元とprovider、二つの属性。
- `nativeConnection`：実際に送り出した属性と`profileMatch`。TextViewが生成中に属性を調整しても、比較用の最終値をそろえる。
- 従来のfocus、IME要求、Unityフレーム・描画、手のreset・追跡。

| 結果 | 次の判断 |
|---|---|
| ONでWebViewの実IME入力が改善 | 直接入力の候補としてGPUホストへ戻して確認する |
| ONでUnity keyboardだけ改善 | Unity経路の設定問題とWebViewの問題を分ける |
| boot値で変化しない | このキー単独で解決する仮説を終了する |
| native成功、native-webinfo失敗 | この二つの入力属性とホストの組み合わせを調べる |
| native-webinfo成功、WebView失敗 | この二つの属性だけでは説明できない。残りのEditorInfo・接続・focus等へ進む |
| 属性取得失敗／profileMatch不一致 | 入力属性比較の結論を出さない |

入力接続を仲介する試作は、この比較結果を得てから選ぶ。別Activityの常時前面化、追加のWindowフラグ、追跡・フォーカスの偽装で回避しない。

## 比較後

HC2のboot上書きは診断用。通常構成に戻す際は **Keyboard Boot > Disable Diagnostic Override** を選んで再ビルドする。HC2シーンは比較設定を要求するため、通常のシーンに戻す。SDK由来の同じキーは削除しない。ビルド失敗後など元の値が残る疑いがあれば、成功した比較ビルド後に診断を無効化し、生成物の実値を確認する。

XR Hands 1.7.3向けreset同期は従来の検証用処理を維持する。これによる復帰確認をIME表示成功と混同しない。

クラウドで実行する検証はAndroid build/lint、Javaの属性・寿命管理、純C#と配布物整合性。Unity Editorのコンパイル・APK生成・Quest上の効果は実機側の確認が必要。
