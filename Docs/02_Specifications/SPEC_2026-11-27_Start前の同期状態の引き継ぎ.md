# 仕様書 0.6.2：Start 前の同期状態の引き継ぎ

- 日付：2026-11-27
- 対象：[Issue #11](https://github.com/yakimannzyuu/YakimaTank/issues/11)。実装の入口は [TASKS](TASKS_2026-11-27_Start前の同期状態の引き継ぎ.md)。
- 方針：やきまは 2026-11-27 に v0.6.2 の単独パッチと独立ブランチ `claude/preserve-prestart-sync` を選択した。この文書は実装に引き継ぐ仕様で、実装・検証は未着手。

## 0. バージョンと制約

- 基準は v0.6.1（`05107ef`）。対象は Unity 2022.3.22f1、VRChat Worlds SDK 3.10.6、UdonSharp。パッケージのライセンスは MIT。
- 既存の手動同期、送信データのバイト列、車体・武器の所有権を前提とする。[ADR 0009](../05_Decisions/project/0009-driver-owned-custom-vehicle-sync.md)
- 2026-11-26 の実機確認では、停止中の車両は途中参加者にも最後の位置に見えた。その際のコールバック順序は未確認。今回の再現には Start 前の受信を意図的に作る PlayMode テストを使う。

## 1. 内容

**採用する仕様は「非オーナーが受け取った有効な初期状態を、3つの Udon すべてで Start 後の状態に残す」こと。** 生存は既存の即時適用を使う。車体・武器は受信と適用を分け、各 Udon の初期化を済ませてから初期状態として1回取り込む。停止中の戦車に、次の移動や再射撃を要求しない。

### 1.1 現状と根拠

以下の `Runtime/`、`Tests/` の起点は `Assets/Yakimannzyuu/YakimaTank/`。行番号・リンクはすべて基準コミットに固定した。

| Udon | Start 前の受信経路と、失われる／残る理由 |
|---|---|
| 車体 | `Runtime/YakimaTankVehicleSync.cs` の `OnDeserialization`（567行）は `result.sendTime` を `incomingSendTime` に保存して `_AcceptState()` を呼ぶ。579行の `!_started` で戻り、履歴にも送信者別の保留にも入らない。さらに `Start`（180行）は `_started = true` → `_RefreshOwner()` → 非オーナーなら `_BecomeRemote()`（196行）。その末尾（412行）の `_ResetHistory()` は `_historyCount = 0`、`_snapNext = true` にする。ガードだけを外して先に `_UseState` しても、履歴件数は消え、受理時刻 `_lastUsedTime` は残る。同じ時刻の再投入も古い状態として退けられる。[受信](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L567)・[初期化](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L180)・[履歴のリセット](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L390) |
| 武器 | `Runtime/YakimaTankWeaponSync.cs` の `OnDeserialization`（417行）も時刻を保存して `_AcceptState()` を呼ぶ。430行で戻るため、角度、装填、初回の発射番号を覚えない。武器の `Start`（134行）は `_started` を立て、ローカル所有なら送信準備をし、`_ApplyAngles()` を呼ぶ。車体と違い、非オーナーの初期化で `_BecomeRemote()` は呼ばない。[受信](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankWeaponSync.cs#L417)・[初期化](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankWeaponSync.cs#L134) |
| 生存 | `Runtime/YakimaTankLife.cs` の `OnDeserialization` → `_AcceptState`（377–416行）はローカル所有を除外し、形式・HP・配列長・時刻等を検証する。受理時は `_hasState` と `_lastUsedTime` を更新し、HP・復活世代・命中履歴を反映する。`Start`（101行）は `!_hasState` の場合だけ最大HPに初期化する。既存の PlayMode は有効化前に HP=40・世代=2 を入れ、有効化前に HP、有効化後に HP と世代を確認する。これを動作の基準にする。[生存の受信](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankLife.cs#L377)・[初期化](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankLife.cs#L101)・[生存の既存テスト](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.Life.cs#L218) |

前提となる仕様は、車体の停止通知・送信者別保留・最初の表示（0.3.0 §§1.4–1.5）、武器の時刻と発射番号（0.5.0 §§1.2、1.5）、生存状態の途中参加（0.6.0 §1.4）。実行順に依存した欠落はコード上の経路として確認した。実クライアントでの発生頻度は未確認。[車体SPEC](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Docs/02_Specifications/SPEC_2026-11-13_車体の同期と所有権.md)・[武器SPEC](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Docs/02_Specifications/SPEC_2026-11-17_砲塔と射撃.md)・[生存SPEC](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Docs/02_Specifications/SPEC_2026-11-17_HPと破壊と復活.md)・[ADR 0009](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Docs/05_Decisions/project/0009-driver-owned-custom-vehicle-sync.md)

### 1.2 Issue 案の評価と方式の選択

| 方式 | 判断 |
|---|---|
| 3つとも使う。車体・武器は受信印を立て、Start の末尾で既存の受理経路に戻す | **採用。** 車体は履歴リセット後、武器は所有者判定後かつ角度の初回描画前に適用する。生存の初期化と通知のタイミングは現行のまま。 |
| 受信印の bool だけを残し、その時点の同期フィールドを1回読む | 早着が1回なら両 Udon ともこれで足りる。複数受信ではフィールドと `incomingSendTime` が上書きされる。例えば有効な発射番号8の後に古い番号7が届くと、7を初期値にし、8の再送で過去弾を表示し得る。採用案では「最後に届いたもの」を「送信時刻が最新の有効候補」に絞るため、印と候補値をローカルに保持する。 |
| ガードを外し、Start の初期化順または履歴リセット条件を変更する | 車体の物理権限、履歴初期化、最新受理時刻の組み合わせを変える必要がある。武器では Start 前の2件目を通常発射として扱う恐れがある。通常起動まで変える利点が小さい。 |
| 3つとも早着を使わない／理由を記載する | 停止の印は以降再送されない場合があり、説明を足しても初期位置・照準を回復できない。生存まで破棄すると既存のHP保持テストとも両立しない。 |

**変更の境界。** 車体・武器それぞれが持つ印と候補はネットワーク同期の対象外である各クライアントの private フィールド。新しい公開 API、`[UdonSynced]`、ネットワークイベントは不要。同期フィールドの型・順序・個数・`SyncFormat`、送信用の `OnPreSerialization`、`YakimaTankSetupSettings` は変更対象外。作業用の候補を同期フィールドに書き戻さず、受理処理へローカル値を渡す。以下の「受信印」「候補」は新設するローカル状態の役割を表し、既存 API の名称ではない。

### 1.3 車体の変更

1. **受信時に識別する。** `OnDeserialization` は今と同じく `result.sendTime` を転記し、テストと共通の入口 `_AcceptState()` を呼ぶ。未起動なら、位置・物理・受理履歴には触らず、`ClassifyState` の現行条件を満たす候補を保つ。送信者・世代・停止印・運転手・位置・正規化した回転・両速度と、**元の送信時刻**を一組にする。受信印は通常の送信用 `_pendingSend` とは別に持つ。
2. **現在所有者と他送信者を混同しない。** 受信時点で確認できる現在所有者の有効候補は1件保持し、同じ送信者については送信時刻が大きいときだけ置き換える。所有者がまだ不明／送信者が異なるものは既存の `_Hold` の送信者別保留に置く。そこには元の受信時刻も残す。起動前に所有者の見え方が変わり、前の現在所有者の候補を保留へ移す場合も、期限の起点を移し替えた時刻にしない。既存の `StoreHold` の上限・同一送信者の時刻比較を使う。[分類](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L894)・[保留](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L626)・[保留の置き換え](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L1066)
3. **適用は履歴リセットの後。** `Start` の初期情報取得 → `_started = true` → `_RefreshOwner()` → `_BecomeOwner(false)` または `_BecomeRemote()` の順を保つ。非オーナーに確定したら、印がある場合だけ、同じ `Start` の末尾から `_AcceptState()` の起動時処理に入る。印を消費し、現在所有者の候補と、その所有者の有効な保留のうち送信時刻が最も新しい1件を通常と同じ `_UseState` 経由で履歴へ入れる。現在所有者の受信として確定していた停止状態に、所有者不一致用の5秒期限を新設しない。不一致の保留には既存の `HoldUsable`・`HoldLifetime` を適用する。採用した保留は取り除き、他送信者の有効な保留は `OnOwnershipTransferred` → `_UseHoldFor` に残す。候補も有効な保留もなければ履歴は空のまま。[適用](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L609)・[所有者変更時の保留](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L312)
4. **表示と乗車は既存経路に任せる。** `_UseState` が `PushHistory`、`_historyCount`、`_lastUsedTime`、世代を更新する。次の `_UpdateRemote` は `_snapNext` でその位置・姿勢を表示する。先に乗車が成立しても、`OnOwnershipTransferred` → `_BecomeOwner(true)` の359行で履歴が1件以上あり、最新の位置・姿勢・速度・角速度を Rigidbody に渡せる。表示済みの Transform が偶然正しい位置にあることには依存しない。[表示](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L712)・[所有権取得](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L349)

### 1.4 武器の変更

1. 未起動の `_AcceptState()` では、`IsValidState` で形式・角度・発射番号・位置・方向・時刻を確認する。最新候補との比較には同じ `sendTime > lastUsedTime` の規則を用い、採用候補の元の送信時刻と値をローカルに保持する。この段階で `_hasState`、通常の `_lastUsedTime`、表示数は進めない。武器は現行SPECどおり、送信者別の保留を持たない。
2. `Start` で所有者を判定した後、非オーナーで受信印があれば、既存の `_ApplyAngles()` の**直前**で印を消費し、`_AcceptState()` 経由で選んだ候補を1回適用する。初回の `first = !_hasState` が成立するため、`_targetYaw/_targetPitch` と `yaw/pitch`、`loaded`、`fireNumber`、受理時刻を更新し、`_ApplyAngles()` が砲塔と砲身の Transform に反映する。起動時の非オーナーに `_BecomeRemote()` を追加しない。[初期化](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankWeaponSync.cs#L134)・[受理と初回角度](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankWeaponSync.cs#L428)・[角度の描画](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankWeaponSync.cs#L379)
3. 発射を表示する既存の条件は一か所に保つ。初回は `ShouldShowFire(first, …)` が false。同番号の再送も false。古い／等しい送信時刻は `IsValidState` で先に除外される。通常受信に切り替わった後は、新しい時刻・異なる番号・有効な方向なら、受信側で `_LaunchShell(..., false, ...)` と `shownFires++` が1回。番号の大小で判定すると所有権移行後の正当な発射を落とすため、既存の「番号が異なる」を保つ。[発射判定・時刻判定](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankWeaponSync.cs#L520)

### 1.5 起動境界の取り扱い

| 条件 | 設計上の結果 |
|---|---|
| 早着0件 | 印は初期値 false。既存の Start が終了し、最初の通常受信が初回になる。同期変数の Prefab 初期値を受信として扱わない。 |
| 複数回・同時刻・順序逆転・不正値 | 車体は送信者ごと、武器は1件。最新の有効な**送信時刻**を基準にし、同時刻では先に採用した候補を保持。不正な後着で有効な停止位置・照準を消さない。Start 前の中間の動きや発射は再生しない。 |
| 受信時にローカル所有が確認できる | 既存の所有者ルールどおり受信状態を適用しない。ローカルプレイヤー情報が未初期化で所有判断ができない場合は候補として記録し、Start 時点の既存の所有者判定に従う。両 Udon の `_IsLocalOwner` がローカルプレイヤー不在時に true を返す実装に注意。実機で Start まで不在になるかは未確認。 |
| 非オーナーとして早着 → Start 時点ではローカル所有 | 印・初期候補を消費して破棄。車体は `_BecomeOwner(false)`、武器は現在のローカル初期化を使用。後から所有権を手放しても古い候補を再生しない。 |
| Start 後の通常受信／所有権移行 | 起動時の呼び出しで印を先に消し、受理時刻を確定してから通常に戻る。Start 内で同期的に処理し、次フレームへ遅延しない。以後の `OnDeserialization` は既存の所有者・時刻・発射判定を使用。初期候補用の分岐で通常受信を2回処理しない。 |

## 2. 仕組みへの影響

変更箇所は各クライアントの車体・武器が初期状態を受け取る経路。生存の状態、座席、所有権を移す経路、リモートの車体を補間する仕組みは既存のものを使う。実装時に [構成文書](../03_Architecture/architecture.md) の受信・初期化の記述を照合し、記述と動作に差が出る箇所を更新する。

## 3. テストと受け入れ条件

| ID | Issue #11 の受け入れ条件 | 設計箇所 | 証拠 |
|---|---|---|---|
| AC1 | VRChat の途中参加で停止車両が最後の位置に見える | §1.3、停止状態の1回だけの受信で履歴を作る | **人間確認**。A・Bの実機観察（T6） |
| AC2 | 車体が Start 前の状態を Start 後に使う PlayMode テスト | §1.3、履歴初期化後に受理 | **自動判定**。追加の Test Runner テストで受信位置・姿勢・世代を照合（T1・T3） |
| AC3 | 武器が Start 前の角度を使い、過去の発射を出さない PlayMode テスト | §1.4、最初の番号を基準にする | **自動判定**。角度、発射番号、装填、`shownFires`、見た目の砲弾数（T2・T4） |
| AC4 | 途中参加者が乗っても車両が初期位置へ飛ばない | §1.3、`_BecomeOwner(true)` が受理済み履歴を引き継ぐ | **自動判定**。早着させた同じ車両に乗り、Rigidbodyと世代を照合（T1・T3）。**人間確認**はAの画面でも移動しないこと（T6） |

Claude Code が再現・回帰テストを作成する。人間が実行する C#／UdonSharp コンパイル、EditMode・PlayMode は自動判定、VRChat での2人の観察は実機確認として、それぞれ対象コミットと結果を TASKS に記録する。

## 4. 文書とリリース

仕様書の一覧にこの版を追加する。実装のリリース時に `Assets/Yakimannzyuu/YakimaTank/package.json`、同階層の `CHANGELOG.md`、ルート `README.md` の「最近の更新」を v0.6.2 に揃える。

## 5. ユーザーに確認してもらうこと

A が車両を初期位置から動かし、砲塔の角度を変えて1発撃ち、停止・降車する。砲弾が消えてから B が途中参加し、停止位置・角度と過去の砲弾が再生されないことを確かめる。B が乗車して入力を始める前の位置も、A・B の両画面で照合する。手順と記録先は [TASKS の T6](TASKS_2026-11-27_Start前の同期状態の引き継ぎ.md) にある。

## 6. 承認待ちの項目

版・ブランチの選択は確定。実機の挙動と各テストの成否は今後の検証事項。
