# 共通診断ツール

現在の一試行は[WF1](WindowImeGuard.md)。以下は共通の操作仕様と保守用の説明。コードはすべてパッチで配布する。個別のDownloadsスクリプトを新たな標準入口にはしない。

## 一つの取得器

Python 3.10以上の標準ライブラリのみ使用。`Tools/probe.py`へ接続確認・採取・解析・起動をまとめる。既存のcaptureシェルとrunシェルは互換入口として共通ツールへ委譲する。Android/nativeの実験コードは変更していない。

- 全プローブ共通のAndroidタグを取得し、sed等で本文を捨てない。
- 対象外プローブの診断行が来てもrawログには保存する。期待したプローブが不在なら失敗として知らせる。
- ログを消去しない。`get-state`成功後にライブ取得を始める。起動前から取得する。
- raw `.log`、`.summary.json`、`.adb-stderr.txt`を保存する。
- 0バイト、プローブ診断行不在、対象プローブ不一致、ADB失敗、boot/属性不一致は終了コード2。保持したファイルで原因を確認する。
- ファイルのoff/onラベルは操作者のラベル。APK実値はsetup/bootCheckから読む。
- keyboard visible/acceptedやOpenXR FOCUSEDを、HMD内のキーボード/レイの実表示成功とは判定しない。

## 設定とコマンド

ADBは`--adb`、`VRWEBVIEW_ADB_EXE`、インストールされたUnity EditorのSDK、PATHの順で解決する。複数端末は`--serial`または`ANDROID_SERIAL`で明示する。ログ先は`VRWEBVIEW_LOG_DIR`または`--log-dir`。WSLで`/mnt/c/Users/tmiyoshi/Downloads`が存在すれば既定で使用し、それ以外は実行ユーザーのDownloadsへ保存する。

```sh
# 読み取り専用の接続チェック。アプリを終了・起動しない
python3 Tools/probe.py doctor

# 再開時だけ使用するライブ採取。別ターミナルからlauncherを実行する
python3 Tools/probe.py capture --label host-off --expect host

# 既存ログを端末なしで検査する
python3 Tools/probe.py summarize path/to/log --expect host
```

`capture`はCtrl+Cで終了する。出力先と診断件数を表示する。空ファイルが作られても正常扱いにせず、stderrとsummaryを一緒に確認する。rawには指定タグのUnity/Androidメッセージも含むため、プローブの行だけのファイルとは異なる。

## summaryが示すもの

行数、プローブの種類、記録されたcase名、PID、実boot値、期待値確認、入力属性、nativeTrial等のレイ抑止状態、OpenXR遷移を抽出する。試行順やファイル名からcase/boot値を補完しない。記録されなかったものは空のままにする。

利用者による実表示・入力の観察は[証拠台帳](EvidenceLedger.md)へ別に記録する。collectorだけで日本語変換・動画・レイ操作の成功を確定しない。

## ローカル検証

```sh
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s Tools/ToolTests -v
```

合成ADBのログ出力・エラーと一時ファイルで検証する。実機は使用しない。POSIX実行fixtureのテストはWindowsではskipし、純粋な解析テストは実行できる。これらの成功をWindows/WSL実環境やQuestでの採取成功とは言い換えない。


## HC2の判定/観測とEditor検証

新しいHC2コードは`pointerObservation=Ready`と、`pointerBlockReasons`、`rayPolicyPermitted`、`raySuppressionReason`を記録する。20秒trialによる抑止、Unity keyboardの状態、追跡元なしを分けて解析する。古いログではこれらを推測で埋めない。rayPolicyPermittedはHMD実表示の判定ではない。

Editor用には`editor-doctor`と`editor-check`を追加した。同じTools/probe.pyから利用し、ADBやHMDを必要としない。詳細は[EditorValidation.md](EditorValidation.md)。これは今すぐの試行依頼ではない。

## 起動処理の共通化

実機試験の停止は継続。以下は保守上の仕様で、追加試行の依頼ではない。

`launch-host [case] [off|on]`と`launch-activity [mode]`を追加した。旧`bash Tools/run_host_probe.sh ...`と`bash Tools/run_activity_gpu_probe.sh ...`も同じ引数で委譲する。case/mode、default、Intent extraの名前と値は保持する。出力はJSONとなり、`status=launch_requested`、`runtime_outcome=not_observed`を返す。起動要求の成功から、scene/case実行・boot実値・キーボードやrayの実表示は推定しない。

ADBとserialの解決はcapture/doctorと共通。直接呼ぶ場合の`--adb`と`--serial`はsubcommandの前へ指定する。packageは`--package`、`VRWEBVIEW_PACKAGE`、既存defaultの順。

1. 引数・packageを確認する。不正ならADBを呼ばない。
2. 接続を確認し、launcherをresolveする。指定package内のUnityPlayerGameActivity/UnityPlayerActivityだけを受け付ける。
3. 確認できた対象packageのみforce-stopし、元のIntent extrasで起動する。

resolve・force-stop・startの各処理に30秒のtimeoutを設け、非0終了だけでなく終了0で返るAndroidのError/Exception等も失敗にする。既存ActivityへのIntent配送を示す`Activity not started`も新しい比較試行の成功にはしない。start失敗/timeout後に自動再試行はしない。起動要求が部分的に実行された可能性は残る。

build/install、scene選択、boot設定の変更、logcat消去・自動採取は行わない。`off/on`は従来どおりAPKのboot設定に対する期待値であり、設定変更ではない。合成ADBで全case/mode・旧入口・serial・改行・各段階の失敗を検証し、Questでの起動は未確認。


## WF1の直接IME回避

`capture --expect window-ime --label WF1`はWF1のinstall、対象provider、直接経路、実編集接続と操作の記録を確認する。HOLD_LOSS発生や入力成功そのものを採取成立の条件にしない。unsupported providerや旧APKは明示する。

このexpectだけmain/system/eventsの三バッファーとWindowManager/InputDispatcher/input_focusを追加する。システム記録が出ない端末もあるため、その不在だけで失敗とはしない。DOMイベントは固定ページの数値だけで、本文は保存しない。古いIP1/IP2のsummaryとは区別する。
