# GameActivity IME互換修正とBS1 — preview.48

標準Unity InputFieldを維持し、Google games-activity:4.4.0の既存入力接続を補う。preview.47の全文取得・変更通知は、Quest3のU2でシステムキーボード内の表示更新まで利用者が確認した。preview.48は初回表示の順序も修正し、BS1のURL欄で確認する。新しい表示経路とBS1の実機結果は未確認。

## 今回の実装

以前のGoogleコードはshowSoftInputの後にrestartInputを呼んでいた。U1/BS1の履歴でも、表示要求時TYPE_NULLから直後に文字入力属性へ更新されていた。変更後は次の順序にする。

1. 既存Google接続をactiveにし、同じViewのフォーカスを確保する。
2. **restartInputを先に行う。** 元のSurfaceViewがAndroidへ接続を返す際のEditorInfoを観測する。
3. UIキュー上で、その接続の非TYPE_NULL属性と現在の属性が一致し、View/Window focus・attached・IMM activeが成立した場合だけ、一度showSoftInputを呼ぶ。
4. 非同期の接続取得はそのcallbackで再開する。一定時間後の無条件showやポーリングはしない。2秒は未成立の要求を終了する期限であり、表示待機時間ではない。

hide時は元のhide→restart処理を維持する。非active化は待機を取り消す。restart中に閉じられた古いremote wrapperから、Googleが保持する同じ接続を再利用する場合も扱う。表示を拒否されてもshowを繰り返さない。

このcallbackはAndroidが接続を取得した観測であり、Meta IME内部の処理完了ACKではない。今回の変更の効果は実機で確認する。

BS1のURL欄は元から通常のTouchScreenKeyboard経路を使うため、同じAAR修正が適用される。ブラウザUIへ単発showや専用キーボード方式を追加しない。U2の追加show componentは比較用として残すが、今回のBS1試験では使わない。

## BS1の検証手順

1. Unityを閉じ、同じjobへfetch/applyする。未コミット変更は保全する。
2. Unityで **VRWebView > GameActivity IME Compatibility > Enable Diagnostic Patch** にチェックが付いていることを確認する。P47で既に有効ならそのまま。メニューはAndroid targetで表示される。設定はプロジェクトごとのEditor preferenceで、既定は無効。
3. **既存のBS1/RichBrowserシーンへ戻す。** アクティブBuild ProfileのScene Listもそのシーンに合わせる。U2をビルドしない。シーンの作り直し、Browser sample再Import、Editor batchテストは不要。
4. GameActivity/OpenXR/Vulkan/ARM64/Keyboard Boot ON/Requires System Keyboardは従来どおり維持する。
5. Build And Run前にWSLで採取を開始する。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-BS1-IME-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect browser-input-compat --label BS1-IME --log-dir "$vrwebview_logs"
```

6. Build And Run。URL欄を一度選び、初回からキーボードが出るか確認する。出なければ連打や旧比較をせず採取を終了する。
7. 日本語を入力・変換確定し、キーボード内の欄とURL欄の両方が更新されるか確認する。試した文字を削除して `https://example.com/` を入力し、Done/Goで移動する。同じ欄を再編集して数文字の追加・削除を確認する。終了後のレイとページ操作も確認する。
8. Ctrl+Cで採取終了。IME dumpも自動取得する。ZIPと実際の結果を添付する。

```bash
python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"
```

起動時event=connection_patch revision=2 showOrder=connection_then_showが今回のmarker。connection_ready、show_requested/show_result、extract_request/extract_update/selection_updateで接続取得・表示・全文更新を区別する。BS1のUI markerの版番号はsampleの版なので、native側のrevisionと同じ数字とは限らない。

今回のexpectは、同じBS1プロセスのrevision2、URL欄からの要求、受理されたshowを要求する。Unityの表示timeoutは失敗として残す。U2の追加showで成立したログは今回の成功根拠にしない。acceptedや通知だけではHMDの実表示成功と判定しない。入力文字列・URL・文字列hashは追加ログへ出さない。

## 全文取得・変更通知

元のGoogle InputConnectionとEditableを使う。getExtractedTextは既存の文字列・選択・変換状態を返し、MONITOR要求tokenを保持して、文字/選択/変換領域の更新をUIキューから通知する。batch中はまとめ、restart/closeで古い監視を解除する。InputFieldへの全文コピー、別EditText、入力接続代理、表示後のフォーカス奪還は追加しない。

P47の実機ログでは、監視付き抽出要求3回、更新通知16回、98回のUnity値一致、Done3回を確認。利用者がキーボード側の表示更新も確認した。ただし抽出と選択通知を合わせた修正であり、どちら一つだけで十分かを切り分けたものではない。全IME/全lifecycleの保証とはしない。

## post processと適合確認

IPostGenerateGradleAndroidProjectは、生成unityLibraryにある明示的4.4.0依存一つだけをUnity標準libs/flatDirの修正版AARへ置換する。直接files(AAR)依存によるRelease lint失敗を避ける。Unity標準GradleテンプレートのflatDirリポジトリを利用する。

- 同梱AAR/provenanceのSHA256と、Gradleの照合専用configurationで解決した公式AARのSHA256を確認する。
- 未対応の版・宣言変更・重複、別経路からの元Googleモジュールのcompile/runtimeへの追加は拒否する。
- 照合用の元AARはcompile/runtimeへ追加しない。同名クラスを二重に取り込まない。
- Editor内のAARは全プラットフォームの自動取り込みを無効にし、有効化した生成プロジェクトへだけコピーする。Windowsの読み取り専用UPMキャッシュからコピーしても生成側は更新可能にする。
- 解除は同じメニューのチェックを外して再ビルドする。自分の生成Gradleブロックと依存を戻し、生成libs内の専用AARを除去する。GradleキャッシュとUnity Editorインストール先は変更しない。

対象は確認済みGoogle4.4.0。source AAR SHA256は3a8b0887ac55a0a6ab56021ced7353a250539eac811278b74bccd8a5b678d9c9。利用者採取のクラスと一致した。将来のUnity/Google更新で自動適用し続けない。

## 生成と検証

利用者のUnityビルド中にbytecode patchを実行しない。maintainerがTools/build_gameactivity_compat.pyで元AARとクラスSHAを確認し、ASM9.6で限定したクラスを変更して決定的ZIPを生成する。修正ツールとJava helper、元/変更クラスSHA、保存entryのSHAを同梱する。native/prefab・manifest・資源のbytesは維持する。Google由来はApache-2.0、追加コードは元リポジトリのMIT。パッケージ全体のライセンスは変更していない。

```text
python3 Tools/build_gameactivity_compat.py --source-aar <Google4.4.0.aar> --android-jar <SDK35/android.jar> --asm <asm-9.6.jar> --asm <asm-commons-9.6.jar> --jdk <JDK21>
python3 Tools/check_gameactivity_compat.py
```

Android~/gameactivity-compat/checksは実際の修正版Google接続をRobolectricで試験する。表示順序・非同期callback・TYPE_NULL・取消・期限・再編集・拒否後の非再試行と、P47の入力/全文/選択/変換/batch/token/closeの試験を実行する。APK/R8/nativeリンクとpost processの依存照合・解除も検証する。これらはUnity Editorの実コンパイルやQuestの表示結果を代替しない。
