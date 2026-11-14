using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            YakimaTankSetupService.BeforeSaveForTests = null;
            YakimaTankSetupService.AfterSaveForTests = null;
            yield return TestWorkspace.WaitForDelayCalls();
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
        public void 出力先がこのツールで作っていないPrefabなら生成せずファイルも変えない()
        {
            AssetDatabase.CreateFolder(_workspace.Folder, "Generated");
            var plain = new GameObject("Plain");
            PrefabUtility.SaveAsPrefabAsset(plain, _settings.outputPrefabPath);
            Object.DestroyImmediate(plain);
            string before = TestWorkspace.Hash(_settings.outputPrefabPath);

            SetupResult result = YakimaTankSetupService.Generate(_settings);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(YakimaTankSetupSettings.outputPrefabPath)), Is.True);
            Assert.That(TestWorkspace.Hash(_settings.outputPrefabPath), Is.EqualTo(before));
        }

        [Test]
        public void 生成し直しても利用者が足したほかの入れ子のPrefabは消さない()
        {
            GenerateOrFail();
            GameObject decoration = _workspace.CreateModel("Decoration");
            GameObject contents = PrefabUtility.LoadPrefabContents(_settings.outputPrefabPath);
            PrefabUtility.InstantiatePrefab(decoration, contents.transform);
            PrefabUtility.SaveAsPrefabAsset(contents, _settings.outputPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            GameObject second = GenerateOrFail();

            Transform kept = second.transform.Find("Decoration");
            Assert.That(kept, Is.Not.Null, "足した入れ子の Prefab が消えた");
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(kept.gameObject), Is.SameAs(decoration));
        }

        [Test]
        public void 生成は開いているシーンを未保存にしない()
        {
            Scene scene = TestWorkspace.OpenEmptyScene();
            try
            {
                Assert.That(EditorSceneManager.SaveScene(scene, _workspace.Folder + "/Open.unity"), Is.True);

                GenerateOrFail();
                Assert.That(scene.isDirty, Is.False, "新しく作るときに未保存になった");
                GenerateOrFail();
                Assert.That(scene.isDirty, Is.False, "作り直すときに未保存になった");
            }
            finally
            {
                TestWorkspace.OpenEmptyScene();
            }
        }

        [Test]
        public void 生成したUdonの同期方式はスクリプトの指定と同じ()
        {
            // ずれていると、シーンを開いたときに UdonSharp がインスタンスの側で直し、手の変更と同じ上書きが残る。
            GameObject prefab = GenerateOrFail();

            Assert.That(Vehicle(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.None));
            Assert.That(Udon<YakimaTankControlState>(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.None));
            Assert.That(Udon<YakimaTankBoarding>(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.None));
            // 座席は NoVariableSync。U# はそれを Continuous にそろえる（変数は送らない）。
            Assert.That(Udon<YakimaTankSeat>(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.Continuous));
        }

        private static UdonBehaviour Udon<T>(GameObject root)
        {
            return root.GetComponentsInChildren<UdonBehaviour>(true).Single(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(T));
        }

        private static Object Reference(UdonBehaviour udon, string name)
        {
            Assert.That(udon.publicVariables.TryGetVariableValue(name, out object value), Is.True, name + " が Udon に入っていない");
            return value as Object;
        }

        [Test]
        public void 座席は砲塔の子で砲塔の上面の中央にずらしを足した所に車体と同じ向きで置く()
        {
            _settings.seatOffset = new Vector3(0.1f, 0.2f, -0.3f);
            GameObject prefab = GenerateOrFail();
            Transform seat = Udon<YakimaTankSeat>(prefab).transform;

            Assert.That(seat.parent, Is.SameAs(prefab.transform.Find(YakimaTankSetupService.ModelName + "/" + TestWorkspace.TurretPath)));
            Vector3 expected = new Vector3(0f, TestWorkspace.TurretTop, 0f) + _settings.seatOffset;
            Assert.That(Vector3.Distance(prefab.transform.InverseTransformPoint(seat.position), expected), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(seat.rotation, prefab.transform.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void 砲塔の対応がなければ座席は車体の子に置く()
        {
            _settings.turret = null;
            GameObject prefab = GenerateOrFail();

            Assert.That(Udon<YakimaTankSeat>(prefab).transform.parent, Is.SameAs(prefab.transform.Find(YakimaTankSetupService.ModelName + "/Hull")));
        }

        [Test]
        public void 座席はVRCStationで乗り物用に移動を止めその場で乗り降りする()
        {
            GameObject prefab = GenerateOrFail();
            var station = prefab.GetComponentsInChildren<VRC.SDK3.Components.VRCStation>(true).Single();

            Assert.That(station.PlayerMobility, Is.EqualTo(VRC.SDKBase.VRCStation.Mobility.ImmobilizeForVehicle));
            Assert.That(station.stationEnterPlayerLocation, Is.SameAs(station.transform));
            Assert.That(station.stationExitPlayerLocation, Is.SameAs(station.transform));
            Assert.That(station.disableStationExit, Is.False);
        }

        [Test]
        public void 乗車の判定はモデル全体の範囲のDefaultのトリガー()
        {
            GameObject prefab = GenerateOrFail();
            UdonBehaviour boarding = Udon<YakimaTankBoarding>(prefab);
            var area = boarding.GetComponent<BoxCollider>();

            Assert.That(area.isTrigger, Is.True);
            Assert.That(boarding.gameObject.layer, Is.EqualTo(0), "Default");
            Assert.That(Reference(boarding, nameof(YakimaTankBoarding.area)), Is.SameAs(area));
            // 転輪の外側 x ±1.3、地面 0 から砲塔の上面 1.74、車体の前後 z ±2。
            Assert.That(Vector3.Distance(area.center, new Vector3(0f, TestWorkspace.TurretTop * 0.5f, 0f)), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(area.size, new Vector3(2.6f, TestWorkspace.TurretTop, 4f)), Is.LessThan(1e-3f));
        }

        [Test]
        public void 車体のコライダーとWheelColliderはWalkthroughのレイヤーに置く()
        {
            GameObject prefab = GenerateOrFail();
            int walkthrough = LayerMask.NameToLayer(YakimaTankSetupValidator.WalkthroughLayer);

            Assert.That(walkthrough, Is.GreaterThanOrEqualTo(0), "プロジェクトに Walkthrough がない（判定にならない）");
            Assert.That(prefab.transform.Find("Colliders/Body").gameObject.layer, Is.EqualTo(walkthrough));
            Assert.That(prefab.GetComponentsInChildren<WheelCollider>(true).Select(w => w.gameObject.layer), Is.All.EqualTo(walkthrough));
        }

        [Test]
        public void コライダー付きのモデルでは元のトリガーでないコライダーを生成Prefabの中で無効にし元のアセットは変えない()
        {
            GameObject withColliders = _workspace.CreateModel("ColliderTank", withColliders: true);
            YakimaTankSetupSettings settings = _workspace.CreateSettings(withColliders, "ColliderSettings");
            string before = TestWorkspace.Hash(AssetDatabase.GetAssetPath(withColliders));

            SetupResult result = YakimaTankSetupService.Generate(settings);
            Assert.That(result.Succeeded, Is.True, result.Error);
            Transform model = AssetDatabase.LoadAssetAtPath<GameObject>(settings.outputPrefabPath).transform.Find(YakimaTankSetupService.ModelName);

            Assert.That(model.Find("Hull").GetComponent<BoxCollider>().enabled, Is.False);
            Assert.That(model.Find(TestWorkspace.TurretPath).GetComponent<SphereCollider>().enabled, Is.True, "トリガーは無効にしない");
            Assert.That(withColliders.transform.Find("Hull").GetComponent<BoxCollider>().enabled, Is.True);
            Assert.That(TestWorkspace.Hash(AssetDatabase.GetAssetPath(withColliders)), Is.EqualTo(before));
        }

        [Test]
        public void 座席と乗車の判定と入力と車両の参照が保存したPrefabのUdonにそろう()
        {
            GameObject prefab = GenerateOrFail();
            UdonBehaviour seat = Udon<YakimaTankSeat>(prefab);
            UdonBehaviour control = Udon<YakimaTankControlState>(prefab);

            Assert.That(Reference(seat, nameof(YakimaTankSeat.station)), Is.SameAs(seat.GetComponent<VRC.SDK3.Components.VRCStation>()));
            Assert.That(Reference(seat, nameof(YakimaTankSeat.boarding)), Is.SameAs(Udon<YakimaTankBoarding>(prefab)));
            Assert.That(Reference(seat, nameof(YakimaTankSeat.vehicle)), Is.SameAs(Vehicle(prefab)));
            Assert.That(Reference(seat, nameof(YakimaTankSeat.control)), Is.SameAs(control));
            Assert.That(Reference(seat, nameof(YakimaTankSeat.desktopInput)), Is.SameAs(Udon<YakimaTankDesktopInput>(prefab)));
            Assert.That(Reference(seat, nameof(YakimaTankSeat.vrInput)), Is.SameAs(Udon<YakimaTankVRInput>(prefab)));
            Assert.That(Reference(Udon<YakimaTankBoarding>(prefab), nameof(YakimaTankBoarding.seat)), Is.SameAs(seat));
            Assert.That(Reference(Vehicle(prefab), nameof(YakimaTankVehicle.control)), Is.SameAs(control));
            Assert.That(Reference(Udon<YakimaTankDesktopInput>(prefab), nameof(YakimaTankDesktopInput.control)), Is.SameAs(control));
            Assert.That(Reference(Udon<YakimaTankVRInput>(prefab), nameof(YakimaTankVRInput.vehicleBody)), Is.SameAs(prefab.transform));
            Assert.That(Value<float>(Udon<YakimaTankVRInput>(prefab), nameof(YakimaTankVRInput.maxTwistAngle)), Is.EqualTo(_settings.maxTwistAngle));
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
        public void 車体自身にメッシュがなければ車体のコライダーは子のメッシュから砲塔と砲身を除いて作る()
        {
            _settings = _workspace.CreateMeshlessParentSettings(_workspace.CreateMeshlessParentModel());
            GameObject prefab = GenerateOrFail();
            BoxCollider collider = prefab.transform.Find("Colliders/Body").GetComponent<BoxCollider>();
            Bounds box = new Bounds(collider.center, collider.size);

            Assert.That(box.max.z, Is.EqualTo(TestWorkspace.MeshlessHullFront).Within(1e-4f), "前端が車体のメッシュの前端（砲身の先 5 ではない）");
            Assert.That(box.max.y, Is.EqualTo(TestWorkspace.MeshlessHullTop).Within(1e-4f), "上端が車体のメッシュの上端（砲塔の上端 1.7 ではない）");
        }

        [Test]
        public void 砲塔自身にメッシュがなければ座席の自動の位置は砲身を除いた砲塔のメッシュの上面の中央()
        {
            _settings = _workspace.CreateMeshlessParentSettings(_workspace.CreateMeshlessParentModel());
            GameObject prefab = GenerateOrFail();
            Transform seat = Udon<YakimaTankSeat>(prefab).transform;

            Vector3 position = prefab.transform.InverseTransformPoint(seat.position);
            Assert.That(Vector3.Distance(position, TestWorkspace.MeshlessTurretTop), Is.LessThan(1e-3f), "座席の位置 " + position.ToString("F3"));
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
            Assert.That(prefab.GetComponentsInChildren<UdonBehaviour>(true).Select(UdonSharpEditorUtility.GetUdonSharpBehaviourType),
                Is.EquivalentTo(new[]
                {
                    typeof(YakimaTankVehicle), typeof(YakimaTankControlState), typeof(YakimaTankDesktopInput),
                    typeof(YakimaTankVRInput), typeof(YakimaTankBoarding), typeof(YakimaTankSeat)
                }));
            Assert.That(prefab.GetComponentsInChildren<VRC.SDK3.Components.VRCStation>(true), Has.Length.EqualTo(1));
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
            long seat = LocalId(Udon<YakimaTankSeat>(first).gameObject);
            long boarding = LocalId(Udon<YakimaTankBoarding>(first));
            long control = LocalId(Udon<YakimaTankControlState>(first));

            _settings.mass = 1234f;
            _settings.suspensionDistance = 0.3f;
            GameObject second = GenerateOrFail();

            Assert.That(AssetDatabase.AssetPathToGUID(_settings.outputPrefabPath), Is.EqualTo(guid));
            Assert.That(LocalId(Value<WheelCollider[]>(Vehicle(second), nameof(YakimaTankVehicle.rightWheels))[0]), Is.EqualTo(wheel));
            Assert.That(LocalId(second.transform.Find("Colliders/Body").GetComponent<BoxCollider>()), Is.EqualTo(box));
            Assert.That(LocalId(Vehicle(second)), Is.EqualTo(udon));
            Assert.That(LocalId(Udon<YakimaTankSeat>(second).gameObject), Is.EqualTo(seat), "座席");
            Assert.That(LocalId(Udon<YakimaTankBoarding>(second)), Is.EqualTo(boarding), "乗車の判定");
            Assert.That(LocalId(Udon<YakimaTankControlState>(second)), Is.EqualTo(control), "共通操作値");
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
                working.seatOffset = new Vector3(0f, 0.2f, 0f);
                working.maxTwistAngle = 30f;
                Assert.That(_settings.maxTurnRate, Is.Not.EqualTo(45f), "保存する前に設定が変わっている");
                Assert.That(_settings.seatOffset, Is.Not.EqualTo(working.seatOffset), "保存する前に座席のずらしが変わっている");

                YakimaTankPlayTuning.SaveWorkingValues(working, _settings);
                GameObject prefab = GenerateOrFail();

                Assert.That(Value<float>(Vehicle(prefab), nameof(YakimaTankVehicle.maxTurnRate)), Is.EqualTo(45f));
                Assert.That(Value<Vector3>(Vehicle(prefab), nameof(YakimaTankVehicle.centerOfMassOffset)), Is.EqualTo(new Vector3(0f, -0.3f, 0f)));
                Assert.That(Value<Vector3>(Udon<YakimaTankSeat>(prefab), nameof(YakimaTankSeat.seatOffset)), Is.EqualTo(new Vector3(0f, 0.2f, 0f)));
                Assert.That(Value<float>(Udon<YakimaTankVRInput>(prefab), nameof(YakimaTankVRInput.maxTwistAngle)), Is.EqualTo(30f));
            }
            finally
            {
                Object.DestroyImmediate(working);
            }
        }

        [Test]
        public void 同じ設定の作業値は作り直さずに変えた値を保つ()
        {
            var working = new YakimaTankWorkingValues();
            try
            {
                working.For(_settings).maxTurnRate = 45f;

                Assert.That(working.For(_settings).maxTurnRate, Is.EqualTo(45f));
            }
            finally
            {
                working.Discard();
            }
        }

        [Test]
        public void 設定を替えると作業値は新しい設定から作り直し前の設定の値で上書きしない()
        {
            YakimaTankSetupSettings other = _workspace.CreateSettings(_model, "OtherSettings");
            other.maxTurnRate = 20f;
            var working = new YakimaTankWorkingValues();
            try
            {
                working.For(_settings).maxTurnRate = 45f;

                YakimaTankSetupSettings forOther = working.For(other);
                YakimaTankPlayTuning.SaveWorkingValues(forOther, other);

                Assert.That(forOther.maxTurnRate, Is.EqualTo(20f));
                Assert.That(other.maxTurnRate, Is.EqualTo(20f));
                Assert.That(_settings.maxTurnRate, Is.Not.EqualTo(45f), "保存していない作業値が元の設定に入っている");
            }
            finally
            {
                working.Discard();
            }
        }

        [Test]
        public void 元のモデルを差し替えると入れ子のモデルだけを置き換えて転輪は使い回す()
        {
            GameObject first = GenerateOrFail();
            long wheel = LocalId(Value<WheelCollider[]>(Vehicle(first), nameof(YakimaTankVehicle.leftWheels))[0]);
            long seat = LocalId(Udon<YakimaTankSeat>(first).gameObject);
            GameObject other = _workspace.CreateModel("OtherTank");
            YakimaTankSetupSettings otherSettings = _workspace.CreateSettings(other, "OtherSettings");
            YakimaTankRuntimeValues.Copy(_settings, otherSettings);
            _settings.modelPrefab = other;
            _settings.body = otherSettings.body;
            _settings.leftWheels = otherSettings.leftWheels;
            _settings.rightWheels = otherSettings.rightWheels;
            _settings.turret = otherSettings.turret;

            GameObject second = GenerateOrFail();

            Transform model = second.transform.Find(YakimaTankSetupService.ModelName);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject), Is.SameAs(other));
            Assert.That(second.transform.Cast<Transform>().Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)), Is.EqualTo(1));
            Assert.That(LocalId(Value<WheelCollider[]>(Vehicle(second), nameof(YakimaTankVehicle.leftWheels))[0]), Is.EqualTo(wheel));
            Assert.That(LocalId(Udon<YakimaTankSeat>(second).gameObject), Is.EqualTo(seat), "座席が作り直された");
            Assert.That(Udon<YakimaTankSeat>(second).transform.parent, Is.SameAs(model.Find(TestWorkspace.TurretPath)), "座席が新しいモデルの砲塔の子でない");
        }
    }
}
