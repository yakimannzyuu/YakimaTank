# 0001. パッケージを Assets/Yakimannzyuu/YakimaTank で開発する

- 状態：承認
- 日付：2026-11-12
- 関連：[0003](0003-public-from-start-git-url.md)・[0004](0004-vpm-at-release.md)

## 背景

ギミックは VPM パッケージ（`com.yakimannzyuu.yakimatank`）として配る。リポジトリは Unity プロジェクトそのものにして、clone すれば開発を続けられるようにしたい。一方で、Assets の下には VRChat SDK のサンプルや試作のファイルもあり、それらは公開しない。

## 決定

- パッケージの本体を `Assets/Yakimannzyuu/YakimaTank/` に置き、その直下に `package.json` を置く。
- `Assets/` の下で Git に入れるのは、このフォルダと親フォルダの `.meta` だけにする。ほかは `.gitignore` で除く。

## 採らなかった案

| 案 | 採らなかった理由 |
|---|---|
| `Packages/com.yakimannzyuu.yakimatank/` で開発する（VRChat 公式の template-package の形） | 開発中と導入後のパスが揃う利点はあるが、作者が慣れた Assets の下での作業を優先した |
| Assets の下をすべて Git に入れる | VRChat SDK のサンプルや試作のファイルまで公開してしまう |

## 結果

- 良くなること：作者が普段どおり Assets の下で作業でき、公開する範囲が 1 つのフォルダに収まる。
- 引き受けること（欠点・制約）：
  - 導入先ではパスが `Packages/com.yakimannzyuu.yakimatank/` に変わり、書き込めなくなる。コードにパッケージ内のパスを直書きしない（rules 付録 U）。
  - 開発用の確認シーンもパッケージの中（`Demo/`）に置くことになる。

## 未決事項

| 何を | なぜまだ決めないか | いつ決めるか（きっかけ） |
|---|---|---|
| `Demo/` を配布物に含めるか | 配布の仕組みをまだ作らないため | VPM 配布を整えるとき（[0004](0004-vpm-at-release.md)） |
