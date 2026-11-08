# patch-exchange基盤の統合結果

2026-11-08 JST。WSL向け修正4コミットをmainへfast-forward統合し、リモートの先端も確認した。

- 統合前main: `ebef0fe48f9a82154fec2882c26deb5022bef5b5`
- 統合後main: `fc65dea7d40f7d6aebc38620f675d76f9e337805`
- force push、既存コミットの書換え、Unityジョブのmainへの混入は行っていない。

## 統合した内容

| コミット | 内容 |
|---|---|
| d3f155d | WindowsマウントでGit indexのmodeを尊重 |
| 74136db | applyの改行形式を明示・適用記録へ保持 |
| 08946c5 | 指定Git commitから元ソースの監査証拠を限定的に読む |
| fc65dea | 混在する作業ツリー改行をGit blob hashと照合 |

mainの`apply --help`で`--worktree-eol`と`--source-git-commit`を確認した。開発用とWindows/WSL適用用のCLIを別ブランチから取得する必要はなくなった。[標準の更新・適用手順](StandardWorkflow.md)を使用する。

旧版main ebef0feには両オプションがなく、そこへ取得先を切り替えたことが以前の適用エラーの原因だった。この記述は過去の状態であり、現在のmainが非対応という意味ではない。

## 検証と監査

統合候補と完全に一致するsnapshotで全277テストが成功済み。統合後のローカルmainでもmode8件・EOL15件・元ソース証拠17件の計40件を再実行し成功した。この40件は全277件と重複するため、合計317件の別テストとは数えない。

4コミットの全変更と13のblob版、コミットメッセージを監査した。検出26件は既存README/正規表現の例と合成EOLテストの文字列であることを個別確認。未確認の秘密値・既知形式の実トークンは検出されていない。

リモートmainから同じ二つのCLIを取り出し、元ZIPと既存ジョブを新しいGit外の領域へresumeできることも確認した。既存のパッチ本文・hashを変更していない。ユーザーの実プロジェクトへのapply、Windows/NTFS上の独立再現、Unity/Quest試験はこの統合では行っていない。

## 運用の切替

ローカルでapplyするcheckoutはmainに保ち、ジョブは`--job-ref`で指定する。クラウドで過去のジョブを開発する場合は、従来どおり同じmain commitのCLI二本をGit外へ取り出してからジョブへ切り替える。違いは利用側のcheckout方針であり、CLIの実装を分けるものではない。

個別のDownloads用適用/復旧helperは過去の復旧資料として残す。通常の更新には追加せず、標準CLIへ戻す。新しい基盤コードをUnityパッチへコピーする方法も採らない。
