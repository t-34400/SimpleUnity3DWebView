# GameActivity IME互換修正の診断 — preview.47

U2の標準InputFieldを維持し、Google games-activity:4.4.0の既存入力接続へ全文取得と変更通知を補う。Questでの修正効果は未確認。製品既定では無効で、UnityのプロジェクトごとのEditor設定で有効化する。

## 今回の根拠

利用者の生成Gradle宣言とキャッシュに4.4.0を確認。採取InputConnection.classのSHA256は56689dabd6edf7ea80a4ed69bfc58b9b4454017497f18cd1c82fce06a1e3424c。getExtractedTextは親の既定実装へ委譲し、IME向け全文更新通知は見当たらない。AOSP14の親実装はnullを返す。参考4.4.2もこの点は同じだった。

P46の103サンプルはTouchScreenKeyboard.textとInputField.textが一致したが、利用者はキーボード内の入力欄には既存文字しか見えず、追加入力は候補とUnityだけに出ると報告。通常のブラウザは正常。この差を調べる互換修正であり、MetaのIMEが実際に全文抽出を要求しているかも記録する。null応答はAPI上認められ、原因を静的解析だけで確定しない。

## 最小の検証手順

1. Unityを閉じ、同じjobへfetch/applyする。未コミット変更は従来どおり保全する。
2. Unityで **VRWebView > GameActivity IME Compatibility > Enable Diagnostic Patch** を一度選び、チェックが付いたことを確認する。Android build targetでのみメニューが出る。
3. **現在のU2シーンをそのまま使う。** シーン作り直し、Browser sample再Import、Editor batchテストは不要。アクティブBuild ProfileのScene ListはUnityInputFieldOrderProbe、GameActivity/OpenXR/Vulkan/ARM64/Keyboard Boot ON等は従来どおり。
4. Build And Run前にWSLで採取を開始する。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-U2-compat-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect unity-input-compat --label U2-compat --log-dir "$vrwebview_logs"
```

5. Build And Run。日本語を入力・変換確定し、**キーボード内の欄にも追加入力が表示されるか**を確認。Doneで閉じ、同じ欄を再編集して数文字追加・削除する。Done後の内容とレイ復帰も確認する。
6. Ctrl+Cで採取終了。IME dumpも自動取得する。ZIPを作り、実際の表示結果とともに添付する。

```bash
python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"
```

起動時event=connection_patch revision=1が、今回の修正が実装された接続のマーカー。event=extract_request/monitor、extract_update、selection_updateで実際の要求・通知を区別する。全文抽出の要求がない場合も、その事実をnotesへ残す。ログだけでシステム入力欄の表示成功とはしない。入力文字列やURLは追加ログへ出さない。

## 修正と適用の分離

- 修正対象はGoogleのInputConnectionとSurfaceViewの接続返却経路の2クラス。同じInputConnectionとEditableを使い、別EditText/代理入力接続へ移さない。nativeが保持する型・元のメソッド・nativeバイナリを維持する。
- 全文抽出は既存Editable/選択範囲を返す。MONITOR要求のtokenを保持し、文字/選択/変換領域の変化後にupdateExtractedTextとupdateSelectionを送る。batch中は通知をまとめる。
- restartInput/closeで古い監視を解除し、同じGoogle接続が再利用される場合もSurfaceViewの返却時に再開できるようにする。通知はViewのUIキューで行う。InputFieldへの文字列コピー、フォーカス奪還は行わない。
- 初期showとrestartInputの順序は今回変更しない。U2の既存単発追加showを使って、表示後の更新に効果があるかを先に確認する。

IPostGenerateGradleAndroidProjectは、生成unityLibraryの明示的4.4.0依存一つだけを、Unity標準のlibs/flatDir経由の修正版AARへ置換する。直接files(AAR)依存によるRelease lint失敗を避ける。標準GradleテンプレートのflatDirリポジトリを利用する。次の条件では停止する。

- 依存の版・宣言形式が対応対象と違う、または宣言が重複する。
- 同梱AARと生成記録のSHA256が一致しない。
- Gradleが照合用に解決した公式AARが確認済みSHA256と違う。
- 通常のcompile/runtime依存から、元のGoogleモジュールが別途入ろうとする。

照合用の公式AARはcompile/runtime classpathへは入れない。重複する同名クラスを作らない。GradleキャッシュやUnity Editorインストール先を変更しない。修正版はEditor内で全プラットフォームの自動取り込みを無効にしたAARとして保持し、有効化したビルドの生成プロジェクトへだけコピーする。

解除は同じメニューのチェックを外して再ビルドする。再利用された生成Gradleに自分のブロックが残っている場合も、元の4.4.0依存へ戻す。BS1などへの製品適用は、この診断後に判断する。

## 再現可能な生成

利用者のUnityビルドではパッチ処理を実行しない。maintainerがTools/build_gameactivity_compat.pyで公式AARの厳密SHAを確認し、ASM9.6で限定したクラスへ修正を加え、ソースからhelperをコンパイルして決定的ZIPとして生成する。source AARのSHA256は3a8b0887ac55a0a6ab56021ced7353a250539eac811278b74bccd8a5b678d9c9。native/prefab・manifest・既存資源のbytesを保持する。

```text
python3 Tools/build_gameactivity_compat.py --source-aar <Google4.4.0.aar> --android-jar <SDK35/android.jar> --asm <asm-9.6.jar> --asm <asm-commons-9.6.jar> --jdk <JDK21>
python3 Tools/check_gameactivity_compat.py
```

原クラスの本文をビルドごとに文字列置換する方式ではない。修正ツール、helperのJavaソース、ソースSHA、元/修正クラスSHA、保存したentryのSHAを同梱し、変更箇所をレビューできるようにした。Google由来部分はApache-2.0、追加コードは元リポジトリのMIT。パッケージ全体のライセンスは変更していない。

## 検証の範囲

Android~/gameactivity-compat/checksは実際の修正版Google接続をRobolectricで試験する独立プロジェクト。Google listenerへの入力伝達、全文取得、変換中、選択/削除、batch、監視token、close/restart/再開を対象とする。Android APKとprefab/nativeリンク、R8有効ビルドも別に確認する。これらはUnity Editorの型解決・実Playerビルドや、Quest IMEの表示結果を代替しない。
