@AGENTS.md

## Claude Code の場合

- 役割の分担（rules 8 章）は `.claude/agents/` のサブエージェントを使う。
  - 探索は `Explore`、Editor 連携での検証の委任は `verifier`。
  - `log-summarizer` は、連携の構造化した結果で足りず、長い生ログを読むときだけ。
  - PR の前に `reviewer` を必ず通す。
- `Explore` は `CLAUDE.md` も `AGENTS.md` も読まない。守らせたい規則は依頼文に書く。
- ブランチ名は `claude/<内容が分かる英語>`。
