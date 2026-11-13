# 生成 Prefab のデータ構造

生成 Prefab の中と、シーンに置いたときの入れ物。名前は実装の定数（`YakimaTankSetupService`・`YakimaTankScenePlacement`）。

```mermaid
flowchart TB
  subgraph scene[シーン]
    root[YakimaTank_Automation] --> holder[YakimaTankPlacement_配置 ID]
    holder --> instance[生成 Prefab のインスタンス]
  end
  subgraph prefab[生成 Prefab]
    top[ルート<br/>Rigidbody・車両の Udon] --> model[Model<br/>元のモデルの入れ子の Prefab]
    top --> colliders[Colliders] --> body[Body<br/>BoxCollider]
    top --> wheels[Wheels] --> left[L0〜<br/>WheelCollider]
    wheels --> right[R0〜<br/>WheelCollider]
  end
  instance -.元.-> top
  top -.参照.-> left
  top -.参照.-> right
```

- 車両の Udon は、ルートの Rigidbody と左右の WheelCollider を参照する。参照は Prefab の中で閉じる。
- 元のモデルは入れ子の Prefab のまま置き、モデルのルートの回転（FBX の軸の変換など）を保って、向きの補正を上から掛ける。
- 生成し直しても、GUID と中のオブジェクト（WheelCollider・BoxCollider・車両の Udon）は保つ。転輪の数が変われば、番号の大きい方を足し引きする。
- 入れ物は配置 ID ごとに 1 つで、中は車両 1 台。入れ物の位置と向きは設定アセットに保存する。
