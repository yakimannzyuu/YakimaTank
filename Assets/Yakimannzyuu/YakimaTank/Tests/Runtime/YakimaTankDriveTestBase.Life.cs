using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;
using VRC.Udon;
using Yakimannzyuu.YakimaTank.Editor;
using Life = Yakimannzyuu.YakimaTank.YakimaTankLife;
using Weapon = Yakimannzyuu.YakimaTank.YakimaTankWeaponSync;

namespace Yakimannzyuu.YakimaTank.Tests.PlayMode
{
    /// <summary>
    /// HP・破壊・復活の同期（仕様書 0.6.0 の 3 節の PlayMode）。
    /// ClientSim は、オーナー宛てのイベントをいつも自分で実行し、送った人も自分になる。受ける申告は <see cref="YakimaTankLife._AcceptHit"/> に、
    /// 受ける状態は <see cref="YakimaTankLife._AcceptState"/> に直接入れる。自分の車両は一覧の 0 番、相手は 1 番。
    /// </summary>
    public abstract partial class YakimaTankDriveTestBase
    {
        private const int OwnIndex = 0;
        private const int ShooterIndex = 1;

        private UdonBehaviour LifeUdon => Part(typeof(Life));

        private int Health => Read<int>(LifeUdon, nameof(Life.health));

        private int RespawnGeneration => Read<int>(LifeUdon, nameof(Life.respawnGeneration));

        private static int LocalId => Networking.LocalPlayer.playerId;

        /// <summary>自分の車両の横に相手の車両を置き、シーンの車両の一覧（自分 0 番・相手 1 番）を作る。</summary>
        private IEnumerator PlaceWithShooter()
        {
            yield return Place(withPlayer: true);
            _other = YakimaTankPlayTuning.Spawn(_prefab, _vehicleRoot.transform.position + new Vector3(30f, 0f, 0f), _vehicleRoot.transform.rotation);
            SpawnRegistry(_vehicleRoot, _other);
            yield return PhysicsSeconds(0.5f);
            Assert.That(Read<int>(SyncUdon, nameof(YakimaTankVehicleSync.vehicleIndex)), Is.EqualTo(OwnIndex), "一覧の番号が知らされていない");
            Assert.That(Health, Is.EqualTo(100), "最初の HP");
        }

        private UdonBehaviour ShooterWeapon => YakimaTankRuntimeValues.FindBehaviour(_other, typeof(Weapon));

        private static void InjectHit(UdonBehaviour life, int player, int shooter, int fireNumber, int generation)
        {
            life.SetProgramVariable(nameof(Life.incomingHitPlayer), player);
            life.SetProgramVariable(nameof(Life.incomingHitShooter), shooter);
            life.SetProgramVariable(nameof(Life.incomingHitNumber), fireNumber);
            life.SetProgramVariable(nameof(Life.incomingHitGeneration), generation);
            life.SendCustomEvent(nameof(Life._AcceptHit));
        }

        private static void InjectLife(UdonBehaviour life, float sendTime, int health, int generation, int[] players, int[] numbers)
        {
            life.SetProgramVariable(nameof(Life.syncFormat), Life.SyncFormat);
            life.SetProgramVariable(nameof(Life.syncHealth), health);
            life.SetProgramVariable(nameof(Life.syncRespawnGeneration), generation);
            life.SetProgramVariable(nameof(Life.syncHitPlayers), players);
            life.SetProgramVariable(nameof(Life.syncHitNumbers), numbers);
            life.SetProgramVariable(nameof(Life.incomingSendTime), sendTime);
            life.SendCustomEvent(nameof(Life._AcceptState));
        }

        private int LastHitResult => Read<int>(LifeUdon, nameof(Life.lastHitResult));

        /// <summary>4 発（既定のダメージ 25）当てて破壊する。番号は first から。</summary>
        private void DestroyByHits(int first)
        {
            for (int number = first; number < first + 4; number++)
            {
                InjectHit(LifeUdon, LocalId, ShooterIndex, number, RespawnGeneration);
            }

            Assert.That(Health, Is.EqualTo(0), "破壊されない（判定にならない）");
        }

        [UnityTest]
        public IEnumerator 同じ命中を2回入れてもHPは1回分だけ減り番号が進めば減りHPのバーが縮む()
        {
            yield return PlaceWithShooter();
            UdonBehaviour life = LifeUdon;
            int requested = Read<int>(life, nameof(Life.requestedCount));

            InjectHit(life, LocalId, ShooterIndex, 5, 0);
            Assert.That(Health, Is.EqualTo(75), "ダメージは撃った車両の武器の値（既定 25）");
            Assert.That(Read<int>(life, nameof(Life.requestedCount)), Is.EqualTo(requested + 1), "HP を変えたら送る");

            InjectHit(life, LocalId, ShooterIndex, 5, 0);
            Assert.That(Health, Is.EqualTo(75), "同じ命中を 2 回数えた");
            Assert.That(LastHitResult, Is.EqualTo(Life.HitDuplicate));
            InjectHit(life, LocalId, ShooterIndex, 4, 0);
            Assert.That(Health, Is.EqualTo(75), "前の番号の命中を数えた");

            InjectHit(life, LocalId, ShooterIndex, 6, 0);
            Assert.That(Health, Is.EqualTo(50));
            Assert.That(Read<int>(life, nameof(Life.acceptedHits)), Is.EqualTo(2));
            Assert.That(Read<int>(life, nameof(Life.rejectedHits)), Is.EqualTo(2));

            yield return null;
            var fill = (Transform)life.GetProgramVariable(nameof(Life.healthFill));
            Assert.That(fill.localScale.x, Is.EqualTo(0.5f).Within(1e-4f), "HP のバーの長さ");
        }

        [UnityTest]
        public IEnumerator 自分自身や範囲の外の車両からの申告と負の発射の番号は使わない()
        {
            yield return PlaceWithShooter();

            InjectHit(LifeUdon, LocalId, OwnIndex, 1, 0);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitBadShooter), "自分自身");
            InjectHit(LifeUdon, LocalId, 2, 1, 0);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitBadShooter), "一覧の範囲の外");
            InjectHit(LifeUdon, LocalId, ShooterIndex, -1, 0);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitBadNumber));
            Assert.That(Health, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator 送った人が撃った車両の武器のオーナーでない申告は使わない()
        {
            yield return PlaceWithShooter();
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, ShooterWeapon.gameObject);
            yield return null;

            InjectHit(LifeUdon, LocalId, ShooterIndex, 1, 0);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitWrongSender), "武器を持っていない自分の申告");
            Assert.That(Health, Is.EqualTo(100));

            InjectHit(LifeUdon, other.playerId, ShooterIndex, 1, 0);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitUse), "武器のオーナーの申告");
            Assert.That(Health, Is.EqualTo(75));
        }

        [UnityTest]
        public IEnumerator オーナー以外は申告でHPを書かず受けた状態を確かめて使う()
        {
            yield return PlaceWithShooter();
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, _vehicleRoot);
            yield return null;
            UdonBehaviour life = LifeUdon;
            int requested = Read<int>(life, nameof(Life.requestedCount));

            InjectHit(life, LocalId, ShooterIndex, 1, 0);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitNotOwner));
            Assert.That(Health, Is.EqualTo(100), "オーナー以外が HP を書いた");
            Assert.That(Read<int>(life, nameof(Life.requestedCount)), Is.EqualTo(requested), "オーナー以外が送った");

            float now = Time.realtimeSinceStartup;
            InjectLife(life, now, 40, 2, new[] { Life.NoPlayer, other.playerId }, new[] { -1, 3 });
            Assert.That(Health, Is.EqualTo(40));
            Assert.That(RespawnGeneration, Is.EqualTo(2));

            InjectLife(life, now - 0.1f, 90, 2, new[] { Life.NoPlayer, other.playerId }, new[] { -1, 3 });
            InjectLife(life, now + 0.1f, 101, 2, new[] { Life.NoPlayer, other.playerId }, new[] { -1, 3 });
            InjectLife(life, now + 0.2f, 90, 2, new[] { Life.NoPlayer }, new[] { -1 });
            Assert.That(Health, Is.EqualTo(40), "古い時刻・最大より大きい HP・シーンの車両の数と違う配列の状態を使った");
            Assert.That(Read<int>(life, nameof(Life.rejectedCount)), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator 破壊されると乗員が降り乗車の判定が無効になり走行の入力が止まる()
        {
            yield return PlaceWithShooter();
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.respawnDelay = 100f));
            yield return PhysicsSeconds(0.5f);
            yield return Board();

            DestroyByHits(1);
            yield return null;

            Assert.That(Flag(Part(typeof(YakimaTankSeat)), nameof(YakimaTankSeat.localSeated)), Is.False, "破壊されても座ったまま");
            Assert.That(BoardingArea.enabled, Is.False, "破壊の間に乗車の判定が有効");
            Part(typeof(YakimaTankBoarding)).SendCustomEvent("_interact");
            yield return null;
            yield return null;
            Assert.That(Flag(Part(typeof(YakimaTankSeat)), nameof(YakimaTankSeat.localSeated)), Is.False, "破壊の間に乗れた");

            SetInput(1f, 0f, 0f);
            yield return PhysicsSeconds(0.5f);
            Assert.That(Read<float>(_vehicle, nameof(YakimaTankVehicle.throttleInput)), Is.EqualTo(0f), "破壊の間に走行の入力が残る");
            Assert.That(Mathf.Abs(ForwardSpeed), Is.LessThan(0.1f), "破壊の間に走った");

            yield return null;
            var fill = (Transform)LifeUdon.GetProgramVariable(nameof(Life.healthFill));
            Assert.That(fill.localScale.x, Is.EqualTo(0f), "破壊の間の HP のバー");
        }

        [UnityTest]
        public IEnumerator 待ち時間の後に復活し車体と武器とHPが初期値に戻り世代が進む()
        {
            yield return PlaceWithShooter();
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.respawnDelay = 0.5f));
            yield return PhysicsSeconds(0.5f);
            Vector3 spawn = _vehicleRoot.transform.position;
            Body.position = spawn + new Vector3(0f, 0f, 5f);
            _vehicleRoot.transform.position = Body.position;
            UdonBehaviour weapon = WeaponUdon;
            weapon.SetProgramVariable(nameof(Weapon.yaw), 40f);
            weapon.SetProgramVariable(nameof(Weapon.loaded), false);
            int vehicleGeneration = Read<int>(_vehicle, nameof(YakimaTankVehicle.generation));
            yield return PhysicsSeconds(0.2f);

            DestroyByHits(1);
            yield return PhysicsSeconds(0.2f);
            Assert.That(Health, Is.EqualTo(0), "待ち時間の前に復活した");

            float start = Time.realtimeSinceStartup;
            while (RespawnGeneration == 0 && Time.realtimeSinceStartup - start < 3f)
            {
                yield return null;
            }

            Assert.That(RespawnGeneration, Is.EqualTo(1), "復活しない");
            Assert.That(Health, Is.EqualTo(100));
            Assert.That(Read<int>(_vehicle, nameof(YakimaTankVehicle.generation)), Is.EqualTo(vehicleGeneration + 1), "車体の世代が進まない");
            Vector3 offset = _vehicleRoot.transform.position - spawn;
            Assert.That(new Vector2(offset.x, offset.z).magnitude, Is.LessThan(0.5f), "最初の位置に戻らない " + offset.ToString("F2"));
            Assert.That(Read<float>(weapon, nameof(Weapon.yaw)), Is.EqualTo(0f), "砲塔の角度が戻らない");
            Assert.That(Flag(weapon, nameof(Weapon.loaded)), Is.True, "装填済みに戻らない");
            Assert.That(BoardingArea.enabled, Is.True, "復活しても乗車の判定が無効のまま");
        }

        [UnityTest]
        public IEnumerator 復活の前の世代に対する申告は使わず復活の前に撃ち復活の後に当たった弾は使う()
        {
            yield return PlaceWithShooter();
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.respawnDelay = 0f));
            yield return PhysicsSeconds(0.5f);
            DestroyByHits(1);
            float start = Time.realtimeSinceStartup;
            while (RespawnGeneration == 0 && Time.realtimeSinceStartup - start < 3f)
            {
                yield return null;
            }

            Assert.That(RespawnGeneration, Is.EqualTo(1), "復活しない（判定にならない）");

            InjectHit(LifeUdon, LocalId, ShooterIndex, 5, 0);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitOldGeneration), "復活の前の世代に対する申告");
            Assert.That(Health, Is.EqualTo(100));

            // 5 番は復活の前に撃った弾。当たったと判定した時点の世代は今の世代。
            InjectHit(LifeUdon, LocalId, ShooterIndex, 5, 1);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitUse));
            Assert.That(Health, Is.EqualTo(75));
        }

        [UnityTest]
        public IEnumerator オーナーが代わっても受けたHPと処理した命中を引き継ぐ()
        {
            yield return PlaceWithShooter();
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, _vehicleRoot);
            yield return null;
            InjectLife(LifeUdon, Time.realtimeSinceStartup, 50, 0, new[] { Life.NoPlayer, LocalId }, new[] { -1, 2 });
            Assert.That(Health, Is.EqualTo(50), "受けた状態を使わない（判定にならない）");

            Session.SetOwner(Networking.LocalPlayer, _vehicleRoot);
            yield return null;
            Assert.That(Flag(LifeUdon, nameof(Life.isOwnerMode)), Is.True);

            InjectHit(LifeUdon, LocalId, ShooterIndex, 2, 0);
            Assert.That(LastHitResult, Is.EqualTo(Life.HitDuplicate), "前のオーナーが処理した命中を数えた");
            InjectHit(LifeUdon, LocalId, ShooterIndex, 3, 0);
            Assert.That(Health, Is.EqualTo(25), "受けた HP から引いていない");
        }

        [UnityTest]
        public IEnumerator 破壊のまま所有権を受け取ると新しいオーナーが待ち時間を数え直して復活させる()
        {
            yield return PlaceWithShooter();
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.respawnDelay = 0.5f));
            yield return PhysicsSeconds(0.2f);
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            Session.SetOwner(other, _vehicleRoot);
            yield return null;
            InjectLife(LifeUdon, Time.realtimeSinceStartup, 0, 3, new[] { Life.NoPlayer, other.playerId }, new[] { -1, 9 });
            yield return PhysicsSeconds(1f);
            Assert.That(RespawnGeneration, Is.EqualTo(3), "オーナー以外が復活させた");

            Session.SetOwner(Networking.LocalPlayer, _vehicleRoot);
            yield return null;
            Assert.That(Read<float>(LifeUdon, nameof(Life.respawnTimer)), Is.LessThan(0.2f), "待ち時間を数え直していない");
            float start = Time.realtimeSinceStartup;
            while (RespawnGeneration == 3 && Time.realtimeSinceStartup - start < 3f)
            {
                yield return null;
            }

            Assert.That(RespawnGeneration, Is.EqualTo(4), "新しいオーナーが復活させない");
            Assert.That(Health, Is.EqualTo(100));
        }

        [UnityTest]
        public IEnumerator 自分の砲弾がほかの車両に当たると申告が届いて相手のHPが減る()
        {
            yield return Place(withPlayer: true);
            PlaceTarget(20f);
            yield return PhysicsSeconds(1f);
            UdonBehaviour targetLife = YakimaTankRuntimeValues.FindBehaviour(_other, typeof(Life));
            Assert.That(Read<int>(targetLife, nameof(Life.health)), Is.EqualTo(100));

            yield return BoardAndHoldFire(true);
            float start = Time.realtimeSinceStartup;
            while (Read<int>(targetLife, nameof(Life.health)) == 100 && Time.realtimeSinceStartup - start < 2f)
            {
                yield return null;
            }

            HoldFire(false);
            Assert.That(Read<int>(WeaponUdon, nameof(Weapon.sentHits)), Is.EqualTo(1), "申告を送っていない");
            Assert.That(Read<int>(targetLife, nameof(Life.health)), Is.EqualTo(75), "申告が届いて HP が減っていない");
            Assert.That(Read<int>(targetLife, nameof(Life.acceptedHits)), Is.EqualTo(1));
            Assert.That(Health, Is.EqualTo(100), "撃った自分の HP が減った");
        }
    }
}
