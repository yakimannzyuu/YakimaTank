# 仕組み

<!-- 仕組みを変えた PR の中で、ここも直す。図と実装が違ったら実装が正。1 図 20 ノードまで。 -->

## 全体構成

設定アセットが正本（ADR 0008）。セットアップウィンドウと AI の連携は同じ検証・生成・配置を使い、生成物から設定を逆算しない。

```mermaid
flowchart LR
  user([利用者]) --> window[セットアップウィンドウ]
  ai([AI]) --> cli[CLI] --> bridge[Editor 連携<br/>ローカル・任意]
  window --> settings[(設定アセット)]
  bridge --> settings
  settings --> validator[検証]
  validator --> service[生成]
  service --> prefab[(生成 Prefab)]
  prefab --> placement[配置]
  placement --> scene[(シーン)]
  window --> tuning[Play 中の調整]
  bridge --> tuning
  tuning --> vehicle[車両の Udon]
  prefab -.含む.-> vehicle
  vehicle --> physics[Rigidbody・WheelCollider]
```

- 連携は開発用のローカルの追加機能で、パッケージには入らない。連携がなくても、ウィンドウだけで同じことができる。
- 車両の Udon は実行中に設定アセットを読まない（Udon から ScriptableObject は読めない）。値は生成のときに書き、Play 中は Editor から書き直す。

## 主要な流れ

### 設定から走行まで

```mermaid
sequenceDiagram
  actor U as 利用者・AI
  participant S as 設定アセット
  participant G as 検証と生成
  participant P as 生成 Prefab
  participant W as シーン
  participant V as 車両の Udon
  U->>S: モデルと対応・値を書く
  U->>G: 生成
  G->>S: 検証（Error なら作らない）
  G->>P: 保存（GUID と中のオブジェクトを保つ）
  G->>P: 読み直して値と参照を確かめる（違えば前の版に戻す）
  U->>W: 配置（配置 ID ごとに 1 台）
  U->>V: Play で走らせる
  U->>V: 作業値を書いて再適用（最初の位置に戻る）
  U->>S: 作業値を設定に保存して生成し直す
```

### 連携でのコンパイルからテストまで

```mermaid
sequenceDiagram
  participant A as AI
  participant C as CLI
  participant B as 連携
  participant E as Editor
  A->>C: compile
  C->>B: 要求（要求 ID・トークン）
  B->>B: 受け付ける前に記録する
  B->>E: コンパイル
  E-->>B: Reload の後に結果
  A->>C: test-run（PlayMode）
  C->>B: 要求
  B->>E: テストを流す（AI が始めた Play の印）
  Note over B,E: Play に入るときの Reload では、実行中の要求を記録から引き継ぐ
  E-->>B: 結果（件数・失敗・スキップ）
  C->>B: 結果を照会
  B-->>C: 要求 ID・入力のハッシュつきの結果
```

## 共通の部品

| 部品 | 場所 | 用途 |
|---|---|---|
| `YakimaTankVehicle` | Runtime | 走行・制動・旋回・再適用（UdonSharp） |
| `YakimaTankSetupSettings` | Editor | 設定アセット（正本） |
| `YakimaTankSetupValidator` | Editor | 設定の検証（Error・Warning） |
| `YakimaTankSettingsWriter` | Editor | 階層パスで設定を書く。解決できなければ何も書かない |
| `YakimaTankSetupService` | Editor | 生成。ウィンドウと連携が共用する |
| `YakimaTankScenePlacement` | Editor | シーンへの配置。手の変更があれば変えずに止める |
| `YakimaTankPlayTuning` | Editor | Play 中の再適用・出し直し・作業値の保存 |
| `YakimaTankWorkingValues` | Editor | Play 中の作業値。作った元の設定と組にし、設定が替われば作り直す |
| `YakimaTankRuntimeValues` | Editor | 設定と車両の Udon で同じ名前の走行の値 |
| `YakimaTankModelPaths` | Editor | モデルの中の階層パス（同じ名前の兄弟は番号つき。名前の中の `\` `/` `[` `]` はエスケープ） |
| `YakimaTankBounds` | Editor | メッシュの範囲（SkinnedMeshRenderer はルートのボーンの座標） |
| `YakimaTankSetupMath` | Editor | 転輪の半径・位置、サスペンションの自動の値 |
| `YakimaTankModelInspector` | Editor | モデルを読むだけで、階層・範囲・軸・名前からの候補を返す（連携の `inspect-model`） |
| `TestWorkspace` | Tests/Editor | テストの一時フォルダと、立方体と円柱のモデル |
| `YakimaTankDriveTestBase` | Tests/Runtime | 走行の判定（PlayMode・ClientSim）。生成物ごとに派生させる |

## そのほかの図

| 図 | ファイル |
|---|---|
| 生成 Prefab のデータ構造 | [diagrams/generated-prefab.md](diagrams/generated-prefab.md) |
| 連携の要求の状態遷移 | [diagrams/request-states.md](diagrams/request-states.md) |
