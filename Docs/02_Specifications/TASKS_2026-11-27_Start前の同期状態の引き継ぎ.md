# 作業の分解：0.6.2 Start 前の同期状態の引き継ぎ

- 仕様書：[SPEC 0.6.2](SPEC_2026-11-27_Start前の同期状態の引き継ぎ.md) ／ [Issue #11](https://github.com/yakimannzyuu/YakimaTank/issues/11)
- ブランチ：`claude/preserve-prestart-sync`。基準：v0.6.1（`05107ef`）。
- Claude Code がコードとテストを作成し、やきまが Unity のコンパイル、Test Runner、VRChat の実機確認を行う。各タスクは1セッションで引き継げる単位とする。
- `Runtime/` と `Tests/` の起点は `Assets/Yakimannzyuu/YakimaTank/`。

## 作業

- [x] T0　v0.6.2 の SPEC・TASKS と仕様一覧を用意する。
- [ ] T1　車体の Start 前受信と乗車を再現するテストを書く（AC2・AC4）。
- [ ] T2　武器の初回角度・発射抑止を再現するテストを書く（AC3）。
- [ ] T3　車体の受信と所有権引き継ぎを修正し、T1を通す。
- [ ] T4　武器の初回受信を修正し、T2を通す。
- [ ] T5　通常受信の回帰、通信形式の比較、コンパイル・EditMode・PlayMode の結果を記録する。
- [ ] T6　VRChatで途中参加と乗車を確認する（AC1・AC4）。
- [ ] リリース時に版を3か所に反映し、最終の検証結果とともに実装差分をレビューする（SPEC §4）。

## 依存順と次へ進む条件

| 工程 | 依存 | 次へ進める条件 |
|---|---|---|
| T1 車体／T2 武器　再現テストを先に書く | T0（資料作成済み）。T1とT2は独立 | 人間が基準実装＋追加テストを実行し、車体の位置・世代、武器の角度・番号が期待値と違って失敗する。生存の対照テストは成功。準備失敗や「砲弾0個」だけを Red の証拠にしない。 |
| T3 車体／T4 武器　それぞれ修正 | T3 は T1 の Red、T4 は T2 の Red | 各自の新規テストと既存の対応テストが Green。通常受信・オーナー起動で副作用がない。 |
| T5　最終差分と自動判定 | T3・T4 | 人間が同一の最終コミットで C#／UdonSharp のコンパイル、EditMode・PlayMode を実行。件数と AC ごとの結果を保存。 |
| T6　実機で確認・引き渡し | T5 | 人間がVRChatでAC1・AC4を確認。Claude Code が実測結果・未確認を区別してTASKSに転記。 |

## タスク詳細

### T1　車体の早着と乗車を再現する（Red）

- **対象**　`Tests/Runtime/YakimaTankDriveTestBase.Sync.cs`。準備の見本は `Tests/Runtime/YakimaTankDriveTestBase.Life.cs` の `Startの前に届いた生存の状態も使いStartで最大に戻さない`。
- **準備**　`Place(withPlayer: true)` で ClientSim を起動。非アクティブな親の下で生成済み Prefab を複製し、全子の `UdonBehaviour` を `UdonManager.Instance.RegisterUdonBehaviour` で登録。複製の車体をリモート所有にし、その Udon に既存の `InjectInto` で注入する。有効化は見本と同じく親から外し、Start と Update を待つ。同期の再注入や private な `Start` の手呼びは不要。[生存の準備手順](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.Life.cs#L224)・[InjectInto](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.Sync.cs#L106)
- **AC2**　生成位置から明確に離した位置、非ゼロの回転・世代、停止印、速度0を入れる。有効化前は表示位置のまま、有効化後は再送なしで停止位置・姿勢・世代が一致し、数フレーム維持。別ケースで新しい有効値→古い値／不正値を入れ、有効値が残ることも確認する。
- **乗車試験の偽陽性を防ぐ。** 送信時刻には注入時点の `Time.realtimeSinceStartup` を使い、有効化直後・乗車前に、同じ複製の車体に対して既存の `CallReturning<float>` で `_GetAge()` を読む。戻り値が0以上なら受理履歴があり、空なら−1。位置・姿勢が合うことと、この履歴の確認を両方必須にする。基準実装では履歴が空なので、表示位置だけを合わせる修正でも試験は通らない。[履歴の観測点](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankVehicleSync.cs#L772)・[PlayMode の戻り値取得](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.Display.cs#L22)
- **AC4**　複製の車体と武器オブジェクトをそれぞれリモート所有にし、早着後に**複製側の** `YakimaTankBoarding` へ `_interact` を送る。ローカル乗車と所有権取得を待ち、`isOwnerMode`、非kinematic、世代が受信値+1、Rigidbodyの位置・姿勢を確認する。比較対象は生成地点と受信した停止地点の両方。位置・角度の許容差は既存の取得テスト（0.05 m・1度）を出発点に、床と干渉しない姿勢で判定する。この試験では追加入力・再送を与えない。必要に応じ、複製側の Station からテスト終了時に降りる。既存の `Board()`・`LeaveSeat()` は通常車両 `_vehicleRoot` 用。[乗車処理](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Runtime/YakimaTankSeat.cs#L106)・[既存の取得・乗車試験](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.Sync.cs#L310)・[Board と後片付け](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.cs#L183)
- **完了判定**　人間が C#・UdonSharp コンパイル後、指定の PlayMode で AC2・AC4 が基準実装の受信欠落により失敗することを記録する。乗車・所有権移行自体が失敗した場合は試験準備を直す。EditMode に新規計算テストは不要。

### T2　武器の初回角度と発射抑止を再現する（Red）

- **対象**　`Tests/Runtime/YakimaTankDriveTestBase.Weapon.cs`。T1と同じ非アクティブ複製を使い、`YakimaTankRuntimeValues.FindBehaviour` で得た**複製の武器の gameObject 自体**にリモート所有者を設定する。車体の所有者だけを変えると武器はローカル所有のままになり得る。
- **AC3 の入力例**　既存の `InjectWeapon` で、Start 前に（時刻 t、yaw 30°、pitch 5°、装填中、発射番号7、正規化した発射方向）を注入。有効化後の角度・装填・発射番号と、`shownFires = 0`、`ActiveShells(複製, false) = 0` を同時に検証する。続いて新しい時刻の同番号は0回、古い／等しい時刻の異なる番号も0回、新しい時刻の異なる番号で初めて1回。直後の同番号再送でも累計1回。複数早着のケースでは Start 前に番号8、その後に古い7を入れ、Start 後に8を再送しても0回に保つ。[InjectWeapon と砲弾数](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.Weapon.cs#L39)・[通常順序の回帰試験](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.Weapon.cs#L159)
- **完了判定**　人間がコンパイル後に PlayMode を実行。基準実装では「過去弾は0回」だけが通っても、非ゼロ角・装填・番号を取り込めずテストが失敗する。既存の通常順序の試験は通る。

### T3　車体の早着を履歴へ引き継ぐ（Green）

- **対象**　`Runtime/YakimaTankVehicleSync.cs` と T1 のテスト。SPEC §1.3 の受信印・起動前候補・起動時の1回適用を実装する。通常受信の `ClassifyState`、`_UseState`、所有権取得時の物理引き継ぎを再利用する。既存の所有者不一致の保留と、現在所有者の初期候補の寿命を区別する。
- **完了判定**　人間による C#／UdonSharp コンパイル、T1のAC2・AC4、通常の補間・停止・所有者不一致の保留・所有権取得の既存 PlayMode が成功。早着の別送信者Cはその場では車体を動かさず、Cへの移行時にまだ有効なら使用する。ローカル所有での起動はリモート位置で上書きされない。既存 `Tests/Editor/YakimaTankSyncMathTests.cs` の履歴・分類・保留のテストを通す。[既存の別送信者の試験](https://github.com/yakimannzyuu/YakimaTank/blob/05107eff9f2953ebbd90a0531f945a0c1f9fc6a1/Assets/Yakimannzyuu/YakimaTank/Tests/Runtime/YakimaTankDriveTestBase.Sync.cs#L254)

### T4　武器の初期状態を一度だけ適用する（Green）

- **対象**　`Runtime/YakimaTankWeaponSync.cs` と T2 のテスト。SPEC §1.4 の候補保存と適用順を実装。検証・発射判定は既存の式を共有し、起動のためだけの発射経路を増やさない。
- **完了判定**　人間による C#／UdonSharp コンパイル、T2のAC3、通常の受信角度の追従・装填と所有権移行の既存 PlayMode が成功。ローカル所有で起動する試験では、早着による角度変更・見た目の砲弾・後からの二重適用が0。既存 `Tests/Editor/YakimaTankWeaponMathTests.cs` で通常の発射判定も通す。

### T5　通常順序と互換性を確認する

- **対象**　新旧の PlayMode、`Tests/Editor/YakimaTankSyncMathTests.cs`、`YakimaTankWeaponMathTests.cs`、`YakimaTankLifeMathTests.cs`、新規 TASKS。生存の Runtime・既存テストは回帰の基準として使う。
- **自動判定（実行は人間）**　最終コミットで C#／UdonSharp コンパイル、EditMode・PlayMode 全件。早着0件、生存の HP=40・世代=2、車体・武器の通常初回受信、同時刻／古い時刻、オーナーへの移行を含める。基準コミットとのソース差分で同期フィールドの属性・型・順、形式定数、送信コード、ネットワークイベント、設定アセットに差がないことを確認する。
- **完了判定**　AC2・AC3・AC4の自動判定に、テスト名・件数・成否・対象コミット・実行結果を対応付ける。ソース比較は通信形式の検査として記載し、ネットワーク上のバイト列を実測したとは記載しない。

### T6　VRChat で途中参加と乗車を確認する

- **対象**　対象コミットのテストワールド、新規 TASKS の結果欄、実装時のPR本文。コード変更は不要。
- **人間確認**　① A が戦車を初期位置から見分けやすい地点へ動かし、砲塔・砲身を初期角度からずらして少なくとも1発撃ち、停止・降車する。② A の画面でその砲弾が消えてから B が途中参加する。車両を動かさず、Bの画面で停止位置と角度をAの画面と照合する。参加時に過去の砲弾が出ないことも観察する。③ B が同じ戦車に乗り、操作入力前に **A・B 両画面**で初期位置への飛びがないことを確認する。
- **完了判定**　AC1・AC4に、確認日・ビルドまたはコミット・A/Bそれぞれの観察を添える。VRChatでコールバック順を強制できたかは未確認のままでよい。早着を意図的に作った証拠はT1・T2、実機で従来の見え方を保った証拠はT6として記録する。

## 再開の状態

- 完了：T0（資料の作成）。
- 次：T1 と T2。対象の複製車両に受信値を入れ、基準実装で失敗することを人間の実行結果で確認する。
- コンパイル・テストの実行IDと結果：未実行。実行者が対象コミット、テスト名・件数、成否、利用できる runId を記入する。
- VRChatでの確認：v0.6.2 は未確認。T6で確認日、対象ビルド、A・Bそれぞれの観察を記入する。
