using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using Yakimannzyuu.YakimaTank.Editor;
using Yakimannzyuu.YakimaTank.Editor.Tests;
using Object = UnityEngine.Object;
using Sync = Yakimannzyuu.YakimaTank.YakimaTankVehicleSync;

namespace Yakimannzyuu.YakimaTank.Tests.PlayMode
{
    /// <summary>
    /// 車体の同期と所有権（仕様書 0.3.0 の 3 節の PlayMode）。
    /// ClientSim は別のクライアントからの受信を再現できないので、受ける状態は <see cref="YakimaTankVehicleSync._AcceptState"/> に直接入れる。
    /// また、ワールドのオブジェクトの Manual の同期を送らない（OnPreSerialization・OnPostSerialization を呼ばない）ので、
    /// 送る要求が増えたら、テストが VRChat と同じ順に呼ぶ。
    /// </summary>
    public abstract partial class YakimaTankDriveTestBase
    {
        private const string RegistryWorkspaceName = "PlayModeRegistry";
        private const float Interval = 0.1f;

        private static string RegistryPrefabPath => new TestWorkspace(RegistryWorkspaceName).Folder + "/Registry.prefab";

        /// <summary>
        /// シーンの車両の一覧の Prefab を、Play に入る前に作る（Play 中に U# の部品を足すと Udon に値が入らない）。
        /// 一覧の中身は、Play 中に置いた車両で埋める。
        /// </summary>
        public virtual void Setup()
        {
            new TestWorkspace(RegistryWorkspaceName);
            Scene preview = EditorSceneManager.NewPreviewScene();
            GameObject root = YakimaTankSetupService.CreateInScene(preview, "Registry");
            var registry = UdonSharpUndo.AddComponent<YakimaTankVehicleRegistry>(root);
            UdonSharpEditorUtility.CopyProxyToUdon(registry);
            UdonSharpEditorUtility.GetBackingUdonBehaviour(registry).SyncMethod = Networking.SyncType.None;
            PrefabUtility.SaveAsPrefabAsset(root, RegistryPrefabPath);
            YakimaTankSetupService.DeferCleanup(() =>
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(preview);
            });
        }

        public virtual void Cleanup()
        {
            new TestWorkspace(RegistryWorkspaceName).Dispose();
            TestWorkspace.DeleteRootIfEmpty();
        }

        private UdonBehaviour SyncUdon => Part(typeof(Sync));

        private static T Read<T>(UdonBehaviour behaviour, string name)
        {
            return (T)behaviour.GetProgramVariable(name);
        }

        private int _served;

        /// <summary>テストのクラスは全部のテストで 1 つなので、テストごとに数え直す。</summary>
        [SetUp]
        public void ResetServed()
        {
            _served = 0;
        }

        /// <summary>送る要求が増えていたら、VRChat と同じく OnPreSerialization・OnPostSerialization を呼ぶ。</summary>
        private void Serve()
        {
            UdonBehaviour sync = SyncUdon;
            int requested = Read<int>(sync, nameof(Sync.requestedCount));
            if (requested > _served)
            {
                _served = requested;
                sync.OnPreSerialization();
                sync.OnPostSerialization(new SerializationResult(true, 100));
            }
        }

        /// <summary>送る間隔は実時間で数えるので、実時間で待ちながら送る要求に応える。</summary>
        private IEnumerator ServeFor(float seconds)
        {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
                Serve();
            }
        }

        private void Inject(int sender, int epoch, float sendTime, Vector3 position, Quaternion rotation, Vector3 velocity, bool stopped, int driver = Sync.NoPlayer)
        {
            UdonBehaviour sync = SyncUdon;
            sync.SetProgramVariable(nameof(Sync.syncFormat), Sync.SyncFormat);
            sync.SetProgramVariable(nameof(Sync.syncEpoch), epoch);
            sync.SetProgramVariable(nameof(Sync.syncPosition), position);
            sync.SetProgramVariable(nameof(Sync.syncRotation), rotation);
            sync.SetProgramVariable(nameof(Sync.syncVelocity), velocity);
            sync.SetProgramVariable(nameof(Sync.syncAngularVelocity), Vector3.zero);
            sync.SetProgramVariable(nameof(Sync.syncStopped), stopped);
            sync.SetProgramVariable(nameof(Sync.syncDriver), driver);
            sync.SetProgramVariable(nameof(Sync.syncSender), sender);
            sync.SetProgramVariable(nameof(Sync.incomingSendTime), sendTime);
            sync.SendCustomEvent(nameof(Sync._AcceptState));
        }

        private int Epoch => Read<int>(SyncUdon, nameof(Sync.epoch));

        private Collider[] PhysicsShapes => Read<Collider[]>(SyncUdon, nameof(Sync.physicsColliders));

        /// <summary>今の位置から離れた、置かれたかが分かる位置。</summary>
        private Vector3 Away(float distance)
        {
            return _vehicleRoot.transform.position + new Vector3(distance, 0f, distance);
        }

        private float DistanceTo(Vector3 position)
        {
            return Vector3.Distance(_vehicleRoot.transform.position, position);
        }

        private IEnumerator GiveOwnershipTo(VRCPlayerApi player)
        {
            Session.SetOwner(player, _vehicleRoot);
            yield return null;
        }

        [UnityTest]
        public IEnumerator オーナーは走ると送る値がRigidbodyと一致し止まると停止の印を付けて送りその後は要求しない()
        {
            yield return Place(withPlayer: true);
            UdonBehaviour sync = SyncUdon;
            Assert.That(Read<bool>(sync, nameof(Sync.isOwnerMode)), Is.True, "ローカルのプレイヤー（マスター）がオーナー");
            yield return ServeFor(1f);
            int beforeDriving = Read<int>(sync, nameof(Sync.requestedCount));

            SetInput(1f, 0f, 0f);
            yield return ServeFor(1.5f);
            Assert.That(Read<int>(sync, nameof(Sync.requestedCount)) - beforeDriving, Is.GreaterThanOrEqualTo(5), "動いている間は送る間隔ごとに要求する");
            sync.OnPreSerialization();
            Assert.That(Vector3.Distance(Read<Vector3>(sync, nameof(Sync.syncPosition)), Body.position), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(Read<Quaternion>(sync, nameof(Sync.syncRotation)), Body.rotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(Read<Vector3>(sync, nameof(Sync.syncVelocity)), Body.velocity), Is.LessThan(1e-4f));
            Assert.That(Read<int>(sync, nameof(Sync.syncSender)), Is.EqualTo(Networking.LocalPlayer.playerId));
            Assert.That(Read<bool>(sync, nameof(Sync.syncStopped)), Is.False);

            SetInput(0f, 0f, 1f);
            yield return ServeFor(4f);
            Assert.That(Body.velocity.magnitude, Is.LessThan(Sync.MovingSpeed), "止まっていない（判定にならない）");
            Assert.That(Read<bool>(sync, nameof(Sync.syncStopped)), Is.True, "停止の印");
            Assert.That(Vector3.Distance(Read<Vector3>(sync, nameof(Sync.syncPosition)), Body.position), Is.LessThan(0.01f));
            int afterStop = Read<int>(sync, nameof(Sync.requestedCount));
            yield return ServeFor(1.5f);
            Assert.That(Read<int>(sync, nameof(Sync.requestedCount)), Is.EqualTo(afterStop), "止まった後は要求しない");
        }

        [UnityTest]
        public IEnumerator 止まった後に車体を0点05メートル以上ずらすと停止の印を付けてもう1回送りずらさなければ送らない()
        {
            yield return Place(withPlayer: true);
            UdonBehaviour sync = SyncUdon;
            yield return ServeFor(1.5f);
            Assert.That(Read<bool>(sync, nameof(Sync.syncStopped)), Is.True, "止まった状態を送っていない（判定にならない）");
            int stopped = Read<int>(sync, nameof(Sync.requestedCount));

            Body.position += new Vector3(0.02f, 0f, 0f);
            yield return ServeFor(1f);
            Assert.That(Read<int>(sync, nameof(Sync.requestedCount)), Is.EqualTo(stopped), "許容値より小さいずれでは送らない");

            Body.position += new Vector3(0.04f, 0f, 0f);
            yield return ServeFor(1f);
            Assert.That(Read<int>(sync, nameof(Sync.requestedCount)), Is.EqualTo(stopped + 1), "ずれたら 1 回だけ送る");
            Assert.That(Read<bool>(sync, nameof(Sync.syncStopped)), Is.True, "停止の印のまま");
            Assert.That(Vector3.Distance(Read<Vector3>(sync, nameof(Sync.syncPosition)), Body.position), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator 所有権がほかの人へ移るとkinematicで物理の形が無効になり走行の入力でも共通操作値でも動かない()
        {
            yield return Place(withPlayer: true);
            yield return GiveOwnershipTo(Session.SpawnRemotePlayer("B"));

            Assert.That(Read<bool>(SyncUdon, nameof(Sync.isOwnerMode)), Is.False);
            Assert.That(Body.isKinematic, Is.True);
            Assert.That(PhysicsShapes.All(c => !c.enabled), Is.True, "車体のコライダーと WheelCollider が無効");
            Assert.That(BoardingArea.enabled, Is.True, "乗車の判定（トリガー）は有効のまま");
            Assert.That(_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.hasAuthority)), Is.False);

            Vector3 start = _vehicleRoot.transform.position;
            SetInput(1f, 1f, 0f);
            _vehicle.SetProgramVariable(nameof(YakimaTankVehicle.localDriverSeated), true);
            SetControl(1f, YakimaTankControlState.GearForward, 1f, 0f);
            yield return PhysicsSeconds(1f);
            Assert.That(Vector3.Distance(_vehicleRoot.transform.position, start), Is.LessThan(1e-3f));
        }

        [UnityTest]
        public IEnumerator オーナー以外では受けた状態を補間した位置に置き世代が変わると補間せずに置く()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi owner = Session.SpawnRemotePlayer("B");
            yield return GiveOwnershipTo(owner);
            int epoch = Epoch;
            Vector3 origin = Away(5f);
            var velocity = new Vector3(0f, 0f, 1f);

            // 最初の状態はすぐに置く。その後は、表示する時刻の前後の状態から補間する。
            float now = Time.realtimeSinceStartup;
            Inject(owner.playerId, epoch, now - 0.6f, origin, Quaternion.identity, velocity, false);
            yield return null;
            Assert.That(DistanceTo(origin), Is.LessThan(1e-3f), "最初の状態にすぐ置く");

            var times = new float[Sync.HistorySize];
            var positions = new Vector3[Sync.HistorySize];
            var rotations = new Quaternion[Sync.HistorySize];
            var velocities = new Vector3[Sync.HistorySize];
            var angulars = new Vector3[Sync.HistorySize];
            var stops = new bool[Sync.HistorySize];
            int count = Sync.PushHistory(times, positions, rotations, velocities, angulars, stops, 0, now - 0.6f, origin, Quaternion.identity, velocity, Vector3.zero, false);
            for (int index = 1; index <= 8; index++)
            {
                float time = now - 0.6f + index * Interval;
                Vector3 position = origin + velocity * (index * Interval);
                Inject(owner.playerId, epoch, time, position, Quaternion.identity, velocity, false);
                count = Sync.PushHistory(times, positions, rotations, velocities, angulars, stops, count, time, position, Quaternion.identity, velocity, Vector3.zero, false);
            }

            yield return null;
            float display = Sync.DisplayTime(Networking.SimulationTime(_vehicleRoot), Time.realtimeSinceStartup, Interval);
            Vector3 expected = Sync.SamplePosition(times, positions, velocities, stops, count, display, 0.25f);
            Assert.That(DistanceTo(expected), Is.LessThan(0.02f), "表示する時刻の補間の位置");

            Vector3 jumped = Away(30f);
            Inject(owner.playerId, epoch + 1, now + 1f, jumped, Quaternion.identity, Vector3.zero, false);
            yield return null;
            Assert.That(DistanceTo(jumped), Is.LessThan(1e-3f), "世代が変わったら補間せずに置く");
        }

        [UnityTest]
        public IEnumerator 停止の状態が所有権の移動より先に届くとその場では置かずその人へ移すと置き今のオーナーの古い状態を間に入れても同じ()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi current = Session.SpawnRemotePlayer("B");
            VRCPlayerApi next = Session.SpawnRemotePlayer("C");
            yield return GiveOwnershipTo(current);
            float now = Time.realtimeSinceStartup;
            Vector3 held = Away(10f);

            Inject(next.playerId, Epoch + 1, now, held, Quaternion.identity, Vector3.zero, true);
            Vector3 older = Away(-10f);
            Inject(current.playerId, Epoch, now - 0.5f, older, Quaternion.identity, Vector3.zero, true);
            yield return null;
            Assert.That(DistanceTo(held), Is.GreaterThan(1f), "まだオーナーでない人の状態は置かない");
            Assert.That(DistanceTo(older), Is.LessThan(1e-3f), "今のオーナーの状態は使う（判定にならない）");

            yield return GiveOwnershipTo(next);
            yield return null;
            Assert.That(DistanceTo(held), Is.LessThan(1e-3f), "所有権が来たら保留の状態に置く");
        }

        [UnityTest]
        public IEnumerator 続けて所有権が移るとBへの通知ではCの状態を使わずCへの通知で使い巻き戻らない()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi a = Session.SpawnRemotePlayer("A");
            VRCPlayerApi b = Session.SpawnRemotePlayer("B");
            VRCPlayerApi c = Session.SpawnRemotePlayer("C");
            yield return GiveOwnershipTo(a);
            float now = Time.realtimeSinceStartup;
            Vector3 fromC = Away(10f);
            Vector3 start = _vehicleRoot.transform.position;

            // A → B → C と移り、C の停止の状態・B への通知・C への通知の順に届く。
            Inject(c.playerId, Epoch + 2, now - 1f, fromC, Quaternion.identity, Vector3.zero, true);
            yield return GiveOwnershipTo(b);
            yield return null;
            Assert.That(DistanceTo(fromC), Is.GreaterThan(1f), "B への通知では C の状態を使わない");

            // C の状態より古い A の状態を、C への通知の前に保留しておく。
            Inject(a.playerId, Epoch + 1, now - 1.1f, start, Quaternion.identity, Vector3.zero, true);
            yield return GiveOwnershipTo(c);
            yield return null;
            Assert.That(DistanceTo(fromC), Is.LessThan(1e-3f), "C への通知で C の状態を使う");

            // 今のオーナーでない人（B）の新しい状態では動かない。A に所有権が移っても、使った状態より古い A の保留で巻き戻らない。
            Inject(b.playerId, Epoch + 3, now + 1f, start, Quaternion.identity, Vector3.zero, true);
            yield return null;
            Assert.That(DistanceTo(fromC), Is.LessThan(1e-3f), "オーナーでない人の状態で動いた");
            yield return GiveOwnershipTo(a);
            yield return null;
            Assert.That(DistanceTo(fromC), Is.LessThan(1e-3f), "使った状態より古い保留で巻き戻った");
        }

        [UnityTest]
        public IEnumerator 所有権を取り戻すと最新の状態から物理が始まり世代が進む()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            yield return GiveOwnershipTo(other);
            int epoch = Epoch;
            Vector3 latest = Away(6f);
            Inject(other.playerId, epoch, Time.realtimeSinceStartup, latest, Quaternion.Euler(0f, 30f, 0f), Vector3.zero, true);
            yield return null;

            yield return GiveOwnershipTo(Networking.LocalPlayer);

            Assert.That(Read<bool>(SyncUdon, nameof(Sync.isOwnerMode)), Is.True);
            Assert.That(Body.isKinematic, Is.False);
            Assert.That(PhysicsShapes.All(c => c.enabled), Is.True);
            Assert.That(Epoch, Is.EqualTo(epoch + 1), "世代が進む");
            Assert.That(Vector3.Distance(Body.position, latest), Is.LessThan(0.05f), "最新の状態の位置から物理が始まる");
            Assert.That(Quaternion.Angle(Body.rotation, Quaternion.Euler(0f, 30f, 0f)), Is.LessThan(1f));
        }

        [UnityTest]
        public IEnumerator 乗ると所有権を取って入力が有効になり運転手が同期変数に入り座ったまま所有権が移ると降ろされる()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi other = Session.SpawnRemotePlayer("B");
            yield return GiveOwnershipTo(other);
            Assert.That(Read<bool>(SyncUdon, nameof(Sync.isOwnerMode)), Is.False, "乗る前はオーナーでない（判定にならない）");

            yield return Board();
            yield return null;
            UdonBehaviour seat = Part(typeof(YakimaTankSeat));
            Assert.That(Read<bool>(SyncUdon, nameof(Sync.isOwnerMode)), Is.True, "座ったら所有権を取る");
            Assert.That(Flag(seat, nameof(YakimaTankSeat.waitingOwnership)), Is.False);
            Assert.That(Flag(Part(typeof(YakimaTankDesktopInput)), nameof(YakimaTankDesktopInput.isActive)), Is.True, "所有権が来たら入力が有効");
            int local = Networking.LocalPlayer.playerId;
            Serve();
            Assert.That(Read<int>(SyncUdon, nameof(Sync.syncDriver)), Is.EqualTo(local), "運転手が同期変数に入る");

            yield return GiveOwnershipTo(other);
            yield return null;
            Assert.That(Flag(seat, nameof(YakimaTankSeat.localSeated)), Is.False, "座ったまま所有権が移ったら降ろす");
            Assert.That(Flag(Part(typeof(YakimaTankDesktopInput)), nameof(YakimaTankDesktopInput.isActive)), Is.False);
        }

        [UnityTest]
        public IEnumerator 自分がオーナーになると保留をすべて捨てその後に所有権が移っても捨てた保留を使わない()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi current = Session.SpawnRemotePlayer("B");
            VRCPlayerApi next = Session.SpawnRemotePlayer("C");
            yield return GiveOwnershipTo(current);
            Vector3 held = Away(10f);
            Inject(next.playerId, Epoch + 1, Time.realtimeSinceStartup, held, Quaternion.identity, Vector3.zero, true);
            int rejected = Read<int>(SyncUdon, nameof(Sync.rejectedCount));

            yield return GiveOwnershipTo(Networking.LocalPlayer);
            Assert.That(Read<int>(SyncUdon, nameof(Sync.rejectedCount)), Is.EqualTo(rejected + 1), "捨てた保留を数える");

            yield return GiveOwnershipTo(next);
            yield return null;
            Assert.That(DistanceTo(held), Is.GreaterThan(1f), "自分がオーナーのときに捨てた保留で置いた");
        }

        [UnityTest]
        public IEnumerator 所有権を待つ間に乗り直すと前の乗車の確かめでは降ろさず今の待ちで所有権が来なければ降ろす()
        {
            yield return Place(withPlayer: true);
            yield return Board();
            UdonBehaviour seat = Part(typeof(YakimaTankSeat));

            // ClientSim では所有権がすぐに来て待ちにならないので、乗り直して待ちの確かめが 2 つ残った状態を作る。
            int checks = Read<int>(seat, nameof(YakimaTankSeat.ownershipChecks));
            seat.SetProgramVariable(nameof(YakimaTankSeat.waitingOwnership), true);
            seat.SetProgramVariable(nameof(YakimaTankSeat.ownershipWaits), checks + 2);

            seat.SendCustomEvent(nameof(YakimaTankSeat._CheckOwnershipTimeout));
            yield return null;
            Assert.That(Flag(seat, nameof(YakimaTankSeat.localSeated)), Is.True, "前の乗車の待ちの確かめで降ろした");

            seat.SendCustomEvent(nameof(YakimaTankSeat._CheckOwnershipTimeout));
            yield return null;
            Assert.That(Flag(seat, nameof(YakimaTankSeat.localSeated)), Is.False, "今の待ちで所有権が来ないのに降ろさない");
            Assert.That(Flag(Part(typeof(YakimaTankDesktopInput)), nameof(YakimaTankDesktopInput.isActive)), Is.False);
        }

        [UnityTest]
        public IEnumerator 座席の出入りの通知がなくても同期した運転手がいる間は乗車の判定が無効で運転手がいなくなると戻る()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi driver = Session.SpawnRemotePlayer("B");
            yield return GiveOwnershipTo(driver);
            Assert.That(BoardingArea.enabled, Is.True, "運転手がいないのに判定が無効（判定にならない）");
            Vector3 position = _vehicleRoot.transform.position;
            Quaternion rotation = _vehicleRoot.transform.rotation;
            float now = Time.realtimeSinceStartup;

            Inject(driver.playerId, Epoch, now, position, rotation, Vector3.zero, true, driver.playerId);
            yield return null;
            Assert.That(BoardingArea.enabled, Is.False, "途中で入った人の画面で、同期した運転手で無効にならない");

            Inject(driver.playerId, Epoch, now + Interval, position, rotation, Vector3.zero, true);
            yield return null;
            Assert.That(BoardingArea.enabled, Is.True, "運転手がいなくなっても戻らない");
        }

        [UnityTest]
        public IEnumerator 再適用で送る間隔と先読みの上限が同期のUdonに入る()
        {
            yield return Place();

            YakimaTankPlayTuning.Reapply(_vehicle, Working(w =>
            {
                w.sendInterval = 0.2f;
                w.maxExtrapolation = 0.4f;
            }));

            Assert.That(SyncUdon.GetProgramVariable(nameof(Sync.sendInterval)), Is.EqualTo(0.2f));
            Assert.That(SyncUdon.GetProgramVariable(nameof(Sync.maxExtrapolation)), Is.EqualTo(0.4f));
        }

        private GameObject SpawnRegistry(params GameObject[] vehicles)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RegistryPrefabPath);
            Assert.That(prefab, Is.Not.Null, "シーンの車両の一覧の Prefab がない");
            GameObject registry = YakimaTankPlayTuning.Spawn(prefab, Vector3.zero, Quaternion.identity);
            UdonBehaviour udon = registry.GetComponent<UdonBehaviour>();
            Type type = udon.GetProgramVariableType(nameof(YakimaTankVehicleRegistry.vehicles));
            Array list = Array.CreateInstance(type.GetElementType(), vehicles.Length);
            for (int index = 0; index < vehicles.Length; index++)
            {
                list.SetValue(YakimaTankRuntimeValues.FindBehaviour(vehicles[index], typeof(Sync)), index);
            }

            udon.SetProgramVariable(nameof(YakimaTankVehicleRegistry.vehicles), list);
            return registry;
        }

        [UnityTest]
        public IEnumerator 同じオーナーの2台は秒速2メートルで当ててもすり抜け相手は動かず当てた側の高さは変わらない([Values(false, true)] bool side)
        {
            yield return Place();
            BoxCollider box = _vehicleRoot.transform.Find("Colliders/Body").GetComponent<BoxCollider>();
            float length = box.size.z;
            float width = box.size.x;
            Vector3 otherPosition = side
                ? new Vector3(0f, SpawnHeight, length * 0.5f + width * 0.5f + 1f)
                : new Vector3(0f, SpawnHeight, length + 1f);
            GameObject other = YakimaTankPlayTuning.Spawn(_prefab, otherPosition, side ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
            SpawnRegistry(_vehicleRoot, other);
            yield return PhysicsSeconds(2f);

            Vector3 otherStart = other.transform.position;
            float startHeight = _vehicleRoot.transform.position.y;
            float pushed = 0f;
            float rise = 0f;
            double start = Time.fixedTimeAsDouble;
            while (!Elapsed(start, 10f))
            {
                yield return new WaitForFixedUpdate();
                SetInput(ForwardSpeed < 2f ? 0.6f : 0f, 0f, 0f);
                pushed = Mathf.Max(pushed, Vector3.Distance(other.transform.position, otherStart));
                rise = Mathf.Max(rise, Mathf.Abs(_vehicleRoot.transform.position.y - startHeight));
            }

            Assert.That(_vehicleRoot.transform.position.z, Is.GreaterThan(otherPosition.z + (side ? width : length) * 0.5f), "相手の向こうまで進んでいない（すり抜けずに止められた）");
            Assert.That(pushed, Is.LessThan(0.05f), "相手が動いた");
            Assert.That(rise, Is.LessThan(0.1f), "当てた側の車体の高さが変わった（乗り上げ）");
        }
    }
}
