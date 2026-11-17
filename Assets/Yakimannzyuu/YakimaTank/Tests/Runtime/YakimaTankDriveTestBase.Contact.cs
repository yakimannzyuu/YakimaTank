using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;
using VRC.Udon;
using Yakimannzyuu.YakimaTank.Editor;
using Sync = Yakimannzyuu.YakimaTank.YakimaTankVehicleSync;

namespace Yakimannzyuu.YakimaTank.Tests.PlayMode
{
    /// <summary>
    /// 車両同士の簡易衝突（仕様書 0.4.0 の 3 節の PlayMode）。
    /// ほかの人が持つ車両は、ClientSim でリモートのプレイヤーへ所有権を移して作り、受ける状態は <see cref="YakimaTankVehicleSync._AcceptState"/> に直接入れる。
    /// </summary>
    public abstract partial class YakimaTankDriveTestBase
    {
        /// <summary>2 台目。後処理で止める（ClientSim の送る要求の処理が、消えた車両に触れないように）。</summary>
        private GameObject _other;

        private UdonBehaviour _otherSync;
        private VRCPlayerApi _otherOwner;

        /// <summary>2 台目を受けた状態で置いた位置（高さは床に落ち着いた後の値）。</summary>
        private Vector3 _otherPosition;

        private static BoxCollider BodyBox(GameObject root)
        {
            return root.transform.Find(YakimaTankSetupService.CollidersName + "/" + YakimaTankSetupService.BodyColliderName).GetComponent<BoxCollider>();
        }

        /// <summary>箱の世界の範囲。無効なコライダーでも求められるように、Collider.bounds（無効なら空）を使わない。</summary>
        private static Bounds WorldBox(BoxCollider box)
        {
            Transform transform = box.transform;
            Vector3 half = box.size * 0.5f;
            var bounds = new Bounds(transform.TransformPoint(box.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -half.x : half.x, (corner & 2) == 0 ? -half.y : half.y, (corner & 4) == 0 ? -half.z : half.z);
                bounds.Encapsulate(transform.TransformPoint(box.center + offset));
            }

            return bounds;
        }

        /// <summary>前後（Z）の重なり。離れていれば負。</summary>
        private static float OverlapZ(GameObject a, GameObject b)
        {
            Bounds first = WorldBox(BodyBox(a));
            Bounds second = WorldBox(BodyBox(b));
            return Mathf.Min(first.max.z, second.max.z) - Mathf.Max(first.min.z, second.min.z);
        }

        /// <summary>自分の車体の前端から、前へ gap だけ離して相手の車体の後端を置く位置（相手は向きをそろえる）。</summary>
        private Vector3 InFront(float gap)
        {
            Bounds box = WorldBox(BodyBox(_vehicleRoot));
            float length = box.size.z;
            return new Vector3(_vehicleRoot.transform.position.x, _vehicleRoot.transform.position.y, box.max.z + gap + length * 0.5f - (box.center.z - _vehicleRoot.transform.position.z));
        }

        /// <summary>
        /// 2 台目を、ほかの人（B）が持つ車両にして position に置く。物理の形は無効で、受けた状態（age 秒前に送った、速度 0）だけで置かれる。
        /// 離れた所で作ってから所有権を移す。重なる位置で作ると、所有権を移すまでの間に同じオーナーどうしとして押し合ってしまう。
        /// </summary>
        private IEnumerator PlaceRemoteOther(Vector3 position, bool stopped = true, float age = 0f)
        {
            _other = YakimaTankPlayTuning.Spawn(_prefab, position + new Vector3(30f, 0f, 0f), Quaternion.identity);
            SpawnRegistry(_vehicleRoot, _other);
            yield return PhysicsSeconds(1f);
            _otherOwner = Session.SpawnRemotePlayer("B");
            Session.SetOwner(_otherOwner, _other);
            yield return null;
            _otherSync = YakimaTankRuntimeValues.FindBehaviour(_other, typeof(Sync));
            Assert.That(Read<bool>(_otherSync, nameof(Sync.isOwnerMode)), Is.False, "2 台目がオーナー以外にならない（判定にならない）");
            Assert.That(BodyBox(_other).enabled, Is.False, "2 台目の車体のコライダーが有効（判定にならない）");

            _otherPosition = new Vector3(position.x, _other.transform.position.y, position.z);
            InjectOther(_otherPosition, Vector3.zero, stopped, Time.realtimeSinceStartup - age);
            yield return null;
            Assert.That(Vector3.Distance(_other.transform.position, _otherPosition), Is.LessThan(1e-3f), "2 台目が受けた状態の位置に置かれない");
        }

        private void InjectOther(Vector3 position, Vector3 velocity, bool stopped, float sendTime)
        {
            InjectInto(_otherSync, _otherOwner.playerId, Read<int>(_otherSync, nameof(Sync.epoch)), sendTime, position, Quaternion.identity, velocity, stopped);
        }

        private int ContactCount => Read<int>(SyncUdon, nameof(Sync.contactCount));

        private int CutoffCount => Read<int>(SyncUdon, nameof(Sync.contactCutoffCount));

        [UnityTest]
        public IEnumerator 同じオーナーの2台は秒速2メートルで当てると相手の手前で止まりめり込みが増え続けず乗り上げない([Values(false, true)] bool side)
        {
            yield return Place();
            BoxCollider box = BodyBox(_vehicleRoot);
            float length = box.size.z;
            float width = box.size.x;
            // 秒速 2 メートルに届くよう、3 m 離して置く。
            Vector3 otherPosition = side
                ? new Vector3(0f, SpawnHeight, length * 0.5f + width * 0.5f + 3f)
                : new Vector3(0f, SpawnHeight, length + 3f);
            _other = YakimaTankPlayTuning.Spawn(_prefab, otherPosition, side ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
            SpawnRegistry(_vehicleRoot, _other);
            yield return PhysicsSeconds(2f);

            Vector3 otherStart = _other.transform.position;
            float startHeight = _vehicleRoot.transform.position.y;
            float pushed = 0f;
            float rise = 0f;
            float deepest = float.NegativeInfinity;
            float hitSpeed = 0f;
            float overlapAt8 = float.NaN;
            double start = Time.fixedTimeAsDouble;
            while (!Elapsed(start, 10f))
            {
                yield return new WaitForFixedUpdate();
                SetInput(ForwardSpeed < 2f ? 0.6f : 0f, 0f, 0f);
                float overlap = OverlapZ(_vehicleRoot, _other);
                if (overlap > 0f && hitSpeed == 0f)
                {
                    hitSpeed = ForwardSpeed;
                }

                deepest = Mathf.Max(deepest, overlap);
                pushed = Mathf.Max(pushed, Vector3.Distance(_other.transform.position, otherStart));
                rise = Mathf.Max(rise, Mathf.Abs(_vehicleRoot.transform.position.y - startHeight));
                if (float.IsNaN(overlapAt8) && Elapsed(start, 8f))
                {
                    overlapAt8 = overlap;
                }
            }

            float final = OverlapZ(_vehicleRoot, _other);
            Assert.That(hitSpeed, Is.GreaterThan(1.5f), "秒速 2 メートル近くで当たっていない（判定にならない）");
            Assert.That(_vehicleRoot.transform.position.z, Is.LessThan(otherPosition.z), "相手の手前で止まらず、すり抜けた");
            Assert.That(deepest, Is.LessThan(0.5f), "めり込みが深い");
            Assert.That(final, Is.LessThanOrEqualTo(overlapAt8 + 0.02f), $"最後の 2 秒でめり込みが増えた（8 秒 {overlapAt8:F3}・10 秒 {final:F3}）");
            Assert.That(pushed, Is.LessThan(0.5f), "相手が動きすぎた");
            Assert.That(rise, Is.LessThan(0.1f), "当てた側の車体の高さが変わった（乗り上げ）");
        }

        [UnityTest]
        public IEnumerator 受けた停止の状態の相手に秒速2メートルで当たると手前で止まり接触の数に入る()
        {
            yield return Place(withPlayer: true);
            yield return PlaceRemoteOther(InFront(3f));

            float hitSpeed = 0f;
            float deepest = float.NegativeInfinity;
            double start = Time.fixedTimeAsDouble;
            while (!Elapsed(start, 8f))
            {
                yield return new WaitForFixedUpdate();
                SetInput(ForwardSpeed < 2f ? 0.6f : 0f, 0f, 0f);
                float overlap = OverlapZ(_vehicleRoot, _other);
                if (overlap > 0f && hitSpeed == 0f)
                {
                    hitSpeed = ForwardSpeed;
                }

                deepest = Mathf.Max(deepest, overlap);
            }

            Assert.That(hitSpeed, Is.GreaterThan(1.5f), "秒速 2 メートル近くで当たっていない（判定にならない）");
            Assert.That(_vehicleRoot.transform.position.z, Is.LessThan(_otherPosition.z), "相手の手前で止まらず、すり抜けた");
            Assert.That(deepest, Is.LessThan(0.5f), "めり込みが深い");
            Assert.That(Body.velocity.magnitude, Is.LessThan(0.2f), "止まっていない");
            Assert.That(ContactCount, Is.EqualTo(1), "押している間、接触の数に入る");
            Assert.That(CutoffCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator 停止の印がなく1秒より古い状態の相手には当たらずに通り抜け打ち切った数に入る()
        {
            yield return Place(withPlayer: true);
            yield return PlaceRemoteOther(InFront(3f), false, Sync.ContactMaxAge + 1f);
            Vector3 otherPosition = _otherPosition;

            int cutoff = 0;
            double start = Time.fixedTimeAsDouble;
            while (!Elapsed(start, 10f) && _vehicleRoot.transform.position.z < otherPosition.z + 1f)
            {
                yield return new WaitForFixedUpdate();
                SetInput(ForwardSpeed < 2f ? 0.6f : 0f, 0f, 0f);
                cutoff = Mathf.Max(cutoff, CutoffCount);
                Assert.That(ContactCount, Is.EqualTo(0), "古い相手に当たった");
            }

            Assert.That(_vehicleRoot.transform.position.z, Is.GreaterThan(otherPosition.z + 1f), "古い相手を通り抜けない");
            Assert.That(cutoff, Is.EqualTo(1), "打ち切った数に入らない");
        }

        [UnityTest]
        public IEnumerator 無人の車両は受けた相手が秒速0点5メートルで寄ってくると押されて動きブレーキを踏んでいると動かない([Values(false, true)] bool braking)
        {
            yield return Place(withPlayer: true);
            var approach = new Vector3(0f, 0f, -0.5f);
            yield return PlaceRemoteOther(InFront(0.2f), false);
            Vector3 otherStart = _otherPosition;
            Vector3 start = _vehicleRoot.transform.position;
            float begin = Time.realtimeSinceStartup;
            float nextSend = begin;
            double physicsStart = Time.fixedTimeAsDouble;
            while (!Elapsed(physicsStart, 3f))
            {
                yield return new WaitForFixedUpdate();
                SetInput(0f, 0f, braking ? 1f : 0f);
                float now = Time.realtimeSinceStartup;
                if (now >= nextSend)
                {
                    InjectOther(otherStart + approach * (now - begin), approach, false, now);
                    nextSend = now + Interval;
                }
            }

            Assert.That(OverlapZ(_vehicleRoot, _other), Is.GreaterThan(0f), "相手が届いていない（判定にならない）");
            float moved = start.z - _vehicleRoot.transform.position.z;
            if (braking)
            {
                Assert.That(Mathf.Abs(moved), Is.LessThan(0.1f), "ブレーキを踏んでいるのに押された");
            }
            else
            {
                Assert.That(moved, Is.GreaterThan(0.5f), "無人の車両が押されない");
            }
        }

        [UnityTest]
        public IEnumerator 受けた相手に押されても後ろの壁を越えない()
        {
            yield return Place(withPlayer: true);
            Bounds own = WorldBox(BodyBox(_vehicleRoot));
            float wallFace = own.min.z - 0.3f;
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Wall";
            wall.transform.position = new Vector3(own.center.x, 2f, wallFace - 0.5f);
            wall.transform.localScale = new Vector3(20f, 4f, 1f);

            yield return PlaceRemoteOther(InFront(-0.4f));
            float startZ = _vehicleRoot.transform.position.z;

            float lowest = float.PositiveInfinity;
            double start = Time.fixedTimeAsDouble;
            while (!Elapsed(start, 3f))
            {
                yield return new WaitForFixedUpdate();
                lowest = Mathf.Min(lowest, WorldBox(BodyBox(_vehicleRoot)).min.z);
            }

            Assert.That(startZ - _vehicleRoot.transform.position.z, Is.GreaterThan(0.2f), "押されて壁まで下がっていない（判定にならない）");
            Assert.That(lowest, Is.GreaterThan(wallFace - 0.05f), "壁を越えた");
        }

        /// <summary>
        /// 押し返しは両方の車両に同じ深さから掛かるので、戦車どうしで押し続けられるのは、押す側の駆動の加速が押される側の停止中のブレーキの減速より大きいときだけ
        /// （仕様書 0.4.0 の 1.5）。既定値（駆動 12000・停止中のブレーキ 20000）では押せない。
        /// </summary>
        [UnityTest]
        public IEnumerator 同じオーナーの2台は停止中のブレーキが駆動より弱ければ全開で止まった車両を押せ強ければ押せない([Values(false, true)] bool weakBrake)
        {
            yield return Place();
            float idleBrake = weakBrake ? _settings.driveTorque * 0.5f : _settings.idleBrakeTorque;
            Assert.That(weakBrake || _settings.idleBrakeTorque > _settings.driveTorque, Is.True, "既定値で停止中のブレーキが駆動より強くない（判定にならない）");
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.idleBrakeTorque = idleBrake));
            BoxCollider box = BodyBox(_vehicleRoot);
            _other = YakimaTankPlayTuning.Spawn(_prefab, new Vector3(0f, SpawnHeight, box.size.z + 0.5f), Quaternion.identity);
            SpawnRegistry(_vehicleRoot, _other);
            YakimaTankPlayTuning.Reapply(YakimaTankRuntimeValues.FindVehicle(_other), Working(w => w.idleBrakeTorque = idleBrake));
            yield return PhysicsSeconds(2f);

            Vector3 otherStart = _other.transform.position;
            float deepest = float.NegativeInfinity;
            double begin = Time.fixedTimeAsDouble;
            while (!Elapsed(begin, 6f))
            {
                yield return new WaitForFixedUpdate();
                SetInput(1f, 0f, 0f);
                deepest = Mathf.Max(deepest, OverlapZ(_vehicleRoot, _other));
            }

            float pushed = _other.transform.position.z - otherStart.z;
            Assert.That(deepest, Is.GreaterThan(0f), "当たっていない（判定にならない）");
            Assert.That(deepest, Is.LessThan(0.5f), "めり込みが深い");
            if (weakBrake)
            {
                Assert.That(pushed, Is.GreaterThan(1f), "停止中のブレーキが駆動より弱いのに押せない");
            }
            else
            {
                Assert.That(Mathf.Abs(pushed), Is.LessThan(0.05f), "停止中のブレーキが駆動より強いのに押された");
            }
        }

        [UnityTest]
        public IEnumerator 再適用で接触の値が同期のUdonに入る()
        {
            yield return Place();

            YakimaTankPlayTuning.Reapply(_vehicle, Working(w =>
            {
                w.contactStopRate = 7f;
                w.contactPushStrength = 9f;
                w.contactMaxPushAcceleration = 3f;
            }));

            Assert.That(SyncUdon.GetProgramVariable(nameof(Sync.contactStopRate)), Is.EqualTo(7f));
            Assert.That(SyncUdon.GetProgramVariable(nameof(Sync.contactPushStrength)), Is.EqualTo(9f));
            Assert.That(SyncUdon.GetProgramVariable(nameof(Sync.contactMaxPushAcceleration)), Is.EqualTo(3f));
        }
    }
}
