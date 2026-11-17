using NUnit.Framework;
using UnityEngine;
using Weapon = Yakimannzyuu.YakimaTank.YakimaTankWeaponSync;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    /// <summary>砲塔・装填・発射の計算（仕様書 0.5.0 の 1.2〜1.6）。</summary>
    public class YakimaTankWeaponMathTests
    {
        private const float Tolerance = 1e-4f;
        private const float TurnRate = 30f;
        private const float DeltaTime = 0.1f;
        private const float PitchMin = -10f;
        private const float PitchMax = 20f;

        [Test]
        public void Desktopは目標の左右の角度へ回る速さで向き近い向きへ回る()
        {
            Assert.That(Weapon.StepYawToTarget(0f, 90f, TurnRate, DeltaTime), Is.EqualTo(3f).Within(Tolerance), "回る速さ × 時間だけ");
            Assert.That(Weapon.StepYawToTarget(89f, 90f, TurnRate, DeltaTime), Is.EqualTo(90f).Within(Tolerance), "目標を越えない");
            Assert.That(Weapon.StepYawToTarget(170f, -170f, TurnRate, DeltaTime), Is.EqualTo(173f).Within(Tolerance), "180 度をまたいで近い向きへ");
            Assert.That(Weapon.StepYawToTarget(179f, -170f, TurnRate, DeltaTime), Is.EqualTo(-178f).Within(Tolerance), "180 度をまたいだら -180〜180 に戻す");
        }

        [Test]
        public void Desktopは目標の上下の角度へ回る速さで向き動ける範囲で止まる()
        {
            Assert.That(Weapon.StepPitchToTarget(0f, 10f, 15f, DeltaTime, PitchMin, PitchMax), Is.EqualTo(1.5f).Within(Tolerance));
            Assert.That(Weapon.StepPitchToTarget(19.5f, 60f, 15f, DeltaTime, PitchMin, PitchMax), Is.EqualTo(PitchMax).Within(Tolerance), "上限で止まる");
            Assert.That(Weapon.StepPitchToTarget(-9.5f, -60f, 15f, DeltaTime, PitchMin, PitchMax), Is.EqualTo(PitchMin).Within(Tolerance), "下限で止まる");
        }

        [Test]
        public void VRは量に回る速さを掛けて回し左右は全周で上下は範囲で止まる()
        {
            Assert.That(Weapon.StepYawByRate(0f, 0.5f, TurnRate, DeltaTime), Is.EqualTo(1.5f).Within(Tolerance));
            Assert.That(Weapon.StepYawByRate(179f, 1f, TurnRate, DeltaTime), Is.EqualTo(-178f).Within(Tolerance), "全周");
            Assert.That(Weapon.StepYawByRate(0f, 3f, TurnRate, DeltaTime), Is.EqualTo(3f).Within(Tolerance), "量は -1〜1 に丸める");
            Assert.That(Weapon.StepPitchByRate(19f, 1f, 15f, DeltaTime, PitchMin, PitchMax), Is.EqualTo(PitchMax).Within(Tolerance));
            Assert.That(Weapon.StepPitchByRate(0f, -0.5f, 15f, DeltaTime, PitchMin, PitchMax), Is.EqualTo(-0.75f).Within(Tolerance));
        }

        [Test]
        public void オーナー以外は最新の角度へ回る速さで寄せる()
        {
            Assert.That(Weapon.StepYawToTarget(0f, 10f, TurnRate, DeltaTime), Is.EqualTo(3f).Within(Tolerance));
            Assert.That(Weapon.StepPitchToTarget(0f, 5f, 15f, DeltaTime, PitchMin, PitchMax), Is.EqualTo(1.5f).Within(Tolerance));
        }

        [Test]
        public void 装填済みで撃てる状態で射撃の入力があれば撃つ()
        {
            Assert.That(Weapon.ShouldFire(true, true, true), Is.True);
            Assert.That(Weapon.ShouldFire(false, true, true), Is.False, "装填中");
            Assert.That(Weapon.ShouldFire(true, false, true), Is.False, "射撃の入力なし");
            Assert.That(Weapon.ShouldFire(true, true, false), Is.False, "所有権がそろっていない・座っていない");
        }

        [Test]
        public void 装填の時間がたつと装填済みになる()
        {
            Assert.That(Weapon.IsReloaded(2.99f, 3f), Is.False);
            Assert.That(Weapon.IsReloaded(3f, 3f), Is.True);
        }

        [Test]
        public void 発射の番号は受けた番号と自分が前に撃った最後の番号の大きい方から続ける()
        {
            Assert.That(Weapon.NextFireNumber(0, 0), Is.EqualTo(1), "最初の 1 発");
            Assert.That(Weapon.NextFireNumber(10, 3), Is.EqualTo(11), "受けた番号が新しい");
            Assert.That(Weapon.NextFireNumber(5, 12), Is.EqualTo(13), "古い番号から引き継いでも、自分の前の番号より大きくする");
        }

        [Test]
        public void 発射の表示は番号が覚えている番号と違うときだけで最初の状態では出さない()
        {
            Assert.That(Weapon.ShouldShowFire(false, 4, 3), Is.True);
            Assert.That(Weapon.ShouldShowFire(false, 3, 3), Is.False, "同じ番号を 2 回");
            Assert.That(Weapon.ShouldShowFire(true, 4, 0), Is.False, "途中参加の最初の状態");
            Assert.That(Weapon.ShouldShowFire(false, 2, 7), Is.True, "所有権が移って番号が戻っても出す（時刻の新しさは受けるときに確かめる）");
        }

        private static bool Valid(byte format = Weapon.SyncFormat, float yaw = 30f, float pitch = 5f, Vector3? position = null, Vector3? direction = null,
            float sendTime = 10f, float lastUsed = 9f)
        {
            return Weapon.IsValidState(format, yaw, pitch, position ?? new Vector3(1f, 2f, 3f), direction ?? Vector3.forward, sendTime, lastUsed, PitchMin, PitchMax);
        }

        [Test]
        public void 正しい状態は使い形式違い非有限範囲外の角度古い時刻は使わない()
        {
            Assert.That(Valid(), Is.True);
            Assert.That(Valid(direction: Vector3.zero), Is.True, "まだ撃っていない（向きが 0）");
            Assert.That(Valid(format: 2), Is.False, "形式");
            Assert.That(Valid(yaw: float.NaN), Is.False, "左右の NaN");
            Assert.That(Valid(yaw: 181f), Is.False, "左右の範囲");
            Assert.That(Valid(pitch: 25f), Is.False, "上下の範囲");
            Assert.That(Valid(pitch: float.PositiveInfinity), Is.False, "上下の無限大");
            Assert.That(Valid(position: new Vector3(0f, float.NaN, 0f)), Is.False, "砲口の位置の NaN");
            Assert.That(Valid(position: new Vector3(0f, 0f, 100001f)), Is.False, "砲口の位置の上限");
            Assert.That(Valid(direction: new Vector3(0f, 0f, 2f)), Is.False, "向きの大きさ");
            Assert.That(Valid(sendTime: 9f, lastUsed: 9f), Is.False, "同じ時刻");
            Assert.That(Valid(sendTime: 8f, lastUsed: 9f), Is.False, "古い時刻");
        }

        [Test]
        public void 砲弾は弾速と重力で進む()
        {
            var position = new Vector3(0f, 10f, 0f);
            var velocity = new Vector3(0f, 0f, 200f);
            Vector3 next = Weapon.ShellPosition(position, velocity, 9.81f, 0.1f);
            Assert.That(Vector3.Distance(next, new Vector3(0f, 10f - 0.5f * 9.81f * 0.01f, 20f)), Is.LessThan(Tolerance));
            Vector3 nextVelocity = Weapon.ShellVelocity(velocity, 9.81f, 0.1f);
            Assert.That(Vector3.Distance(nextVelocity, new Vector3(0f, -0.981f, 200f)), Is.LessThan(Tolerance));
        }

        [Test]
        public void 光線の当たりはワールドの物かほかの車両の命中用のトリガーのうち近い方を選び自分の車両と乗車の判定は通り抜ける()
        {
            float[] distances = { 5f, 2f, 3f, 8f };
            bool[] solids = { true, false, false, false };
            bool[] targets = { false, false, true, false };
            Assert.That(Weapon.ChooseHit(distances, solids, targets, 4), Is.EqualTo(2), "近いトリガー（自分の車両・乗車の判定）は飛ばし、ほかの車両の命中用のトリガー");

            targets[2] = false;
            Assert.That(Weapon.ChooseHit(distances, solids, targets, 4), Is.EqualTo(0), "壁で止まる");

            solids[0] = false;
            Assert.That(Weapon.ChooseHit(distances, solids, targets, 4), Is.EqualTo(-1), "何にも当たらない");
            Assert.That(Weapon.ChooseHit(distances, solids, targets, 0), Is.EqualTo(-1), "光線が何も拾わない");
        }
    }
}
