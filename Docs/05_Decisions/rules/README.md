# ADR の一覧（共通ルール）

共通ルール（[rules.md](../../04_Development/rules.md)）を決めた理由と採らなかった案。原本は [zeusisgood/agent-kickoff](https://github.com/zeusisgood/agent-kickoff) にあり、**案件側では直さない**。案件の決定は [../project/README.md](../project/README.md)。

| 番号 | 題名 | 状態 | 要約 |
|---|---|---|---|
| [0001](0001-record-decisions-in-adr.md) | 決定を ADR に残す | 承認 | 版をまたぐ決定を 1 決定 1 本・連番で残す。案件とルールで置き場所を分ける |
| [0002](0002-public-template-repo-no-skill.md) | ルールは公開のテンプレートリポに置き、スキルは作らない | 承認 | 原本は公開テンプレートリポ。チャットは URL から読み、メモリに 1 行だけ置く |
| [0003](0003-kickoff-files-made-in-chat.md) | 仕様書・devcontainer・CLAUDE.md はチャットで作る | 承認 | devcontainer は Claude Code より先に要るため |
| [0004](0004-calver-yyyy-m-n.md) | 版は YYYY.M.N にする | 承認 | N はその月の何回目か。互換性はプロトコル版・データ版で持つ |
| [0005](0005-git-commit-convention.md) | コミットは Git 本来の慣習で書く | 承認 | 英語・命令形の件名、本文に Why。Conventional Commits は採らない |
| [0007](0007-subagent-model-assignment.md) | サブエージェントは役割ごとにモデルを割り当てる | 承認 | Claude Code の場合。探索とログは haiku、レビューは sonnet。仕組みは公式文書に任せる |
| [0008](0008-pr-review-in-session-and-at-claude.md) | PR レビューはセッション内で行い、必要なときだけ @claude で呼ぶ | 承認 | Claude Code の場合。サブスクリプションのトークンで、API 課金なし |
| [0009](0009-devcontainer-slim-native-installer.md) | devcontainer は slim＋vscode＋ネイティブインストーラにする | 承認 | 簡単な削減だけ。ファイアウォールは入れない |
| [0010](0010-personal-prefs-outside-repo.md) | 個人的な設定はリポに含めない | 承認 | vi・カラーコードはチャットだけで使う |
| [0011](0011-agent-neutral-agent-kickoff.md) | エージェントに依存しない形にし、リポ名を agent-kickoff にする | 承認 | 入口は AGENTS.md。Claude 固有の設定は付録 B と CLAUDE.md へ |
| [0012](0012-upstream-rules-from-projects.md) | 案件で生まれたルールは、明示的に指示されたときだけ原本に PR を出す | 承認 | TODO.md に控えて相談。PR を出せない環境ではプロンプトを作って持ち出す |
| [0013](0013-downstream-rule-version-check.md) | ルールに版を付け、作業を始めるときに原本の版を確かめる | 承認 | 新しければ差分の要約を見せて相談。取り込みは案件内の PR で |
| [0014](0014-tasks-file-per-spec.md) | 作業の分解を、仕様書とは別の TASKS ファイルに書く | 承認 | 実装順のチェックリスト。中断からの再開用 |
| [0015](0015-keep-the-process-light.md) | 進め方を重くしすぎない | 承認 | 文書だけの変更は仕様書なし。rules は 300 行まで |
| [0016](0016-cc0-license.md) | 原本のライセンスを CC0-1.0 にする | 承認 | 案件に条件を残さない。テンプレートの LICENSE は案件で置き換える |
| [0017](0017-propose-branch-name-before-work.md) | ブランチ名は切る前に提案し、了承を得る | 承認 | 環境が割り当てる名前は内容と関係ない。相談で了承を得て、別の名前への push の許可にする |
| [0018](0018-bundle-third-party-skills.md) | 外部のスキルを雛形に同梱する | 承認 | security-audit と ponytail を `.claude/skills/` に写す。ponytail は打ったときだけ。devcontainer に Node |
| [0019](0019-check-open-prs-before-work.md) | ブランチを切る前に開いた PR を確かめる | 承認 | あれば並べて、既存のブランチに積む・Merge を待つ・並行する から選んでもらう。rebase の手間を防ぐ |
