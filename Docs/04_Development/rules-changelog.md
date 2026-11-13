# ルールの変更履歴

原本（[zeusisgood/agent-kickoff](https://github.com/zeusisgood/agent-kickoff)）のルール版ごとの変更点。新しい版を一番上に足す。見出しは `## YYYY.M.N (YYYY-MM-DD)`。

案件に取り込むときに読むので、各項目は 1 行で、何が変わったかと関係する rules-adr の番号を書く。案件ごとに直しているファイル（`AGENTS.md`・`CLAUDE.md`・`.claude/`・`.devcontainer/` など）の変更は、そのファイル名を書く。

## この案件への取り込み記録

原本の更新を取り込むとき（rules 13 章の下り）は、ここに 1 回分ずつ足す。新しいものを上に足す。

### 2026-11-12 初回の取り込み

- 取り込み元：[zeusisgood/agent-kickoff](https://github.com/zeusisgood/agent-kickoff) の `3a5dd0e89878f8379e2ce8b36c91f3e52061c026`（2026-11-08 のコミット）
- 原本のルール版：2026.11.0

置き場所（原本 → この案件）：

| 原本 | この案件 |
|---|---|
| `doc/rules.md`・`doc/rules-changelog.md`・`doc/TODO.md`・`doc/upstream-prompt.md` | `Docs/04_Development/` |
| `doc/spec/` | `Docs/02_Specifications/` |
| `doc/architecture.md`・`doc/diagrams/` | `Docs/03_Architecture/` |
| `doc/adr/` | `Docs/05_Decisions/project/` |
| `doc/rules-adr/` | `Docs/05_Decisions/rules/` |

取り込まなかったもの（次の取り込みでも入れない）：

- rules-adr 0006 と、それに関わる記述（`rules.md` の 3 章の 1 節・5 章の手順 9・10 章の禁止事項 1 行・付録 A-4 の 1 行、この変更履歴と `upstream-prompt.md` の該当する行、`.claude/settings.json` の deny 6 行）
- `rules.md` 9 章の本文（devcontainer）と付録 B-1 の devcontainer・security-audit の記述
- `KICKOFF.md`・`README.md`・`CHANGELOG.md`・`LICENSE`・`.devcontainer/`・`.github/workflows/release.yml`・`.claude/skills/security-audit/`・`.claude/skills/README.md`
- `.github/workflows/claude.yml`（入れるかは未決。`Docs/04_Development/TODO.md`）

この案件で加えた改変（次の取り込みでも残す）：

- 全ファイル：案件内を指すパスと相対リンクを、上の置き場所に合わせた。原本の中のパスを説明する記述はそのまま
- `rules.md`：4 章を SemVer に（ADR 0002）、5 章の手順を SemVer とパッケージの `CHANGELOG.md` に合わせた、9 章を「使わない」に、13 章の下りを差分で取り込む手順に、付録 B-1 を Bash と PowerShell の権限・ローカルのスキルに合わせた、付録 U を追加
- `Docs/02_Specifications/_template.md`：版を SemVer に
- `AGENTS.md`：この案件の内容で書いた。原本の冒頭にある「このリポが原本なら」の 1 行は入れない
- `CLAUDE.md`：ponytail の 2 行を各自の `CLAUDE.local.md` に移した
- `.claude/agents/reviewer.md`：パスと、付録 U の観点を足した
- `.claude/settings.json`：PowerShell の権限ルールを足した
- `.claude/skills/` の ponytail 3 つ：ローカルにだけ置き、Git に入れない（ADR 0006）
- `Docs/02_Specifications/_template.md`：承認・関連 Issue・PR までに通す検証・除外と未確認・承認待ちの項目の欄を足した（2026-11-13、ADR 0007）
- `Docs/02_Specifications/_tasks_template.md`：「再開の状態」の欄を足した（2026-11-13、ADR 0007）
- `.claude/agents/`：`verifier` を足し、`reviewer` に検証の証拠と公開範囲の観点を足した。`CLAUDE.md` の役割の行を直した（2026-11-13、ADR 0007）
- `.codex/agents/`：原本にない。Codex 用の役割の定義を足した（2026-11-13、ADR 0007）


## 2026.11.0 (2026-11-04)

- ブランチを切る前に開いている PR を確かめ、あれば並べて、既存のブランチに積む・Merge を待つ・並行する のどれにするかを、ブランチ名の提案と同じ返事で聞く（2 章・3 章、rules-adr 0019）。雛形ファイルへの影響はない

## 2026.10.5 (2026-10-21)

- `.claude/skills/`：外部のスキル `security-audit`（cloudflare）と `ponytail`・`ponytail-audit`・`ponytail-review`（DietrichGebert）を写して同梱。ponytail の 3 つは打ったときだけ動く。出どころは `.claude/skills/README.md`（付録 B、rules-adr 0018）
- 案件への取り込み：`.claude/skills/` は差分ではなく、ディレクトリごと（`LICENSE` を含めて）写す。`CLAUDE.md`・Dockerfile・`claude.yml` は下の差分を手で反映する
- `.devcontainer/Dockerfile`：基盤イメージに `node` がなければ apt の `nodejs` を入れる（security-audit の検証スクリプト用）
- `.github/workflows/claude.yml`：上限を 100 ターン・60 分に上げる
- `README.md`・`KICKOFF.md`：ファイル一覧に `.claude/skills/` を足す。`LICENSE` を置き換えるときも `.claude/skills/*/LICENSE` は残す、と書く（rules-adr 0016 に注記）
- `CLAUDE.md`：ponytail は打ったときだけ使い、rules と食い違えば rules に従う。`/ponytail-review` は `reviewer` の代わりにしない
- 付録 B に、クラウドセッションでは devcontainer が使われないことと、security-audit は隔離環境がなければ対象のコードを実行しないことを足す

## 2026.10.4 (2026-10-21)

- ブランチ名は切る前に提案し、ユーザーの了承を得る。相談の「決めてほしいこと」に含める。依頼文で指定されていれば要らない（3 章、rules-adr 0017）。雛形ファイルへの影響はない

## 2026.10.3 (2026-10-21)

- `.devcontainer/Dockerfile`：基盤イメージに UID・GID 1000 の利用者（`node`・`ubuntu` など）がいれば消してから `vscode` を作る。別の UID で作られて、ワークスペースに書き込めなかった
- `AGENTS.md`：原本ではこの文書ではなく `doc/upstream-prompt.md` に従う、の 1 行を冒頭に足す（案件では当てはまらない）

## 2026.10.2 (2026-10-21)

- 原本のライセンスを CC0-1.0 にした。テンプレートの `LICENSE` は案件のライセンスに置き換える（README・`KICKOFF.md`、rules-adr 0016）。既存の案件では、原本の `LICENSE` を取り込まない

## 2026.10.1 (2026-10-21)

- 仕様書を承認したら、作業の分解 `doc/spec/TASKS_…` を書く。雛形 `doc/spec/_tasks_template.md` を追加（2 章、rules-adr 0014）
- 文書だけの変更は仕様書なしでよい。rules は 300 行までを目安にする（0 章・2 章、rules-adr 0015）
- 既存のリポに入れるとき、既存コードから規約を抜き出して相談する（`KICKOFF.md`）
- Copier などのマージの仕組みは今は使わない、を未決事項として明記（13 章、rules-adr 0013 の注記）
- `AGENTS.md`：要点の 1 行を変更（作業の分解・文書だけの変更）

## 2026.10.0 (2026-10-21)

- 初版。相談 → 仕様書 → 実装の進め方、書き分けの原則、ブランチ・コミット・PR、日付の版、ADR、役割の分担、devcontainer、安全（rules-adr 0001〜0011）
- 共通ルールとの行き来（上り・下り）とルール版（13 章、rules-adr 0012・0013）
