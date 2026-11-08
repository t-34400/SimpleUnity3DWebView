# patch-exchange基盤の統合課題

2026-11-08 JST。プロジェクトの製品パッチと、転送基盤の修正を混在させない。このページは基盤の統合レビュー資料であり、mainへの統合完了を示すものではない。

## 確認した版

- `main`: `ebef0fe48f9a82154fec2882c26deb5022bef5b5`
- `fix/windows-worktree-modes`: `fc65dea7d40f7d6aebc38620f675d76f9e337805`
- mainから修正ブランチへの追加は4コミット。

| コミット | 内容 |
|---|---|
| d3f155d | Windowsマウントの作業ツリーでGit indexのmodeを尊重 |
| 74136db | applyで明示的な改行形式を扱う |
| 08946c5 | 指定したGit commitから元ソースの監査証拠を読む |
| fc65dea | ファイル単位で混在する作業ツリー改行をGit blob hashと照合 |

mainには`--worktree-eol`と`--source-git-commit`がなく、修正ブランチにはあることを実際のapply --helpで確認した。開発/publish用のmain CLIと、このWindows/WSL consumer用CLIを取り違えたことが、今回の適用エラーの直接原因。

## ローカル検証

修正ブランチの同じcommitをGit外の一時snapshotへ展開し、`PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s tests -v`を実行。**277件成功**。実ユーザーのUnityプロジェクトは使用していない。これは実Windows/WSL/NTFS環境での独立再現を意味しない。

差分はREADME、二つのCLI、mode/source-evidence/EOLの三つのテストファイル。source evidenceはv1の単一repoに限定される。改行・modeの調整で既存パッチ本文やhashを書き換えず、source-onlyの扱いやsecret検査を迂回しないことがレビュー対象になる。

## 統合の完了条件

1. [4コミットの差分](https://github.com/aist-harc-swioh/ai-patch-exchange/compare/main...fix/windows-worktree-modes)を基盤変更としてレビューする。
2. 対応オプション・既存apply記録・改行/mode/元ソース証拠の互換性を確認する。
3. mainへ統合し、mainから取り出した同じ二つのCLIで対象ジョブを読めることを確認する。
4. 個別のDownloads用bootstrap/recoveryスクリプトを通常入口から外し、標準applyの手順へ戻す。

既存のパッチ列、適用済みGit履歴、stash、ユーザーの未コミット変更は統合のために書き換えない。統合前にmainのCLIへ切り替える案内を出さない。

GitHub APIによるPR状態の読み取りは、この環境では403で取得できなかった。既存PRの有無は未確認で、新規PR作成・mainへのmergeは行っていない。統合済みとは報告しない。今回のプロジェクト整理パッチへ基盤CLIをコピーして解決した扱いにもしない。
