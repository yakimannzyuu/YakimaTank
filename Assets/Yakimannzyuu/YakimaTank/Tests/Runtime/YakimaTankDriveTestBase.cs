using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Tsvrc.UdonTestKit;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.Udon;
using Yakimannzyuu.YakimaTank.Editor;

namespace Yakimannzyuu.YakimaTank.Tests.PlayMode
{
    /// <summary>
    /// 生成した車両を ClientSim の中で Udon VM として動かし、走行の判定（仕様書 3 節の表）を確かめる。
    /// どの生成物で確かめるかは派生のクラスが決める（製品のテストは立方体のモデル、開発環境は実際のモデル）。
    /// 生成は Play に入る前に済ませておく。Play 中に U# の Prefab を作ると Udon に値が入らないため。
    /// </summary>
    public abstract class YakimaTankDriveTestBase : ClientSimTestBase
    {
        private const int StepsPerSecond = 50;

        private GameObject _prefab;
        private YakimaTankSetupSettings _settings;
        private GameObject _vehicleRoot;
        private UdonBehaviour _vehicle;

        protected YakimaTankDriveTestBase(string sceneName) : base(TestScene.Empty(sceneName))
        {
        }

        /// <summary>生成に使った設定。出力先の Prefab が生成済みであること。</summary>
        protected abstract YakimaTankSetupSettings LoadSettings();

        /// <summary>車両を置く高さ（床の上面は 0）。転輪が床にめり込まず、落ちる距離が短い高さ。</summary>
        protected virtual float SpawnHeight => 0.1f;

        private IEnumerator Place()
        {
            _settings = LoadSettings();
            Assert.That(_settings, Is.Not.Null, "設定が読めない");
            _prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_settings.outputPrefabPath);
            Assert.That(_prefab, Is.Not.Null, "生成した車両がない: " + _settings.outputPrefabPath);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(400f, 1f, 400f);

            _vehicleRoot = YakimaTankPlayTuning.Spawn(_prefab, new Vector3(0f, SpawnHeight, 0f), Quaternion.identity);
            _vehicle = YakimaTankRuntimeValues.FindVehicle(_vehicleRoot);
            UdonBehaviour[] vehicles = Vehicles();
            Assert.That(vehicles, Has.Length.EqualTo(1), "車両が 1 台ではない（判定にならない）: " +
                string.Join(", ", vehicles.Select(v => $"{v.gameObject.scene.name}:{v.transform.root.name}:{v.SyncMethod}")));
            yield return WaitReady(_vehicle);
            yield return Steps(StepsPerSecond);
        }

        private static UdonBehaviour[] Vehicles()
        {
            return Object.FindObjectsOfType<UdonBehaviour>()
                .Where(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankVehicle))
                .ToArray();
        }

        private static IEnumerator WaitReady(UdonBehaviour vehicle)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!(vehicle.GetProgramVariable(nameof(YakimaTankVehicle.isReady)) is bool ready && ready) && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(vehicle.GetProgramVariable(nameof(YakimaTankVehicle.isReady)), Is.True, "車両が動き出せる状態にならない");
        }

        private static IEnumerator Steps(int count)
        {
            for (int index = 0; index < count; index++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private void SetInput(float throttle, float steer, float brake)
        {
            _vehicle.SetProgramVariable(nameof(YakimaTankVehicle.throttleInput), throttle);
            _vehicle.SetProgramVariable(nameof(YakimaTankVehicle.steerInput), steer);
            _vehicle.SetProgramVariable(nameof(YakimaTankVehicle.brakeInput), brake);
        }

        private Rigidbody Body => _vehicleRoot.GetComponent<Rigidbody>();

        private float ForwardSpeed => Vector3.Dot(Body.velocity, _vehicleRoot.transform.forward);

        private YakimaTankSetupSettings Working(System.Action<YakimaTankSetupSettings> change)
        {
            YakimaTankSetupSettings working = Object.Instantiate(_settings);
            change(working);
            return working;
        }

        [UnityTest]
        public IEnumerator 置いて1秒で全部の転輪が接地し車体は傾かない()
        {
            yield return Place();
            WheelCollider[] wheels = _vehicleRoot.GetComponentsInChildren<WheelCollider>();
            Assert.That(wheels.All(w => w.isGrounded), Is.True, "接地していない転輪がある");
            Assert.That(Vector3.Angle(_vehicleRoot.transform.up, Vector3.up), Is.LessThan(5f));
        }

        [UnityTest]
        public IEnumerator 前進の入力で2秒に0点5メートル以上まっすぐ進む()
        {
            yield return Place();
            Vector3 start = _vehicleRoot.transform.position;
            Vector3 forward = _vehicleRoot.transform.forward;
            SetInput(1f, 0f, 0f);
            yield return Steps(2 * StepsPerSecond);

            Vector3 moved = _vehicleRoot.transform.position - start;
            float along = Vector3.Dot(moved, forward);
            float side = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(moved, forward), Vector3.up).magnitude;
            Assert.That(along, Is.GreaterThanOrEqualTo(0.5f));
            Assert.That(side, Is.LessThan(along * 0.2f));
        }

        [UnityTest]
        public IEnumerator 後退の入力で2秒に0点5メートル以上後ろへ進む()
        {
            yield return Place();
            Vector3 start = _vehicleRoot.transform.position;
            Vector3 forward = _vehicleRoot.transform.forward;
            SetInput(-1f, 0f, 0f);
            yield return Steps(2 * StepsPerSecond);

            Assert.That(Vector3.Dot(_vehicleRoot.transform.position - start, forward), Is.LessThanOrEqualTo(-0.5f));
        }

        [UnityTest]
        public IEnumerator 前進の速度は最高速度の110パーセントを越えない()
        {
            yield return Place();
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.maxForwardSpeed = 3f));
            yield return Steps(StepsPerSecond / 2);
            SetInput(1f, 0f, 0f);
            float fastest = 0f;
            for (int index = 0; index < 6 * StepsPerSecond; index++)
            {
                yield return new WaitForFixedUpdate();
                fastest = Mathf.Max(fastest, ForwardSpeed);
            }

            Assert.That(fastest, Is.GreaterThan(2f), "最高速度の近くまで加速していない（判定にならない）");
            Assert.That(fastest, Is.LessThanOrEqualTo(3f * 1.1f));
        }

        [UnityTest]
        public IEnumerator 旋回の入力だけで2秒に右へ20度以上回りその場から1メートル以上動かない()
        {
            yield return Place();
            Vector3 start = _vehicleRoot.transform.position;
            Vector3 forward = _vehicleRoot.transform.forward;
            SetInput(0f, 1f, 0f);
            yield return Steps(2 * StepsPerSecond);

            float turned = Vector3.SignedAngle(forward, _vehicleRoot.transform.forward, Vector3.up);
            Assert.That(turned, Is.GreaterThanOrEqualTo(20f), "右へ回った角度");
            Assert.That(Vector3.Distance(_vehicleRoot.transform.position, start), Is.LessThan(1f));
        }

        [UnityTest]
        public IEnumerator ブレーキで2秒以内に秒速0点2メートル未満まで止まる()
        {
            yield return Place();
            SetInput(1f, 0f, 0f);
            yield return Steps(2 * StepsPerSecond);
            Assert.That(ForwardSpeed, Is.GreaterThan(1f), "止める前に速度が出ていない（判定にならない）");

            SetInput(0f, 0f, 1f);
            int steps = 0;
            while (Body.velocity.magnitude >= 0.2f && steps < 2 * StepsPerSecond)
            {
                yield return new WaitForFixedUpdate();
                steps++;
            }

            Assert.That(Body.velocity.magnitude, Is.LessThan(0.2f));
        }

        [UnityTest]
        public IEnumerator 再適用すると最初の位置に戻り入力が0になり新しい質量になる()
        {
            yield return Place();
            Vector3 spawn = (Vector3)_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.spawnPosition));
            int generation = (int)_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.generation));
            SetInput(1f, 0.5f, 0f);
            yield return Steps(2 * StepsPerSecond);
            Assert.That(Vector3.Distance(_vehicleRoot.transform.position, spawn), Is.GreaterThan(0.3f), "走っていない（判定にならない）");

            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.mass = 15000f));

            Assert.That(Vector3.Distance(_vehicleRoot.transform.position, spawn), Is.LessThan(0.01f));
            Assert.That(Body.velocity.magnitude, Is.LessThan(0.01f));
            Assert.That(_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.throttleInput)), Is.EqualTo(0f));
            Assert.That(_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.steerInput)), Is.EqualTo(0f));
            Assert.That(_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.brakeInput)), Is.EqualTo(0f));
            Assert.That(_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.turnRate)), Is.EqualTo(0f), "旋回の速さが前の走行から残っている");
            // 1e-3 は相対の許容差（float の質量の丸めを見込む）。
            Assert.That(Body.mass, Is.EqualTo(15000f).Within(15000f * 1e-3f));
            Assert.That(_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.generation)), Is.EqualTo(generation + 1));
        }

        [UnityTest]
        public IEnumerator 再適用の値が不正なら転輪の駆動を外して止まったままにする()
        {
            yield return Place();
            SetInput(1f, 0f, 0f);
            yield return Steps(2 * StepsPerSecond);
            Assert.That(ForwardSpeed, Is.GreaterThan(1f), "止める前に速度が出ていない（判定にならない）");

            LogAssert.Expect(LogType.Error, new Regex(@"\[YakimaTank\].*mass"));
            YakimaTankPlayTuning.Reapply(_vehicle, Working(w => w.mass = 0f));
            yield return Steps(StepsPerSecond);

            Assert.That(_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.isReady)), Is.False);
            Assert.That(_vehicleRoot.GetComponentsInChildren<WheelCollider>().Select(w => w.motorTorque), Is.All.EqualTo(0f), "駆動のトルクが残っている");
            Assert.That(Body.velocity.magnitude, Is.LessThan(0.2f));
        }

        [UnityTest]
        public IEnumerator 出し直すと車両は1台のまま新しい値で動き部品の参照は新しい車両の中で閉じる()
        {
            yield return Place();
            GameObject old = _vehicleRoot;
            _vehicleRoot = YakimaTankPlayTuning.Respawn(old, _prefab, Working(w => w.mass = 15000f));
            _vehicle = YakimaTankRuntimeValues.FindVehicle(_vehicleRoot);
            yield return null;

            Assert.That(old == null, Is.True, "前の車両が残っている");
            Assert.That(Vehicles(), Has.Length.EqualTo(1));
            Assert.That(Body.mass, Is.EqualTo(15000f).Within(15000f * 1e-3f));
            Assert.That(_vehicle.GetProgramVariable(nameof(YakimaTankVehicle.body)), Is.SameAs(Body));
            foreach (string field in new[] { nameof(YakimaTankVehicle.leftWheels), nameof(YakimaTankVehicle.rightWheels) })
            {
                var wheels = (System.Array)_vehicle.GetProgramVariable(field);
                Assert.That(wheels.Cast<Component>().All(w => w != null && w.transform.IsChildOf(_vehicleRoot.transform)), Is.True, field);
            }

            yield return Steps(StepsPerSecond);
            Vector3 start = _vehicleRoot.transform.position;
            SetInput(1f, 0f, 0f);
            yield return Steps(2 * StepsPerSecond);
            Assert.That(Vector3.Dot(_vehicleRoot.transform.position - start, _vehicleRoot.transform.forward), Is.GreaterThan(0.5f), "出し直した車両が進まない");
        }
    }
}
