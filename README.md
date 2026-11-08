# VRWebView

Unity 6000.4 / Meta Quest向けWebViewの再設計プロジェクトです。現在は情報整理・設計見直し・技術的負債の解消フェーズで、追加の実機試験は停止しています。製品用バックエンドの完成・日本語IMEとの両立はまだ宣言していません。

入口は[現在の状態](Packages/com.t34400.vrwebview/Documentation~/CurrentStatus.md)です。

- [証拠台帳](Packages/com.t34400.vrwebview/Documentation~/EvidenceLedger.md)：実機観察、提供ログ、仮説、未確認事項。
- [Editor検証](Packages/com.t34400.vrwebview/Documentation~/EditorValidation.md)：batchmodeの起動・XML判定。実Editor実行は未確認。
- [GPU寿命](Packages/com.t34400.vrwebview/Documentation~/GpuLifetime.md)：世代と退役条件の状態処理。実体への接続・実GPU確認は未実施。
- [設計の見直し](Packages/com.t34400.vrwebview/Documentation~/ArchitectureReview.md)：残す境界と採用・保留の判断。
- [技術的負債](Packages/com.t34400.vrwebview/Documentation~/TechnicalDebt.md)：今回の整理範囲と残る完了条件。
- [共通診断仕様](Packages/com.t34400.vrwebview/Documentation~/Diagnostics.md)：一つのcollectorと旧コマンドの互換入口。
- [転送基盤の統合課題](Packages/com.t34400.vrwebview/Documentation~/FoundationIntegration.md)：WSL修正のmain統合結果と通常の適用手順。

## ソースと配布

埋め込みUPMは`Packages/com.t34400.vrwebview`、Androidソースは`Android~`。`Runtime/Core`は基礎実装で、現在のGPU/IMEプローブは製品backendへ未接続です。プローブは比較資料として保存し、過去の「次の試験」は実行指示として扱いません。

unitypackageのexportは`python3 Tools/export_unitypackage.py --output <保存先>`。UPMと同時にimportして同じGUIDを重複させないでください。既存ソース・パッチの転送にはpatch-exchangeを使います。HC2の抑止判定と観測はHostPointerObservation、時間条件はHostTrialTimingへ切り出しました。起動スクリプトも共通Tools/probe.pyへ委譲します。既存UnityのGUID、namespace、assembly、JNI、シーン配置、AAR/.soは保持しています。

## ローカル検証

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s Tools/ToolTests -v
python3 Tools/run_checks.py
```

共通診断の合成fixture、純C#、配布物整合性の検証です。Unity Editorのcompile/import/buildやQuest上の実表示を意味しません。

## Androidツールチェーン

Java 17以上、Android SDK Platform 35、NDK 27.2.12479018、CMake 3.22.1、Gradle 8.9 / AGP 8.7.3。Androidソースを変更した際は`python3 Tools/build_android.py`でAARとsource/provenanceを更新します。今回の文書・診断ツール整理で再ビルドは不要です。
