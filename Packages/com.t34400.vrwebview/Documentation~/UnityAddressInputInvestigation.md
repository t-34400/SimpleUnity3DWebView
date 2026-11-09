# Unity URL入力の調査 — 2026-11-10 JST

## 判断

URL入力にはUnity標準InputField / TouchScreenKeyboardを維持する。今回の失敗だけを理由にUnity入力を不採用にしない。Native EditText、代理InputConnection、DOM全文同期への置換は追加しない。preview.44はIME修正済みの版ではなく、U1という独立した対照を追加した版。

## 最新BS1の証拠

利用者はpreview.43でヒット修正を確認し、IMEは依然失敗と報告した。00:46:48.079 prepare → .093 keyboard request → .094 InputField.ActivateInputFieldInternal → .099 UnityPlayerGameActivityのGameActivitySurfaceViewからshowSoftInput → .102 Quest IMEのPHASE_IME_ON_SHOW_SOFT_INPUT_TRUE失敗 → .497 Unity timeout。00:48:12の2回目も同じ段階で失敗した。実bootKeyboard1 / overlayDeclaredtrueを3行で確認。

したがって、今の失敗はURLが選択される前の中断ではない。一方、IME側の失敗段階名だけでは拒否条件までは分からない。hideInput警告の存在だけで原因ともしない。

.108のediting_end cleared=falseはBrowserHost.clearCompletedEditorのevaluateJavascript結果callbackであり、表示要求後に新しいendEditingが実行された証拠ではない。endEditingAsyncはAndroid UI側でclearFocus等を行ってから完了を返すが、OSのIME接続確立まで完了した保証はしない。後者は未確認の競合候補。

最新raw: 583311 bytes / 5253 lines / SHA256 `2147e897d4a624f70a9bb49a97e76a820aeac490e412b594f7f3e57e98d3ff56`。文字列本文は文書・診断へ転載しない。

## Unity公式ソースで確認した範囲

- [uGUI 6000.4.0a4公開ミラー、InputField.cs](https://github.com/Unity-Technologies/uGUI/blob/191f765e9d1b7e419ae7f20502a38b9d9966bfc8/com.unity.ugui/Runtime/UGUI/UI/Core/InputField.cs#L3145): ActivateInputFieldInternalがTouchScreenKeyboard.Openを呼ぶ。Default/Standard/SingleLine設定は現在のURL欄と同じAPI経路。InputFieldは独自のAndroidキーボードを持たない。
- 同ソースではinput.touchSupportedがtrueの場合だけTouchScreenKeyboard.hideInputを書き込む。XRでこの判定がfalseなら、shouldHideMobileInput=falseを設定するだけではグローバルhideInputを書き戻す保証にならない。ただし実機のその値と実際の6000.4.5f1同梱実装は未採取で、これを根因と断定しない。
- 新しいmainにはhideInputを無条件に設定する実装がある。mainは別のUnity版なので、無断でuGUIを入れ替えたり、そのまま使用版の仕様と見なしたりしない。
- [Unity 6000.4系TouchScreenKeyboard C#公開ソース](https://github.com/Unity-Technologies/UnityCsReference/blob/3cd545734a2c41c66c6a4ae2b9af6be3bf1fd1bb/Runtime/Export/TouchScreenKeyboard/TouchScreenKeyboard.bindings.cs): Openの先はネイティブ実装。公開C#だけでGameActivityのEditorInfoやQuest IMEの拒否条件までは読めない。

公開uGUIリポジトリには6000.4.5f1の正確なミラーを発見できなかった。2.0.0というpackage versionだけで異なるEditor同梱実装を同一視しない。

## 既知報告の扱い

[OpenArm #616](https://github.com/enactic/openarm/issues/616)はQuest 3 / v2.7、UnityPlayerGameActivity、overlay_keyboard宣言ありで同じPHASE_IME_ON_SHOW_SOFT_INPUT_TRUEとUnity timeoutを記録している。Activityへの変更・別Unity版での回避報告へのリンクもあるが、投稿者自身が未検証と明記している。この報告はGameActivity/Quest連携を調べる根拠であり、こちらの根因確定や修正版の保証には使わない。Metaフォーラム・公式リリースページはこの環境では取得できず、引用元の本文を直接検証したとは言わない。

## U1: 次の一つの比較

**VRWebView > Create Unity Input Field Control Scene**で生成する。標準UnityEngine.UI.InputFieldそのもの（派生クラスなし）、Default、Standard、SingleLine。WebView/BrowserHost/sessionを一切作らず、Android編集終了待ち、独自キーボード、表示timeoutによる強制終了、フォーカス奪回も持たない。BS1と同じray module/Quest設定を使い、uGUIのOnPointerDown/OnSelect/LateUpdateへ渡す。生成時にkeyboard boot ONを明示する。GameActivity / SDK / rendererを同時に変更しない。

既存BS1シーンは保存され、新規シーンだけが従来のEditorBuildSettingsへ登録される。Unity 6のアクティブBuild ProfileのScene ListがU1かも確認する。Browser sampleの再ImportはU1には不要。

Build And Run前にWSLで採取を開始する（Editor batch testsは前提にしない）。

```bash
cd /mnt/c/source/Unity/SimpleUnity3DWebView
vrwebview_logs="/mnt/c/Users/tmiyoshi/Downloads/VRWebView-U1-$(date +%Y%m%d-%H%M%S)"
python3 Tools/probe.py capture --expect unity-input --label U1 --log-dir "$vrwebview_logs"
```

HMDに「U1: Unity InputField / no WebView」があることを確認し、空の欄を一度選ぶ。表示された場合だけ日本語変換・確定・キーボード終了を行う。失敗したら設定総当たりをせず停止する。Ctrl+C後、同じshellで:

```bash
python3 -m zipfile -c "${vrwebview_logs}.zip" "$vrwebview_logs"
```

- U1が成功: 標準Unity入力がその構成で成立。BS1のページ編集終了・View focus返却・static keyboard stateの差を優先して修正する。
- U1も同じIME側showで失敗: BrowserAddressInput/ページ編集終了処理が必要条件ではない。次は実際の6000.4.5f1のInputField/Unity GameActivityのEditorInfoと、Quest IME/既知修正を照合する。別EditTextへ置換する根拠にはしない。
- U1でshow自体が出ない: selection/InputField.isFocused、keyboard instance、isSupported/isInPlaceEditingAllowed等で起動前の条件を確認する。

ログはready/選択/focus/keyboard status/visible/フレームとtext_changedの発生だけを記録し、入力内容を出さない。TouchScreenKeyboard.visibleやacceptedだけでHMDの実表示を成功判定しない。

クラウドではUnity EditorとQuestを実行できないため、この対照の実機結果とIME修正は未確認。以前のWF1/RT1成功を無効化する調査ではない。
