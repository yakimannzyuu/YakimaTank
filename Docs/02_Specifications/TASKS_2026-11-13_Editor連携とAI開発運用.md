# 作業の分解：SPEC_2026-11-13_Editor連携とAI開発運用

- 仕様書：[SPEC_2026-11-13_Editor連携とAI開発運用.md](SPEC_2026-11-13_Editor連携とAI開発運用.md)
- ブランチ：`claude/editor-setup-workflow`（対の仕様書と同じブランチ・同じ PR）

## 作業

- [x] ブランチを切る。作業前の未コミットの変更を分ける（初期仕様の文言は別のコミット、Kit の依存の記録はこの作業に含める）
- [x] 仕様書 2 本・ADR 0007・0008・TASKS を書く
- [x] 運用文書：`AGENTS.md`・`Docs/04_Development/ai-workflow.md`・`CLAUDE.md`
- [x] 役割：`.claude/agents/`（検証役を足す、レビューの観点）・`.codex/agents/`
- [x] 雛形：SPEC・TASKS・Issue・PR。`rules-changelog.md` の取り込み記録に改変を書く
- [x] Kit の依存：`.gitignore` に本体を足し、版と取得元を記録する
- [x] 連携の基盤（ローカル・非公開）：接続・認証・照合・要求の記録・`status`
- [x] `compile`・`udon-compile`：正常・C# だけの誤り・UdonSharp だけの誤り・直した後を区別する
- [x] `test-list`・`test-run`：EditMode・PlayMode・Reload をまたぐ
- [x] 受け入れ確認：接続を断る・再送で二重に実行しない・作業を奪わない・試作のテストとの比較（手動の Play 中に断ることは、利用者の確認に回す）
- [x] 図を直す：`Docs/03_Architecture/architecture.md`・`diagrams/`
- [x] README の「開発に参加する」
- [x] レビューを通す（rules 8 章）
- [x] PR を作る（対の仕様書と同じ PR）

## メモ

- 中断から戻るための状態（要求 ID・Editor の状態・ローカルのパスなど）は、非公開のローカルの再開記録に書く。ここには書かない。
