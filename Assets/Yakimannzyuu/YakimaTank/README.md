# YakimaTank

VRChat のワールドで、戦車モデルを簡単なセットアップで走行・射撃できるようにする Udon ギミックです。

> 開発中です。仕様や構成は予告なく変わります。

## 動作環境

- Unity 2022.3
- VRChat Worlds SDK 3.10.0 以降(UdonSharp を含む)

## 導入方法(開発中の版)

VCC での配布は正式リリース時に用意します。それまでは Unity Package Manager の git URL で導入できます。

1. VRChat Worlds SDK を入れたワールドプロジェクトを Unity で開く
2. Window → Package Manager を開く
3. 左上の「+」→ Add package from git URL を選ぶ
4. 次の URL を入力して Add を押す

```
https://github.com/yakimannzyuu/YakimaTank.git?path=/Assets/Yakimannzyuu/YakimaTank
```

特定の版に固定したいときは、URL の末尾に `#<コミットハッシュ>` を付けます。

## 更新方法

Unity は導入したコミットを `Packages/packages-lock.json` に記録します。そのため、リポジトリが更新されても、Unity を再起動しただけでは導入済みの版は変わりません。

最新版にするには、上の導入方法の手順 3・4 で同じ URL をもう一度指定します。コミットハッシュを付けて固定していた場合は、それを外すか、更新先のハッシュに変えてください。

## ライセンス

MIT License です。詳しくは [LICENSE.md](LICENSE.md) を見てください。戦車モデル本体は含みません。
