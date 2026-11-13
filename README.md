# YakimaTank

VRChat 向けの戦車用 Udon ギミックです。戦車モデルを、専用のセットアップウィンドウで簡単に走行・射撃できるようにします。

> 開発中です。仕様や構成は予告なく変わります。

このリポジトリは Unity プロジェクトそのものです。配布するパッケージ（`com.yakimannzyuu.yakimatank`）の本体は [`Assets/Yakimannzyuu/YakimaTank/`](Assets/Yakimannzyuu/YakimaTank/) にあります。

## 対応環境

- Unity 2022.3
- VRChat Worlds SDK 3.10.0 以降（UdonSharp を含む）

## 導入方法（開発中の版）

VCC での配布は正式リリース時に用意します。それまでは Unity Package Manager の git URL で導入できます。

1. VRChat Worlds SDK を入れたワールドプロジェクトを Unity で開く
2. Window → Package Manager を開く
3. 左上の「+」→ Add package from git URL を選ぶ
4. 次の URL を入力して Add を押す

```
https://github.com/yakimannzyuu/YakimaTank.git?path=/Assets/Yakimannzyuu/YakimaTank
```

特定の版に固定したいときは、URL の末尾に `#<コミットハッシュ>` を付けます。

### 更新方法

Unity は導入したコミットを `Packages/packages-lock.json` に記録します。そのため、このリポジトリが更新されても、Unity を再起動しただけでは導入済みの版は変わりません。

最新版にするには、上の手順 3・4 で同じ URL をもう一度指定します。コミットハッシュを付けて固定していた場合は、それを外すか、更新先のハッシュに変えてください（[Unity の Git 依存関係の仕様](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-git.html#git-locks)）。

## 開発に参加する

1. このリポジトリを clone する
2. VCC で「既存のプロジェクトを追加」から clone したフォルダを選び、Resolve で VRChat SDK を復元する
   - テストに使う Udon Test Kit も Resolve で戻る。先に TsVRC の VPM リポジトリを VCC に足しておく（[手順](https://tsvrc.com/docs/install-a-package)）
3. Unity で開く

AI のエージェントが開いている Editor でコンパイルやテストを行う「ローカル Editor 連携」は、任意のローカル機能で、このリポジトリには入っていません。入れていない環境では、Unity でコンパイルとテストを確かめます（[AI の作業手順](Docs/04_Development/ai-workflow.md)）。

Git に入れるのは、`Assets/` の下では `Assets/Yakimannzyuu/YakimaTank/` だけです。開発の進め方は [AGENTS.md](AGENTS.md) と [Docs/04_Development/rules.md](Docs/04_Development/rules.md) にあります。

## フォルダ

| フォルダ | 内容 |
|---|---|
| `Assets/Yakimannzyuu/YakimaTank/` | パッケージ本体（`package.json`・Runtime・Editor・Tests・Demo） |
| `Docs/01_Overview/` | ギミックの概要 |
| `Docs/02_Specifications/` | 仕様書 |
| `Docs/03_Architecture/` | 仕組みと図 |
| `Docs/04_Development/` | 開発のルール |
| `Docs/05_Decisions/` | 決定の記録（ADR） |
| `Packages/`・`ProjectSettings/` | Unity プロジェクトの設定 |

## 最近の更新

- 0.1.0：走行（前進・後退・旋回・制動）と、セットアップウィンドウでの生成・シーンへの配置・Play 中の調整
- 0.0.1：パッケージの雛形

変更の一覧は [CHANGELOG.md](Assets/Yakimannzyuu/YakimaTank/CHANGELOG.md) にあります。

## ライセンス

MIT License です（[LICENSE](LICENSE)）。戦車モデル本体は含みません。
