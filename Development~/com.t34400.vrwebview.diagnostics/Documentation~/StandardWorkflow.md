# 通常の更新・適用手順

2026-11-08 JST。現在は整理フェーズで実機試験を停止している。以下は次回更新時の仕様であり、今この操作や再ビルドを依頼するものではない。

## 配布側と適用側

クラウドは既存ジョブへcheckpoint/validate/publishする。手元はai-patch-exchangeのmainを更新し、そのCLIで`--job-ref`をapplyする。プロジェクト別のDownloadsスクリプトを増やさない。

WSLでのGit設定・改行指定は利用者のWindows作業ツリーに合わせるために必要で、CLIを修正ブランチから取得するための回避策ではない。過去のapply記録に`git`が記録されているので、継続時も同じ`--worktree-eol git`を指定する。

## このプロジェクトの適用例

Unityを閉じ、対象プロジェクトの作業中の変更を通常のGit運用で保持し、apply可能な状態にしてから使用する。CLIは対象の未変更状態を検査する。

```bash
cd "$HOME/ai-patch-exchange"
git switch main
git pull --ff-only
git fetch origin \
  refs/heads/jobs/vrwebview/h1-scroll-001:refs/remotes/origin/jobs/vrwebview/h1-scroll-001

GIT_CONFIG_COUNT=2 \
GIT_CONFIG_KEY_0=core.autocrlf GIT_CONFIG_VALUE_0=true \
GIT_CONFIG_KEY_1=core.filemode GIT_CONFIG_VALUE_1=false \
python3 scripts/exchange.py --repo "$PWD" apply \
  --job-ref origin/jobs/vrwebview/h1-scroll-001 \
  --project vrwebview --job h1-scroll-001 \
  --target /mnt/c/source/Unity/SimpleUnity3DWebView \
  --worktree-eol git \
  --source-git-commit 237d94d9c1cf04aa55625f66fef6b09160fe7b75
```

mainへWSL修正を統合したので、`git show origin/fix/...`や一時CLIの作成はこの利用側の手順には不要。readyとして公開されたジョブを扱い、`--allow-incomplete`を通常の指定に加えない。

`--source-git-commit`は元ソースの照合用証拠を限定的に読む指定で、古いファイルを復元する指示ではない。元ZIPを手元へ再exportする必要はない。

対象の変更が残っている場合はapplyが停止する。以前のstashを消す・何度も自動stashする・失敗後に無条件に復元する処理は入れない。復旧を要する場合は、その時点のHEAD・作業ツリー・適用記録を確認する。

## 診断コードの更新

診断ツールも同じプロジェクトのパッチに含まれる。取得入口は`Tools/probe.py`へ集約し、旧captureコマンドもそこへ委譲する。次の試行を計画した段階で[診断仕様](Diagnostics.md)を使い、空ファイルや診断行の欠落を成功扱いにしない。

今回の統合・文書更新だけでHMD操作やUnity再ビルドは必要ない。
