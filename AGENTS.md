# AGENTS.md

エージェント固有の指示は、`CLAUDE.md` などの入口ファイルに書く。

## 最初に読むもの

| 目的 | 文書 |
|---|---|
| 作業のルール（必ず守る） | [Docs/04_Development/rules.md](Docs/04_Development/rules.md) |
| ギミックの概要 | [Docs/01_Overview/overview.md](Docs/01_Overview/overview.md) |
| 仕組みと図 | [Docs/03_Architecture/architecture.md](Docs/03_Architecture/architecture.md) |
| 仕様書の一覧 | [Docs/02_Specifications/README.md](Docs/02_Specifications/README.md) |
| この案件の決定（ADR） | [Docs/05_Decisions/project/README.md](Docs/05_Decisions/project/README.md) |
| 共通ルールの理由（ADR） | [Docs/05_Decisions/rules/README.md](Docs/05_Decisions/rules/README.md) |
| 共通ルールの変更履歴と取り込み記録 | [Docs/04_Development/rules-changelog.md](Docs/04_Development/rules-changelog.md) |
| 後回しの作業 | [Docs/04_Development/TODO.md](Docs/04_Development/TODO.md) |

## 要点

- 「相談・ラフ・整理・計画」では実装しない。
- 承認済みの仕様書があれば、作業の分解（`TASKS_…`）を書いてから、実装から PR まで進める。Merge はユーザー。文書だけの変更は仕様書なしでよい。
- PR の前に、書いた文脈から切り離したレビューを通す。
- 承認済みの ADR に反する変更が必要なら止まる。
- セッションの最初の作業では、原本のルール版を確かめる。新しければ止めて相談する（rules 13 章）。
- `Docs/04_Development/rules.md` と `Docs/05_Decisions/rules/` は直接直さない。共通にしたいルールは `Docs/04_Development/TODO.md` の「原本へ提案」に控えて相談する。
- コードは How、テストは What、コミットは Why、コメントは Why not。
- `Docs/00_Memo/` は非公開のメモ。Git に入れない。その内容を公開するファイルに写すときは、ユーザーに確かめる。

## この案件について

- 概要：VRChat 向けの戦車用 Udon ギミック。戦車モデルを、専用のセットアップウィンドウで簡単に走行・射撃できるようにする。VPM パッケージ `com.yakimannzyuu.yakimatank` として配る。
- 言語・ツール：Unity 2022.3.22f1 / VRChat Worlds SDK 3.10.6 / UdonSharp（C#）。開発は Windows。
- パッケージの置き場所：`Assets/Yakimannzyuu/YakimaTank/`（ADR 0001）。`Assets/` の下で Git に入れるのはここだけ。
- ライセンス：MIT（ADR 0005）
- 該当する付録：A-1（通信。VRChat の同期として読み替える）、U（Unity・VRChat）

## 版を書く所

版は SemVer（ADR 0002）。版を上げるときは、ここに挙げた所をすべて揃える。

| ファイル | 場所 |
|---|---|
| `Assets/Yakimannzyuu/YakimaTank/package.json` | `version` |
| `Assets/Yakimannzyuu/YakimaTank/CHANGELOG.md` | 一番上の節（`## [x.y.z] - YYYY-MM-DD`） |
| `README.md`（ルート） | 「最近の更新」 |

## コマンド

エージェントは Unity を起動できない。ビルドとテストはユーザーが Unity で行う。

| 目的 | 方法 |
|---|---|
| コンパイルの確認 | ユーザーが Unity を開き、Console にエラーがないことを確かめる |
| テスト | ユーザーが Window → General → Test Runner の EditMode で実行する |
