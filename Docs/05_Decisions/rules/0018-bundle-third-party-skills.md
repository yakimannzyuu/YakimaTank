# 0018. 外部のスキルを雛形に同梱する

- 状態：承認
- 日付：2026-10-21
- 関連：rules 9 章・付録 B・`.github/workflows/claude.yml`・[0002](0002-public-template-repo-no-skill.md)・[0009](0009-devcontainer-slim-native-installer.md)

## 背景

どの案件でも使いたいスキルが 2 つある。

- [cloudflare/security-audit-skill](https://github.com/cloudflare/security-audit-skill)（MIT）：セキュリティ監査。検証のスクリプト（`validate-*.cjs`）に Node.js が要る。
- [DietrichGebert/ponytail](https://github.com/DietrichGebert/ponytail)（MIT）：コードを最小にする人格（`ponytail`）と、報告だけをする `ponytail-audit`・`ponytail-review`。Claude Code ではプラグインとして配られ、on / off と持ち越しはフック（Node）が担う。

案件では、ローカル・devcontainer・クラウドセッション・GitHub Action（`@claude`）のどこでも同じスキルを使いたい。

## 決定

- 4 つのスキルを `.claude/skills/<名前>/` に写して同梱する。写したコミットと元の LICENSE を一緒に置く。
  - `security-audit`：cloudflare/security-audit-skill の `skills/security-audit/`（`c1c8a8c`）
  - `ponytail`・`ponytail-audit`・`ponytail-review`：DietrichGebert/ponytail の `skills/` の同名ディレクトリ（`09c434a`）
- `ponytail` の人格は標準で off にする。frontmatter に `disable-model-invocation: true` を足し、ユーザーが `/ponytail` と打ったときだけ有効にする。止めるときは「stop ponytail」と言うか、新しいセッションにする。フックとプラグインの機能は入れない。
- `ponytail-audit`・`ponytail-review` にも同じ 1 行を足す。説明文が「review this」「audit this codebase」で発動するため、PR の前の `reviewer` や `security-audit` の代わりに選ばれないようにする。
- 出どころ・コミット・写したあとの変更は `.claude/skills/README.md` に書く。
- `ponytail` と rules が食い違うときは rules に従う。PR の前に必ず通すレビューは `reviewer` のまま（rules 8 章）。`ponytail-review` は任意で追加する。
- devcontainer に Node.js を入れる。基盤イメージに `node` がないときだけ、apt の `nodejs` を入れる。
- 通信制限は入れない。隔離環境がないとき、security-audit は対象のコードを実行せず、見つけたものを `needs_validation` に留める。
- クラウドセッションでは devcontainer は使われない、と付録 B に書く。道具を足すのは環境の設定スクリプトになる。
- GitHub Action（`claude.yml`）の上限を `--max-turns 100`・`timeout-minutes: 60` に上げる。レビューと `ponytail-audit` くらいまでを Action で回す。security-audit は、途中の様子を見ながら回せるローカルかクラウドセッションで行う。Action で足りないときも同じ。

## 採らなかった案

| 案 | 採らなかった理由 |
|---|---|
| devcontainer で `~/.claude/skills` に入れる | クラウドセッションと Action では使えない。名前付きボリュームは初回しかイメージの中身を写さないので、更新も届かない |
| ponytail をプラグインで入れる | on / off は確実になるが、フック 3 つと過去のセッションの読み込みまで入る。ユーザーごとに入れるので、クラウドでは毎回消える |
| SessionStart フックで毎回取ってくる | 版が固定されない。通信の許可が要る |
| `ponytail` を標準で on にする | rules（テストなど）との食い違いが常に起きうる |
| Node を常に apt で入れる | `node:*-slim` を基盤にした案件で Node が 2 つになる |
| Action の上限を今のまま（30 ターン・30 分）にする | `ponytail-audit` が途中で止まりやすい |
| Action で security-audit まで回せる上限（300 ターン・180 分前後）にする | 1 回でサブスクリプションの利用枠を大きく使う。手元の作業と同じ枠を減らす。PR のコメントでは途中の判断を挟みにくい |
| 通信制限を任意または標準で入れる | `NET_ADMIN` の権限と工数が要る（0009 と同じ理由） |
| 13 章の手順もスキルにする | 手順が rules とスキルの 2 か所になる（0002） |

## 結果

- 良くなること：案件を作った時点で、どの環境でも同じスキルが使える。
- 引き受けること：
  - 写しなので、元のリポの更新は手で取り込む。
  - スキルを残す案件には、MIT の著作権表示（各ディレクトリの `LICENSE`）を残す義務が付く。原本の CC0-1.0（[0016](0016-cc0-license.md)）の「案件に条件を残さない」は、スキルを除いた部分の話になる。
  - Node を入れる仕組みは Debian 系（apt）の基盤イメージが前提。確かめたのは `debian:trixie-slim`（apt の Node 20 が入る）と `node:22-trixie-slim`（入れない）の 2 つ。
  - `security-audit` の `*.test.cjs` が、案件のテストの glob に拾われることがある。拾われたら、案件の設定で `.claude/` を除く。
  - devcontainer のイメージが Node の分だけ大きくなる。
  - `ponytail` の off はモデルが指示に従うかどうかに頼る。確実に止めるには新しいセッションにする。
  - Action が暴走したときに使う利用枠が、今の約 3 倍になる。
  - security-audit で対象のコードを実行して確かめることはできない。

## 未決事項

| 何を | なぜまだ決めないか | いつ決めるか（きっかけ） |
|---|---|---|
| 通信制限を雛形に入れるか（0009 から引き継ぐ） | 実行しない監査で足りている | 監査で対象のコードを実行して確かめる必要が出たとき |
| 写したスキルの更新の取り込み方 | 写したばかりで、ずれがない | 元のリポの更新を取り込みたくなったとき |

## 0002・0009 との関係

- 0002 の「スキルは作らない」は、ルールを配る手段としてのスキルの話で、決定は変えない。案件で使う道具のスキルを同梱するのは、この ADR で決める。
- 0009 の「Node.js は不要」は Claude Code の入れ方の話で、決定は変えない。スキルの検証のために Node を足すのは、この ADR で決める。0009 の未決事項（通信制限）は、この ADR に引き継ぐ。
