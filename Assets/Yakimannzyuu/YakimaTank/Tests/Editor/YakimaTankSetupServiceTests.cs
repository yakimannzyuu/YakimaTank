using System;
using System.Linq;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    public class YakimaTankSetupServiceTests
    {
        private TestWorkspace _workspace;
        private GameObject _model;
        private YakimaTankSetupSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _workspace = new TestWorkspace();
            _model = _workspace.CreateModel();
            _settings = _workspace.CreateSettings(_model);
        }

        [TearDown]
        public void TearDown()
        {
            YakimaTankSetupService.BeforeSaveForTests = null;
            YakimaTankSetupService.AfterSaveForTests = null;
            _workspace.Dispose();
        }

        private GameObject GenerateOrFail()
        {
            SetupResult result = YakimaTankSetupService.Generate(_settings);
            Assert.That(result.Succeeded, Is.True, result.Error);
            return AssetDatabase.LoadAssetAtPath<GameObject>(_settings.outputPrefabPath);
        }

        private static UdonBehaviour Vehicle(GameObject root)
        {
            return root.GetComponents<UdonBehaviour>().Single(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankVehicle));
        }

        private static T Value<T>(UdonBehaviour udon, string name)
        {
            Assert.That(udon.publicVariables.TryGetVariableValue(name, out T value), Is.True, name + " が Udon に入っていない");
            return value;
        }

        private static long LocalId(Object target)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out _, out long localId);
            return localId;
        }

        [Test]
        public void 生成すると元のモデルの入れ子とRigidbodyとWheelColliderと車両のUdonがそろう()
        {
            GameObject prefab = GenerateOrFail();

            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<WheelCollider>(true), Has.Length.EqualTo(6));
            Assert.That(prefab.transform.Find("Colliders/Body").GetComponent<BoxCollider>(), Is.Not.Null);
            Transform model = prefab.transform.Find(YakimaTankSetupService.ModelName);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject), Is.SameAs(_model));
            UdonBehaviour vehicle = Vehicle(prefab);
            Assert.That(Value<float>(vehicle, nameof(YakimaTankVehicle.mass)), Is.EqualTo(_settings.mass));
            Assert.That(Value<WheelCollider[]>(vehicle, nameof(YakimaTankVehicle.leftWheels)), Has.Length.EqualTo(3));
            Assert.That(Value<Rigidbody>(vehicle, nameof(YakimaTankVehicle.body)), Is.SameAs(prefab.GetComponent<Rigidbody>()));
        }

        [Test]
        public void 生成した車両のUdonの同期方式はスクリプトの指定と同じ()
        {
            // ずれていると、シーンを開いたときに UdonSharp がインスタンスの側で直し、手の変更と同じ上書きが残る。
            GameObject prefab = GenerateOrFail();

            Assert.That(Vehicle(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.None));
        }

        [Test]
        public void WheelColliderの半径と位置は転輪のメッシュから求める()
        {
            GameObject prefab = GenerateOrFail();
            WheelCollider front = Value<WheelCollider[]>(Vehicle(prefab), nameof(YakimaTankVehicle.leftWheels))[2];

            Assert.That(front.radius, Is.EqualTo(TestWorkspace.WheelRadius).Within(1e-4f));
            Vector3 expected = YakimaTankSetupMath.WheelColliderPosition(new Vector3(-1.15f, TestWorkspace.WheelRadius, 1.2f), _settings.suspensionDistance, YakimaTankSetupService.WheelTargetPosition);
            Assert.That(Vector3.Distance(front.transform.position, expected), Is.LessThan(1e-4f));
        }

        [Test]
        public void 元のモデルのルートの回転を保ったまま向きの補正を上から掛ける()
        {
            Quaternion own = Quaternion.Euler(0f, 30f, 0f);
            GameObject rotated = _workspace.CreateModel("RotatedTank", own);
            YakimaTankSetupSettings settings = _workspace.CreateSettings(rotated, "RotatedSettings");
            settings.modelRotation = new Vector3(0f, 90f, 0f);

            SetupResult result = YakimaTankSetupService.Generate(settings);
            Assert.That(result.Succeeded, Is.True, result.Error);
            Transform model = AssetDatabase.LoadAssetAtPath<GameObject>(settings.outputPrefabPath).transform.Find(YakimaTankSetupService.ModelName);

            Assert.That(Quaternion.Angle(model.localRotation, Quaternion.Euler(0f, 90f, 0f) * own), Is.LessThan(0.01f));
        }

        [Test]
        public void 車体のコライダーは車体自身のメッシュから作り子の砲塔を含めない()
        {
            GameObject prefab = GenerateOrFail();
            Bounds box = new Bounds(prefab.transform.Find("Colliders/Body").GetComponent<BoxCollider>().center, prefab.transform.Find("Colliders/Body").GetComponent<BoxCollider>().size);

            Assert.That(box.max.y, Is.EqualTo(1.3f).Within(1e-4f), "車体の上端（砲塔の上端 1.54 ではない）");
            Assert.That(box.min.y, Is.EqualTo(0.5f).Within(1e-4f), "車体の下端（転輪の中心 0.4 より上）");
        }

        [Test]
        public void 生成しても元のモデルのアセットは変わらない()
        {
            string before = TestWorkspace.Hash(AssetDatabase.GetAssetPath(_model));
            GenerateOrFail();
            GenerateOrFail();
            Assert.That(TestWorkspace.Hash(AssetDatabase.GetAssetPath(_model)), Is.EqualTo(before));
        }

        [Test]
        public void 二回生成してもコンポーネントとオブジェクトが重ならない()
        {
            GenerateOrFail();
            SetupResult second = YakimaTankSetupService.Generate(_settings);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_settings.outputPrefabPath);

            Assert.That(second.Succeeded, Is.True, second.Error);
            Assert.That(second.Created, Is.Empty, "2 回目に新しく作ったもの");
            Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Has.Length.EqualTo(1));
            Assert.That(prefab.GetComponentsInChildren<WheelCollider>(true), Has.Length.EqualTo(6));
            Assert.That(prefab.GetComponentsInChildren<UdonBehaviour>(true), Has.Length.EqualTo(1));
            Assert.That(prefab.transform.Cast<Transform>().Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)), Is.EqualTo(1));
        }

        [Test]
        public void 生成し直してもGUIDと中のオブジェクトを保つ()
        {
            GameObject first = GenerateOrFail();
            string guid = AssetDatabase.AssetPathToGUID(_settings.outputPrefabPath);
            long wheel = LocalId(Value<WheelCollider[]>(Vehicle(first), nameof(YakimaTankVehicle.rightWheels))[0]);
            long box = LocalId(first.transform.Find("Colliders/Body").GetComponent<BoxCollider>());
            long udon = LocalId(Vehicle(first));

            _settings.mass = 1234f;
            _settings.suspensionDistance = 0.3f;
            GameObject second = GenerateOrFail();

            Assert.That(AssetDatabase.AssetPathToGUID(_settings.outputPrefabPath), Is.EqualTo(guid));
            Assert.That(LocalId(Value<WheelCollider[]>(Vehicle(second), nameof(YakimaTankVehicle.rightWheels))[0]), Is.EqualTo(wheel));
            Assert.That(LocalId(second.transform.Find("Colliders/Body").GetComponent<BoxCollider>()), Is.EqualTo(box));
            Assert.That(LocalId(Vehicle(second)), Is.EqualTo(udon));
            Assert.That(Value<float>(Vehicle(second), nameof(YakimaTankVehicle.mass)), Is.EqualTo(1234f));
        }

        [Test]
        public void 生成し直してもシーンのインスタンスへの参照と手の値が保たれる()
        {
            GameObject prefab = GenerateOrFail();
            Scene scene = TestWorkspace.OpenEmptyScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                WheelCollider wheel = instance.GetComponentsInChildren<WheelCollider>(true).Single(w => w.name == "L1");
                var holder = new GameObject("Holder");
                SceneManager.MoveGameObjectToScene(holder, scene);
                var constraint = holder.AddComponent<LookAtConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = wheel.transform, weight = 1f });
                Light light = instance.AddComponent<Light>();
                light.intensity = 3f;

                _settings.driveTorque = 999f;
                GenerateOrFail();

                Assert.That(instance != null, Is.True, "インスタンスが消えた");
                Assert.That(constraint.GetSource(0).sourceTransform, Is.SameAs(wheel.transform), "シーンからの参照が切れた");
                Assert.That(wheel != null && wheel.name == "L1", Is.True);
                Assert.That(instance.GetComponent<Light>().intensity, Is.EqualTo(3f), "インスタンスに足した値が消えた");
                Assert.That(Value<float>(Vehicle(prefab), nameof(YakimaTankVehicle.driveTorque)), Is.EqualTo(999f));
            }
            finally
            {
                TestWorkspace.OpenEmptyScene();
            }
        }

        [Test]
        public void 保存の前に失敗したら前の成果物はそのまま残る()
        {
            GenerateOrFail();
            string before = TestWorkspace.Hash(_settings.outputPrefabPath);
            YakimaTankSetupService.BeforeSaveForTests = _ => throw new InvalidOperationException("テストの失敗");
            _settings.mass = 1f;

            SetupResult result = YakimaTankSetupService.Generate(_settings);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(TestWorkspace.Hash(_settings.outputPrefabPath), Is.EqualTo(before));
        }

        [Test]
        public void 保存の後の検査で失敗したら控えから前の成果物に戻す()
        {
            GenerateOrFail();
            string before = TestWorkspace.Hash(_settings.outputPrefabPath);
            YakimaTankSetupService.AfterSaveForTests = _ => throw new InvalidOperationException("テストの失敗");
            _settings.mass = 1f;

            SetupResult result = YakimaTankSetupService.Generate(_settings);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.RestoredPrevious, Is.True);
            Assert.That(TestWorkspace.Hash(_settings.outputPrefabPath), Is.EqualTo(before));
        }

        [Test]
        public void 検証にErrorがあれば何も作らない()
        {
            _settings.body = null;
            SetupResult result = YakimaTankSetupService.Generate(_settings);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(_settings.outputPrefabPath), Is.Null);
        }

        [Test]
        public void 作業値を設定に保存してから生成し直すと値が再現する()
        {
            GenerateOrFail();
            YakimaTankSetupSettings working = Object.Instantiate(_settings);
            try
            {
                working.maxTurnRate = 45f;
                working.centerOfMassOffset = new Vector3(0f, -0.3f, 0f);
                Assert.That(_settings.maxTurnRate, Is.Not.EqualTo(45f), "保存する前に設定が変わっている");

                YakimaTankPlayTuning.SaveWorkingValues(working, _settings);
                GameObject prefab = GenerateOrFail();

                Assert.That(Value<float>(Vehicle(prefab), nameof(YakimaTankVehicle.maxTurnRate)), Is.EqualTo(45f));
                Assert.That(Value<Vector3>(Vehicle(prefab), nameof(YakimaTankVehicle.centerOfMassOffset)), Is.EqualTo(new Vector3(0f, -0.3f, 0f)));
            }
            finally
            {
                Object.DestroyImmediate(working);
            }
        }

        [Test]
        public void 元のモデルを差し替えると入れ子のモデルだけを置き換えて転輪は使い回す()
        {
            GameObject first = GenerateOrFail();
            long wheel = LocalId(Value<WheelCollider[]>(Vehicle(first), nameof(YakimaTankVehicle.leftWheels))[0]);
            GameObject other = _workspace.CreateModel("OtherTank");
            YakimaTankSetupSettings otherSettings = _workspace.CreateSettings(other, "OtherSettings");
            YakimaTankRuntimeValues.Copy(_settings, otherSettings);
            _settings.modelPrefab = other;
            _settings.body = otherSettings.body;
            _settings.leftWheels = otherSettings.leftWheels;
            _settings.rightWheels = otherSettings.rightWheels;

            GameObject second = GenerateOrFail();

            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(second.transform.Find(YakimaTankSetupService.ModelName).gameObject), Is.SameAs(other));
            Assert.That(second.transform.Cast<Transform>().Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)), Is.EqualTo(1));
            Assert.That(LocalId(Value<WheelCollider[]>(Vehicle(second), nameof(YakimaTankVehicle.leftWheels))[0]), Is.EqualTo(wheel));
        }
    }
}
