# 共通診断ツール

現在は実機試験を停止中。以下は再開時の操作仕様と保守用の説明であり、新しい試行の依頼ではない。コードはすべてパッチで配布する。個別のDownloadsスクリプトを新たな標準入口にはしない。

## 一つの取得器

Python 3.10以上の標準ライブラリのみ使用。`Tools/probe.py`の`doctor`、`capture`、`summarize`へまとめる。既存の`capture_host_probe.sh`と`capture_activity_gpu_probe.sh`は互換入口として同じcaptureへ委譲する。launcherとUnity/Androidの実験コードは今回変更しない。

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

# 再開時だけ使用するライブ採取。別端末から既存launcherを実行する
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
