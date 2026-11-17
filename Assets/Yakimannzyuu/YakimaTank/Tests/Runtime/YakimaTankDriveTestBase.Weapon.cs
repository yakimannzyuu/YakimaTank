using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using Yakimannzyuu.YakimaTank.Editor;
using Weapon = Yakimannzyuu.YakimaTank.YakimaTankWeaponSync;

namespace Yakimannzyuu.YakimaTank.Tests.PlayMode
{
    /// <summary>
    /// 砲塔・装填・発射の同期（仕様書 0.5.0 の 3 節の PlayMode）。
    /// ClientSim は別のクライアントからの受信を再現できないので、受ける状態は <see cref="YakimaTankWeaponSync._AcceptState"/> に直接入れる。
    /// </summary>
    public abstract partial class YakimaTankDriveTestBase
    {
        private UdonBehaviour WeaponUdon => Part(typeof(Weapon));

        private GameObject WeaponObject => WeaponUdon.gameObject;

        private int FireNumber => Read<int>(WeaponUdon, nameof(Weapon.fireNumber));

        /// <summary>座って、入力アダプターを止め、射撃の入力を共通操作値に直接書けるようにする。</summary>
        private IEnumerator BoardAndHoldFire(bool fire)
        {
            yield return Board();
            StopAdapters();
            Part(typeof(YakimaTankControlState)).SetProgramVariable(nameof(YakimaTankControlState.fire), fire);
        }

        private void HoldFire(bool fire)
        {
            Part(typeof(YakimaTankControlState)).SetProgramVariable(nameof(YakimaTankControlState.fire), fire);
        }

        private static void InjectWeapon(UdonBehaviour weapon, float sendTime, float yaw, float pitch, bool loaded, int fireNumber, Vector3 position, Vector3 direction)
        {
            weapon.SetProgramVariable(nameof(Weapon.syncFormat), Weapon.SyncFormat);
            weapon.SetProgramVariable(nameof(Weapon.syncYaw), yaw);
            weapon.SetProgramVariable(nameof(Weapon.syncPitch), pitch);
            weapon.SetProgramVariable(nameof(Weapon.syncLoaded), loaded);
            weapon.SetProgramVariable(nameof(Weapon.syncFireNumber), fireNumber);
            weapon.SetProgramVariable(nameof(Weapon.syncFirePosition), position);
            weapon.SetProgramVariable(nameof(Weapon.syncFireDirection), direction);
            weapon.SetProgramVariable(nameof(Weapon.incomingSendTime), sendTime);
            weapon.SendCustomEvent(nameof(Weapon._AcceptState));
        }

        private int ActiveShells(GameObject root, bool counting)
        {
            return root.GetComponentsInChildren<UdonBehaviour>(true)
                .Where(b => UdonSharpEditor.UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankShell))
                .Count(b => Flag(b, nameof(YakimaTankShell.active)) && Flag(b, nameof(YakimaTankShell.counting)) == counting);
        }

        [UnityTest]
        public IEnumerator 車体と武器の所有権がほかの人にあっても座ると続けて両方を取り両方そろってから入力が有効になる()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, _vehicleRoot);
            Session.SetOwner(other, WeaponObject);
            yield return null;
            Assert.That(Flag(WeaponUdon, nameof(Weapon.isOwnerMode)), Is.False, "武器がほかの人のもの（判定にならない）");

            yield return Board();
            yield return null;

            Assert.That(Flag(SyncUdon, nameof(YakimaTankVehicleSync.isOwnerMode)), Is.True, "車体の所有権の通知が来ない");
            Assert.That(Flag(WeaponUdon, nameof(Weapon.isOwnerMode)), Is.True, "武器の所有権の通知が来ない");
            Assert.That(Flag(Part(typeof(YakimaTankSeat)), nameof(YakimaTankSeat.waitingOwnership)), Is.False);
            Assert.That(Flag(Part(typeof(YakimaTankDesktopInput)), nameof(YakimaTankDesktopInput.isActive)), Is.True);
        }

        [UnityTest]
        public IEnumerator 所有権を待つ間は射撃の入力があっても撃たず両方そろうと撃つ()
        {
            yield return Place(withPlayer: true);
            yield return BoardAndHoldFire(false);
            UdonBehaviour seat = Part(typeof(YakimaTankSeat));

            // ClientSim では所有権がすぐに来て待ちにならないので、待ちの状態を作る。
            seat.SetProgramVariable(nameof(YakimaTankSeat.waitingOwnership), true);
            HoldFire(true);
            yield return null;
            yield return null;
            Assert.That(FireNumber, Is.EqualTo(0), "待つ間に撃った");

            seat.SetProgramVariable(nameof(YakimaTankSeat.waitingOwnership), false);
            yield return null;
            yield return null;
            Assert.That(FireNumber, Is.EqualTo(1), "そろったのに撃たない");
        }

        [UnityTest]
        public IEnumerator 撃つと番号が進み砲口の位置と向きが入ってすぐに送り押し続けても装填ごとに1発ずつ撃つ()
        {
            yield return Place(withPlayer: true);
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.reloadTime = 0.5f));
            yield return PhysicsSeconds(0.5f);
            yield return BoardAndHoldFire(false);
            UdonBehaviour weapon = WeaponUdon;
            int requested = Read<int>(weapon, nameof(Weapon.requestedCount));

            HoldFire(true);
            yield return null;
            yield return null;
            Assert.That(FireNumber, Is.EqualTo(1));
            Assert.That(Flag(weapon, nameof(Weapon.loaded)), Is.False, "撃ったら装填中");
            Assert.That(Read<int>(weapon, nameof(Weapon.requestedCount)), Is.GreaterThan(requested), "撃ったらすぐに送る");
            weapon.OnPreSerialization();
            var muzzle = Read<Transform>(weapon, nameof(Weapon.muzzle));
            Assert.That(Read<int>(weapon, nameof(Weapon.syncFireNumber)), Is.EqualTo(1));
            Assert.That(Vector3.Distance(Read<Vector3>(weapon, nameof(Weapon.syncFirePosition)), muzzle.position), Is.LessThan(0.5f), "砲口の位置");
            Assert.That(Vector3.Angle(Read<Vector3>(weapon, nameof(Weapon.syncFireDirection)), muzzle.forward), Is.LessThan(1f), "砲口の向き");
            Assert.That(ActiveShells(_vehicleRoot, true), Is.EqualTo(1), "自分の砲弾（命中を数える）");

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 0.3f)
            {
                yield return null;
            }

            Assert.That(FireNumber, Is.EqualTo(1), "装填の前に撃った");
            while (Time.realtimeSinceStartup - start < 0.8f)
            {
                yield return null;
            }

            Assert.That(FireNumber, Is.EqualTo(2), "押し続けて、装填が終わったら次を撃つ");
        }

        [UnityTest]
        public IEnumerator 受けた発射の番号が変わると見た目の砲弾を1回出し同じ番号と古い時刻と最初の状態では出さない()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, WeaponObject);
            yield return null;
            UdonBehaviour weapon = WeaponUdon;
            Vector3 position = _vehicleRoot.transform.position + new Vector3(0f, 2f, 0f);
            float now = Time.realtimeSinceStartup;

            InjectWeapon(weapon, now, 0f, 0f, false, 5, position, Vector3.forward);
            Assert.That(Read<int>(weapon, nameof(Weapon.shownFires)), Is.EqualTo(0), "途中参加の最初の状態で過去の発射を出した");

            InjectWeapon(weapon, now + 0.1f, 0f, 0f, false, 6, position, Vector3.forward);
            Assert.That(Read<int>(weapon, nameof(Weapon.shownFires)), Is.EqualTo(1));
            Assert.That(ActiveShells(_vehicleRoot, false), Is.EqualTo(1), "見た目の砲弾（命中を数えない）");

            InjectWeapon(weapon, now + 0.2f, 0f, 0f, true, 6, position, Vector3.forward);
            Assert.That(Read<int>(weapon, nameof(Weapon.shownFires)), Is.EqualTo(1), "同じ番号を 2 回受けて出した");

            InjectWeapon(weapon, now + 0.15f, 0f, 0f, false, 7, position, Vector3.forward);
            Assert.That(Read<int>(weapon, nameof(Weapon.shownFires)), Is.EqualTo(1), "古い時刻の状態で出した");
            Assert.That(Read<int>(weapon, nameof(Weapon.rejectedCount)), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator オーナー以外は受けた角度へ回る速さで寄せる()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, WeaponObject);
            yield return null;
            UdonBehaviour weapon = WeaponUdon;
            float now = Time.realtimeSinceStartup;
            InjectWeapon(weapon, now, 0f, 0f, true, 0, Vector3.zero, Vector3.zero);
            InjectWeapon(weapon, now + 0.1f, 90f, 10f, true, 0, Vector3.zero, Vector3.zero);
            yield return null;
            float early = Read<float>(weapon, nameof(Weapon.yaw));
            Assert.That(early, Is.GreaterThan(0f).And.LessThan(45f), "すぐには置かず、回る速さで寄せる");

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 4f)
            {
                yield return null;
            }

            Assert.That(Read<float>(weapon, nameof(Weapon.yaw)), Is.EqualTo(90f).Within(0.01f));
            Assert.That(Read<float>(weapon, nameof(Weapon.pitch)), Is.EqualTo(10f).Within(0.01f));
            var turret = Read<Transform>(weapon, nameof(Weapon.turret));
            Assert.That(Vector3.SignedAngle(_vehicleRoot.transform.forward, turret.forward, _vehicleRoot.transform.up), Is.EqualTo(90f).Within(0.5f), "砲塔が右へ 90 度");
        }

        [UnityTest]
        public IEnumerator 装填中に武器の所有権が移ると新しいオーナーが数え直し装填済みなら引き継ぐ([Values(false, true)] bool loaded)
        {
            yield return Place(withPlayer: true);
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.reloadTime = 1f));
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, WeaponObject);
            yield return null;
            UdonBehaviour weapon = WeaponUdon;
            InjectWeapon(weapon, Time.realtimeSinceStartup, 0f, 0f, loaded, 3, Vector3.zero, Vector3.forward);

            Session.SetOwner(Networking.LocalPlayer, WeaponObject);
            yield return null;
            Assert.That(Flag(weapon, nameof(Weapon.isOwnerMode)), Is.True);
            Assert.That(Flag(weapon, nameof(Weapon.loaded)), Is.EqualTo(loaded), loaded ? "装填済みを引き継がない" : "装填中なのに装填済み");

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 0.6f)
            {
                yield return null;
            }

            Assert.That(Flag(weapon, nameof(Weapon.loaded)), Is.EqualTo(loaded), "装填の時間を最初から数え直していない");
            while (Time.realtimeSinceStartup - start < 1.3f)
            {
                yield return null;
            }

            Assert.That(Flag(weapon, nameof(Weapon.loaded)), Is.True);
        }

        [UnityTest]
        public IEnumerator 発射の番号は受けた番号と自分が前に撃った番号の大きい方から続ける()
        {
            yield return Place(withPlayer: true);
            yield return BoardAndHoldFire(true);
            yield return null;
            yield return null;
            Assert.That(FireNumber, Is.EqualTo(1), "撃っていない（判定にならない）");
            HoldFire(false);
            yield return Exit();

            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, WeaponObject);
            yield return null;
            UdonBehaviour weapon = WeaponUdon;
            InjectWeapon(weapon, Time.realtimeSinceStartup, 0f, 0f, true, 0, Vector3.zero, Vector3.forward);
            Session.SetOwner(Networking.LocalPlayer, WeaponObject);
            yield return null;

            yield return BoardAndHoldFire(true);
            yield return null;
            yield return null;
            Assert.That(FireNumber, Is.EqualTo(2), "古い番号（0）から引き継いでも、自分の前の番号（1）より大きくする");
            Assert.That(Read<int>(weapon, nameof(Weapon.lastOwnFire)), Is.EqualTo(2));
        }

        private GameObject PlaceTarget(float distance)
        {
            _other = YakimaTankPlayTuning.Spawn(_prefab, _vehicleRoot.transform.position + _vehicleRoot.transform.forward * distance, _vehicleRoot.transform.rotation);
            SpawnRegistry(_vehicleRoot, _other);
            return _other;
        }

        [UnityTest]
        public IEnumerator 自分の砲弾がほかの車両の命中用のトリガーに当たると当たった車両の番号を記録し自分の車両と乗車の判定は通り抜ける()
        {
            yield return Place(withPlayer: true);
            PlaceTarget(20f);
            yield return PhysicsSeconds(1f);
            UdonBehaviour targetSync = YakimaTankRuntimeValues.FindBehaviour(_other, typeof(YakimaTankVehicleSync));
            int targetIndex = Read<int>(targetSync, nameof(YakimaTankVehicleSync.vehicleIndex));
            Assert.That(targetIndex, Is.EqualTo(1), "一覧の番号が知らされていない");
            Assert.That(Read<int>(SyncUdon, nameof(YakimaTankVehicleSync.vehicleIndex)), Is.EqualTo(0));

            yield return BoardAndHoldFire(true);
            float start = Time.realtimeSinceStartup;
            while (Read<int>(WeaponUdon, nameof(Weapon.hitCount)) == 0 && Time.realtimeSinceStartup - start < 2f)
            {
                yield return null;
            }

            Assert.That(FireNumber, Is.GreaterThanOrEqualTo(1), "撃っていない（判定にならない）");
            Assert.That(Read<int>(WeaponUdon, nameof(Weapon.hitCount)), Is.EqualTo(1));
            Assert.That(Read<int>(WeaponUdon, nameof(Weapon.lastHitVehicle)), Is.EqualTo(targetIndex));
        }

        [UnityTest]
        public IEnumerator 砲弾は壁で止まり向こうの車両には当たらない()
        {
            yield return Place(withPlayer: true);
            PlaceTarget(20f);
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.position = _vehicleRoot.transform.position + _vehicleRoot.transform.forward * 10f + Vector3.up * 2f;
            wall.transform.rotation = _vehicleRoot.transform.rotation;
            wall.transform.localScale = new Vector3(10f, 4f, 0.5f);
            yield return PhysicsSeconds(1f);

            yield return BoardAndHoldFire(true);
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 1f)
            {
                yield return null;
            }

            Assert.That(FireNumber, Is.GreaterThanOrEqualTo(1), "撃っていない（判定にならない）");
            Assert.That(ActiveShells(_vehicleRoot, true), Is.EqualTo(0), "砲弾が壁で止まらない");
            Assert.That(Read<int>(WeaponUdon, nameof(Weapon.hitCount)), Is.EqualTo(0), "壁の向こうの車両に当たった");
        }

        [UnityTest]
        public IEnumerator 武器の所有権を失うと飛んでいる自分の砲弾は命中を数えずに消え座っていれば降ろされる()
        {
            yield return Place(withPlayer: true);
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.shellSpeed = 2f));
            PlaceTarget(20f);
            yield return PhysicsSeconds(1f);
            yield return BoardAndHoldFire(true);
            yield return null;
            yield return null;
            Assert.That(ActiveShells(_vehicleRoot, true), Is.EqualTo(1), "飛んでいる砲弾がない（判定にならない）");

            Session.SetOwner(Session.SpawnRemotePlayer("B"), WeaponObject);
            yield return null;

            Assert.That(ActiveShells(_vehicleRoot, true), Is.EqualTo(0), "所有権を失っても自分の砲弾が飛んでいる");
            Assert.That(Flag(Part(typeof(YakimaTankSeat)), nameof(YakimaTankSeat.localSeated)), Is.False, "座ったまま武器の所有権が移ったのに降ろされない");
            Assert.That(Read<int>(WeaponUdon, nameof(Weapon.hitCount)), Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator 座ったまま砲塔を回すと座席が位置の参照に付いて行き向きは車体のまま()
        {
            yield return Place(withPlayer: true);
            var offset = new Vector3(0.3f, 0f, 0f);
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.seatOffset = offset));
            yield return PhysicsSeconds(1f);
            yield return BoardAndHoldFire(false);
            UdonBehaviour seat = Part(typeof(YakimaTankSeat));
            Vector3 before = seat.transform.position;

            // Desktop：頭を右へ 90 度向けた入力（車体から見た向き）。
            Part(typeof(YakimaTankControlState)).SetProgramVariable(nameof(YakimaTankControlState.turretYawInput), 0.5f);
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 4f)
            {
                yield return null;
            }

            yield return new WaitForEndOfFrame();
            Assert.That(Read<float>(WeaponUdon, nameof(Weapon.yaw)), Is.EqualTo(90f).Within(0.01f), "見た方へ向いていない");
            var anchor = Read<Transform>(seat, nameof(YakimaTankSeat.seatAnchor));
            Vector3 expected = anchor.position + anchor.rotation * offset;
            Assert.That(Vector3.Distance(seat.transform.position, expected), Is.LessThan(1e-3f), "座席が位置の参照に付いて行かない");
            Assert.That(Vector3.Distance(seat.transform.position, before), Is.GreaterThan(0.2f), "ずらした位置が砲塔と一緒に回っていない（判定にならない）");
            Assert.That(Quaternion.Angle(seat.transform.rotation, _vehicleRoot.transform.rotation), Is.LessThan(0.01f), "座席が砲塔と一緒に回った");
            Assert.That(Vector3.Distance(Networking.LocalPlayer.GetPosition(), seat.transform.position), Is.LessThan(0.5f), "座った人が座席に付いて行かない");
        }
    }
}
