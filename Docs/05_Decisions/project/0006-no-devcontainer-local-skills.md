# 0006. devcontainer を使わず、同梱スキルは Git に入れない

- 状態：承認
- 日付：2026-11-12
- 関連：rules-adr 0009・0018 をこの案件では置き換える

## 背景

共通ルールは、devcontainer で開発環境を揃え（rules-adr 0009）、外部のスキルを `.claude/skills/` に同梱する（rules-adr 0018）。この案件は Windows 上の Unity と VCC で開発する。

## 決定

- devcontainer は使わない。Windows の Unity と VCC で直接開発する。
- ponytail 系のスキルは、各自の `.claude/skills/` にだけ置き、Git に入れない。使い方は各自の `CLAUDE.local.md` に書く。
- security-audit は取り込まない。

## 採らなかった案

| 案 | 採らなかった理由 |
|---|---|
| devcontainer を使う | Unity エディタと VRChat SDK は Linux のコンテナの中で動かせない |
| スキルを Git に入れて同梱する | この案件の利用者（ワールド制作者）には要らず、外部の MIT の素材が公開リポジトリに増える |
| security-audit も入れる | Web やサーバー向けの観点が中心で、Udon のギミックにはほとんど当たらない |

## 結果

- 良くなること：リポジトリには、ギミックとその開発ルールだけが入る。
- 引き受けること（欠点・制約）：別の環境で clone したときは、使う人がスキルを自分で置く。

## 未決事項

なし
