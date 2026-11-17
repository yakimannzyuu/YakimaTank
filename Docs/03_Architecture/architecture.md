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

### 実行中の部品

乗車と入力は、生成 Prefab の中の Udon で閉じる。入力の方法と車両の動きは、共通操作値で分ける。この版は同期しない（各自の画面だけ）。

```mermaid
flowchart LR
  player([プレイヤー]) --> boarding[乗車の判定<br/>Default のトリガー]
  boarding --> seat[座席<br/>VRCStation]
  seat --> desktop[Desktop の入力]
  seat --> vr[VR の入力]
  desktop --> control[(共通操作値)]
  vr --> control
  control --> vehicle[車両の Udon<br/>走行への変換]
  seat --> vehicle
  vehicle --> physics[Rigidbody・WheelCollider<br/>Walkthrough]
  control --> panel[デバッグ UI<br/>別 Prefab]
```

- 車両は、自分が座っている間だけ物理の更新ごとに共通操作値を読む。座っていない間は `SetDriveInput` を直接使える。
- 車体のコライダーと WheelCollider は `Walkthrough` レイヤーで、プレイヤーと当たらない。元のモデルのトリガーでないコライダーは、生成 Prefab の中で無効にする。

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

### 乗車から降車まで

```mermaid
sequenceDiagram
  actor P as プレイヤー
  participant B as 乗車の判定
  participant S as 座席
  participant I as 入力アダプター
  participant C as 共通操作値
  participant V as 車両
  P->>B: Interact
  B->>S: 空いていれば座らせる（UseStation）
  S->>B: 判定を無効にする（誰かが座っている間）
  S->>I: 自分なら Desktop か VR の入力を有効にする
  S->>V: 自分が座っている
  loop 乗っている間
    I->>C: 操作値を書く
    V->>C: 物理の更新ごとに読み、アクセル × ギアで走る
  end
  P->>S: 降りる（Space・ジャンプ）
  S->>I: 入力を止める
  S->>C: 初期値に戻す
  S->>V: 入力を 0 にする（停止中のブレーキで止まる）
  S->>B: 判定を戻す
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
| `YakimaTankVehicle` | Runtime | 走行・制動・旋回・再適用。自分が座っている間は共通操作値から走る（UdonSharp） |
| `YakimaTankControlState` | Runtime | 共通操作値（ハンドル・アクセル・ギアなど）。入力が書き、車両とデバッグ UI が読む |
| `YakimaTankSeat` | Runtime | 座席（VRCStation と同じ GameObject）。出入りで乗車の判定と入力を切り替え、降りたら初期値に戻す |
| `YakimaTankBoarding` | Runtime | 乗車の判定の Interact |
| `YakimaTankDesktopInput` | Runtime | Desktop の入力（W・S・A・D、視点、左クリック）。変換は静的な関数 |
| `YakimaTankVRInput` | Runtime | VR の入力（左手で走行、右手で砲と射撃）。変換は静的な関数 |
| `YakimaTankDebugPanel` | Runtime | デバッグ UI。共通操作値を箱の大きさで見せる。別 Prefab |
| `YakimaTankSetupSettings` | Editor | 設定アセット（正本）。読み込むときに今の版へ移す |
| `YakimaTankSetupValidator` | Editor | 設定の検証（Error・Warning）。`Walkthrough` レイヤー・砲塔・元のモデルのコライダー・砲塔と砲身を除いた車体のメッシュも見る |
| `YakimaTankSettingsWriter` | Editor | 階層パスで設定を書く。解決できなければ何も書かない |
| `YakimaTankSetupService` | Editor | 生成（座席・乗車の判定・入力・レイヤーを含む）。ウィンドウと連携が共用する |
| `YakimaTankScenePlacement` | Editor | シーンへの配置。車両は生成 Prefab のインスタンスで見分け、その車両を指すデバッグ UI を 1 枚まで許す。ほかの手の変更があれば変えずに止める |
| `YakimaTankDebugPanelPlacement` | Editor | 入れ物にデバッグ UI を 1 枚置く（開発ワールドの更新が配置の後に呼ぶ） |
| `YakimaTankDebugPanelBuilder` | Editor | パッケージのデバッグ UI の Prefab を作る（作り直すときも使う） |
| `YakimaTankPlayTuning` | Editor | Play 中の再適用・出し直し・作業値の保存。車両・座席・VR の入力に書く。出し直したら、前の車両を表示していたデバッグ UI を新しい車両につなぎ直す |
| `YakimaTankWorkingValues` | Editor | Play 中の作業値。作った元の設定と組にし、設定が替われば作り直す |
| `YakimaTankRuntimeValues` | Editor | 設定と、値を受ける Udon（車両・座席・VR の入力）で同じ名前の値 |
| `YakimaTankModelPaths` | Editor | モデルの中の階層パス（同じ名前の兄弟は番号つき。名前の中の `\` `/` `[` `]` はエスケープ） |
| `YakimaTankBounds` | Editor | メッシュの範囲（SkinnedMeshRenderer はルートのボーンの座標）。車体・砲塔自身にメッシュがなければ、子から砲塔・砲身の階層を除いて求める |
| `YakimaTankSetupMath` | Editor | 転輪の半径・位置、サスペンションの自動の値 |
| `YakimaTankModelInspector` | Editor | モデルを読むだけで、階層・範囲・軸・名前からの候補を返す（連携の `inspect-model`） |
| `TestWorkspace` | Tests/Editor | テストの一時フォルダと、立方体と円柱のモデル |
| `YakimaTankDriveTestBase` | Tests/Runtime | 走行と乗降の判定（PlayMode・ClientSim）。生成物ごとに派生させる。乗降のテストだけ ClientSim を始める |

## これからの設計

これから足す機能の段階と境界の案は [prototype-plan.md](prototype-plan.md)。この文書は今の実装だけを描く。

## そのほかの図

| 図 | ファイル |
|---|---|
| 生成 Prefab のデータ構造 | [diagrams/generated-prefab.md](diagrams/generated-prefab.md) |
| 連携の要求の状態遷移 | [diagrams/request-states.md](diagrams/request-states.md) |
