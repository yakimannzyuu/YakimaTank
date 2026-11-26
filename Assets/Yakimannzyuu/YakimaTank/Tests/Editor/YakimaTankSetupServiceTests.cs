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

            // ルートの車両の Udon と同期の Udon は、同じ GameObject なので Manual にそろえる。
            Assert.That(Vehicle(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.Manual));
            Assert.That(Udon<YakimaTankVehicleSync>(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.Manual));
            Assert.That(Udon<YakimaTankLife>(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.Manual));
            Assert.That(Udon<YakimaTankControlState>(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.None));
            Assert.That(Udon<YakimaTankBoarding>(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.None));
            // 座席は NoVariableSync。U# はそれを Continuous にそろえる（変数は送らない）。
            Assert.That(Udon<YakimaTankSeat>(prefab).SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.Continuous));
        }

        private static UdonBehaviour Udon<T>(GameObject root)
        {
            return root.GetComponentsInChildren<UdonBehaviour>(true).Single(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(T));
        }

        [Test]
        public void 同期のUdonはルートに1つで車両と座席と車体と物理の形の参照がそろい車両と座席からも指される()
        {
            GameObject prefab = GenerateOrFail();
            UdonBehaviour sync = Udon<YakimaTankVehicleSync>(prefab);

            Assert.That(sync.gameObject, Is.SameAs(prefab), "ルート（所有権の単位）");
            Assert.That(Reference(sync, nameof(YakimaTankVehicleSync.vehicle)), Is.SameAs(Vehicle(prefab)));
            Assert.That(Reference(sync, nameof(YakimaTankVehicleSync.seat)), Is.SameAs(Udon<YakimaTankSeat>(prefab)));
            Assert.That(Reference(sync, nameof(YakimaTankVehicleSync.body)), Is.SameAs(prefab.GetComponent<Rigidbody>()));
            Collider[] shapes = Value<Collider[]>(sync, nameof(YakimaTankVehicleSync.physicsColliders));
            Collider[] expected = new Collider[] { prefab.transform.Find("Colliders/Body").GetComponent<BoxCollider>() }
                .Concat(prefab.GetComponentsInChildren<WheelCollider>(true)).ToArray();
            Assert.That(shapes, Is.EquivalentTo(expected), "車体のコライダーと全部の WheelCollider（乗車の判定は入れない）");
            Assert.That(Reference(Vehicle(prefab), nameof(YakimaTankVehicle.sync)), Is.SameAs(sync));
            Assert.That(Reference(Udon<YakimaTankSeat>(prefab), nameof(YakimaTankSeat.sync)), Is.SameAs(sync));
            Assert.That(Value<float>(sync, nameof(YakimaTankVehicleSync.sendInterval)), Is.EqualTo(_settings.sendInterval));
        }

        [Test]
        public void 同期のUdonは接触に使う車体のBoxColliderを参照し接触の値が入る()
        {
            _settings.contactStopRate = 12f;
            _settings.contactPushStrength = 25f;
            _settings.contactMaxPushAcceleration = 4f;
            GameObject prefab = GenerateOrFail();
            UdonBehaviour sync = Udon<YakimaTankVehicleSync>(prefab);

            Assert.That(Reference(sync, nameof(YakimaTankVehicleSync.bodyCollider)), Is.SameAs(prefab.transform.Find("Colliders/Body").GetComponent<BoxCollider>()));
            Assert.That(Value<float>(sync, nameof(YakimaTankVehicleSync.contactStopRate)), Is.EqualTo(12f));
            Assert.That(Value<float>(sync, nameof(YakimaTankVehicleSync.contactPushStrength)), Is.EqualTo(25f));
            Assert.That(Value<float>(sync, nameof(YakimaTankVehicleSync.contactMaxPushAcceleration)), Is.EqualTo(4f));
        }

        private static Object Reference(UdonBehaviour udon, string name)
        {
            Assert.That(udon.publicVariables.TryGetVariableValue(name, out object value), Is.True, name + " が Udon に入っていない");
            return value as Object;
        }

        private Transform Turret(GameObject prefab)
        {
            return prefab.transform.Find(YakimaTankSetupService.ModelName + "/" + TestWorkspace.TurretPath);
        }

        [Test]
        public void 座席はルートの下で砲塔の下の位置の参照にずらしを足した所に車体と同じ向きで置く()
        {
            _settings.seatOffset = new Vector3(0.1f, 0.2f, -0.3f);
            GameObject prefab = GenerateOrFail();
            UdonBehaviour seatUdon = Udon<YakimaTankSeat>(prefab);
            Transform seat = seatUdon.transform;
            var anchor = (Transform)Reference(seatUdon, nameof(YakimaTankSeat.seatAnchor));

            Assert.That(seat.parent, Is.SameAs(prefab.transform), "座席は砲塔と一緒に回らない");
            Assert.That(anchor.parent, Is.SameAs(Turret(prefab)), "位置の参照は砲塔の下");
            Assert.That(Vector3.Distance(prefab.transform.InverseTransformPoint(anchor.position), new Vector3(0f, TestWorkspace.TurretTop, 0f)), Is.LessThan(1e-3f),
                "位置の参照は砲塔の上面の中央");
            Assert.That(Quaternion.Angle(anchor.rotation, prefab.transform.rotation), Is.LessThan(0.01f), "位置の参照の向きは、砲塔が 0 度のとき車体と同じ");
            Vector3 expected = new Vector3(0f, TestWorkspace.TurretTop, 0f) + _settings.seatOffset;
            Assert.That(Vector3.Distance(prefab.transform.InverseTransformPoint(seat.position), expected), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(seat.rotation, prefab.transform.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void 砲塔の対応がなければ生成しない()
        {
            _settings.turret = null;
            SetupResult result = YakimaTankSetupService.Generate(_settings);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics.Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(YakimaTankSetupSettings.turret)), Is.True);
        }

        [Test]
        public void 武器の同期のUdonはルートの子のWeaponに1つでManualで砲塔と砲身と砲口と車体の同期と座席を指し座席からも指される()
        {
            GameObject prefab = GenerateOrFail();
            UdonBehaviour weapon = Udon<YakimaTankWeaponSync>(prefab);
            Transform turret = Turret(prefab);

            Assert.That(weapon.transform.parent, Is.SameAs(prefab.transform), "ルートの子（車体とは別の所有権の領域）");
            Assert.That(weapon.gameObject.name, Is.EqualTo(YakimaTankSetupService.WeaponName));
            Assert.That(weapon.SyncMethod, Is.EqualTo(VRC.SDKBase.Networking.SyncType.Manual));
            Assert.That(Reference(weapon, nameof(YakimaTankWeaponSync.vehicleSync)), Is.SameAs(Udon<YakimaTankVehicleSync>(prefab)));
            Assert.That(Reference(weapon, nameof(YakimaTankWeaponSync.seat)), Is.SameAs(Udon<YakimaTankSeat>(prefab)));
            Assert.That(Reference(weapon, nameof(YakimaTankWeaponSync.control)), Is.SameAs(Udon<YakimaTankControlState>(prefab)));
            Assert.That(Reference(weapon, nameof(YakimaTankWeaponSync.turret)), Is.SameAs(turret));
            Assert.That(Reference(weapon, nameof(YakimaTankWeaponSync.barrel)), Is.SameAs(turret.Find("Barrel")));
            Assert.That(Reference(Udon<YakimaTankSeat>(prefab), nameof(YakimaTankSeat.weapon)), Is.SameAs(weapon));
            Assert.That(Value<Vector3>(weapon, nameof(YakimaTankWeaponSync.turretAxis)), Is.EqualTo(Vector3.up), "砲塔の左右の軸は車体の上");
            Assert.That(Value<Vector3>(weapon, nameof(YakimaTankWeaponSync.barrelAxis)), Is.EqualTo(Vector3.right), "砲身の上下の軸は車体の右");
            Assert.That(Value<float>(weapon, nameof(YakimaTankWeaponSync.reloadTime)), Is.EqualTo(_settings.reloadTime));
        }

        [Test]
        public void 生存の状態のUdonはルートに1つで車両と車体の同期と座席と武器を指しそれらと命中用のトリガーからも指されHPと復活の値が入る()
        {
            _settings.maxHealth = 80f;
            _settings.damage = 20f;
            _settings.respawnDelay = 7f;
            GameObject prefab = GenerateOrFail();
            UdonBehaviour life = Udon<YakimaTankLife>(prefab);
            UdonBehaviour sync = Udon<YakimaTankVehicleSync>(prefab);
            UdonBehaviour seat = Udon<YakimaTankSeat>(prefab);
            UdonBehaviour weapon = Udon<YakimaTankWeaponSync>(prefab);

            Assert.That(life.gameObject, Is.SameAs(prefab), "ルート（車体の所有権の単位）");
            Assert.That(Reference(life, nameof(YakimaTankLife.vehicle)), Is.SameAs(Vehicle(prefab)));
            Assert.That(Reference(life, nameof(YakimaTankLife.vehicleSync)), Is.SameAs(sync));
            Assert.That(Reference(life, nameof(YakimaTankLife.seat)), Is.SameAs(seat));
            Assert.That(Reference(life, nameof(YakimaTankLife.weapon)), Is.SameAs(weapon));
            Assert.That(Reference(Vehicle(prefab), nameof(YakimaTankVehicle.life)), Is.SameAs(life));
            Assert.That(Reference(seat, nameof(YakimaTankSeat.life)), Is.SameAs(life));
            Assert.That(Reference(weapon, nameof(YakimaTankWeaponSync.life)), Is.SameAs(life));
            Assert.That(Reference(sync, nameof(YakimaTankVehicleSync.weapon)), Is.SameAs(weapon), "撃った車両の武器を一覧から引く");
            foreach (UdonBehaviour hitbox in prefab.GetComponentsInChildren<UdonBehaviour>(true)
                         .Where(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankHitbox)))
            {
                Assert.That(Reference(hitbox, nameof(YakimaTankHitbox.life)), Is.SameAs(life), hitbox.name);
            }

            Assert.That(Value<float>(life, nameof(YakimaTankLife.maxHealth)), Is.EqualTo(80f));
            Assert.That(Value<float>(life, nameof(YakimaTankLife.respawnDelay)), Is.EqualTo(7f));
            Assert.That(Value<float>(weapon, nameof(YakimaTankWeaponSync.damage)), Is.EqualTo(20f), "ダメージは撃つ側の武器が持つ");
        }

        [Test]
        public void HPのバーはルートの下でモデルの上端より上にありコライダーがなく出力先のフォルダの材質を使う()
        {
            GameObject prefab = GenerateOrFail();
            UdonBehaviour life = Udon<YakimaTankLife>(prefab);
            var bar = (Transform)Reference(life, nameof(YakimaTankLife.healthBar));
            var fill = (Transform)Reference(life, nameof(YakimaTankLife.healthFill));

            Assert.That(bar.parent, Is.SameAs(prefab.transform));
            Assert.That(bar.name, Is.EqualTo(YakimaTankSetupService.HealthBarName));
            Assert.That(fill.parent, Is.SameAs(bar));
            Assert.That(YakimaTankBounds.TryGetLocalBounds(prefab.transform.Find(YakimaTankSetupService.ModelName), prefab.transform, out Bounds model), Is.True);
            Assert.That(bar.localPosition.y, Is.EqualTo(model.max.y + YakimaTankSetupService.HealthBarClearance).Within(1e-4f), "モデルの上端より 0.6 m 上");
            Assert.That(bar.localPosition.x, Is.EqualTo(model.center.x).Within(1e-4f));
            Assert.That(bar.localPosition.z, Is.EqualTo(model.center.z).Within(1e-4f));
            Assert.That(bar.GetComponentsInChildren<Collider>(true), Is.Empty, "砲弾と Interact を遮らない");
            Assert.That(fill.localPosition.x, Is.EqualTo(-YakimaTankSetupService.HealthBarWidth * 0.5f).Within(1e-4f), "中身は左端を軸に縮める");
            Assert.That(fill.localPosition.z, Is.LessThan(0f), "中身は背景の手前（頭の方）");
            string folder = System.IO.Path.GetDirectoryName(_settings.outputPrefabPath)?.Replace('\\', '/') + "/";
            Assert.That(AssetDatabase.GetAssetPath(bar.Find(YakimaTankSetupService.HealthBackName).GetComponent<MeshRenderer>().sharedMaterial),
                Is.EqualTo(folder + YakimaTankSetupService.HealthBackMaterialName));
            Assert.That(AssetDatabase.GetAssetPath(fill.GetComponentInChildren<MeshRenderer>().sharedMaterial),
                Is.EqualTo(folder + YakimaTankSetupService.HealthFillMaterialName));
        }

        [Test]
        public void 砲口は砲身の前の端で車体の前を向く()
        {
            GameObject prefab = GenerateOrFail();
            var muzzle = (Transform)Reference(Udon<YakimaTankWeaponSync>(prefab), nameof(YakimaTankWeaponSync.muzzle));

            Assert.That(muzzle.parent, Is.SameAs(Turret(prefab).Find("Barrel")));
            Vector3 position = prefab.transform.InverseTransformPoint(muzzle.position);
            Assert.That(position.z, Is.EqualTo(TestWorkspace.BarrelFront).Within(1e-3f), "砲口の位置 " + position.ToString("F3"));
            Assert.That(Quaternion.Angle(muzzle.rotation, prefab.transform.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void 命中用のトリガーは車体と砲塔に1つずつのDefaultのトリガーで車体の同期を指し車体の箱は車体のコライダーと同じ大きさ()
        {
            GameObject prefab = GenerateOrFail();
            UdonBehaviour[] hitboxes = prefab.GetComponentsInChildren<UdonBehaviour>(true)
                .Where(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankHitbox)).ToArray();
            UdonBehaviour sync = Udon<YakimaTankVehicleSync>(prefab);

            Assert.That(hitboxes, Has.Length.EqualTo(2));
            Assert.That(hitboxes.Select(h => h.transform.parent), Is.EquivalentTo(new[] { prefab.transform, Turret(prefab) }));
            foreach (UdonBehaviour hitbox in hitboxes)
            {
                var area = hitbox.GetComponent<BoxCollider>();
                Assert.That(area.isTrigger && area.enabled, Is.True, hitbox.name);
                Assert.That(hitbox.gameObject.layer, Is.EqualTo(0), hitbox.name + " は Default");
                Assert.That(Reference(hitbox, nameof(YakimaTankHitbox.vehicle)), Is.SameAs(sync), hitbox.name);
            }

            var body = prefab.transform.Find(YakimaTankSetupService.CollidersName + "/" + YakimaTankSetupService.BodyColliderName).GetComponent<BoxCollider>();
            var bodyHitbox = prefab.transform.Find(YakimaTankSetupService.HitboxName).GetComponent<BoxCollider>();
            Assert.That(bodyHitbox.center, Is.EqualTo(body.center));
            Assert.That(bodyHitbox.size, Is.EqualTo(body.size));
            var turretHitbox = Turret(prefab).Find(YakimaTankSetupService.TurretHitboxName).GetComponent<BoxCollider>();
            Assert.That(Vector3.Distance(turretHitbox.center, Vector3.zero), Is.LessThan(1e-3f), "砲身を除いた砲塔の箱");
            Assert.That(Vector3.Distance(turretHitbox.size, Vector3.one), Is.LessThan(1e-3f), "砲塔の立方体（砲塔の座標）");
        }

        [Test]
        public void 砲弾は武器の下に決まった数の見えない球で武器を指し出力先のフォルダの材質を使う()
        {
            GameObject prefab = GenerateOrFail();
            UdonBehaviour weapon = Udon<YakimaTankWeaponSync>(prefab);
            UdonBehaviour[] shells = prefab.GetComponentsInChildren<UdonBehaviour>(true)
                .Where(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankShell)).ToArray();

            Assert.That(shells, Has.Length.EqualTo(YakimaTankSetupMath.ShellCount(_settings.shellLifetime, _settings.reloadTime)));
            string material = System.IO.Path.GetDirectoryName(_settings.outputPrefabPath)?.Replace('\\', '/') + "/" + YakimaTankSetupService.ShellMaterialName;
            foreach (UdonBehaviour shell in shells)
            {
                Assert.That(shell.transform.IsChildOf(weapon.transform), Is.True, shell.name);
                Assert.That(Reference(shell, nameof(YakimaTankShell.weapon)), Is.SameAs(weapon));
                Assert.That(shell.GetComponent<Collider>(), Is.Null, "砲弾に当たりはない（光線で見る）");
                var renderer = shell.GetComponent<MeshRenderer>();
                Assert.That(renderer.enabled, Is.False, "撃つまで見えない");
                Assert.That(AssetDatabase.GetAssetPath(renderer.sharedMaterial), Is.EqualTo(material));
            }

        }

        [Test]
        public void 砲身の下に名前にMuzzleを含むGameObjectがあればその位置を砲口にする()
        {
            _settings = _workspace.CreateRiggedSettings(_workspace.CreateRiggedModel());
            GameObject prefab = GenerateOrFail();
            var muzzle = (Transform)Reference(Udon<YakimaTankWeaponSync>(prefab), nameof(YakimaTankWeaponSync.muzzle));

            Vector3 position = prefab.transform.InverseTransformPoint(muzzle.position);
            Assert.That(Vector3.Distance(position, new Vector3(0f, 1.5f, TestWorkspace.BarrelFront)), Is.LessThan(1e-3f), "砲口の位置 " + position.ToString("F3"));
            Assert.That(muzzle.parent.name, Is.EqualTo("GunPivot"), "砲身と一緒に回る");
            Assert.That(Quaternion.Angle(muzzle.rotation, prefab.transform.rotation), Is.LessThan(0.01f));
        }

        [Test]
        public void 砲塔の下にメッシュがなければ砲塔の命中用のトリガーは車体の箱の前後と左右のままモデルの上端までの箱でルートの下に置く()
        {
            _settings = _workspace.CreateRiggedSettings(_workspace.CreateRiggedModel());
            GameObject prefab = GenerateOrFail();

            var body = prefab.transform.Find(YakimaTankSetupService.CollidersName + "/" + YakimaTankSetupService.BodyColliderName).GetComponent<BoxCollider>();
            Transform upper = prefab.transform.Find(YakimaTankSetupService.TurretHitboxName);
            Assert.That(upper, Is.Not.Null, "ルートの下にない");
            var area = upper.GetComponent<BoxCollider>();
            Bounds bounds = new Bounds(area.center, area.size);
            Assert.That(bounds.min.x, Is.EqualTo(body.center.x - body.size.x * 0.5f).Within(1e-3f));
            Assert.That(bounds.max.z, Is.EqualTo(body.center.z + body.size.z * 0.5f).Within(1e-3f));
            Assert.That(bounds.min.y, Is.EqualTo(body.center.y - body.size.y * 0.5f).Within(1e-3f));
            Assert.That(bounds.max.y, Is.EqualTo(TestWorkspace.TurretTop).Within(1e-3f), "モデルの上端まで");
            Assert.That(area.isTrigger, Is.True);
        }

        [Test]
        public void 砲弾の数は寿命と装填の時間から決まり生成し直すと足し引きする()
        {
            _settings.reloadTime = 1f;
            GameObject first = GenerateOrFail();
            Assert.That(Shells(first), Is.EqualTo(4));

            _settings.reloadTime = 3f;
            GameObject second = GenerateOrFail();
            Assert.That(Shells(second), Is.EqualTo(2), "余った砲弾が残っている");
            Assert.That(second.transform.Find(YakimaTankSetupService.WeaponName + "/" + YakimaTankSetupService.ShellsName).childCount, Is.EqualTo(2));
        }

        [Test]
        public void 前の生成の名前の違う命中用のトリガーと位置の参照は生成し直すと残さない()
        {
            GenerateOrFail();
            GameObject contents = PrefabUtility.LoadPrefabContents(_settings.outputPrefabPath);
            Transform turret = contents.transform.Find(YakimaTankSetupService.ModelName + "/" + TestWorkspace.TurretPath);
            turret.Find(YakimaTankSetupService.TurretHitboxName).name = "TurretHitbox";
            turret.Find(YakimaTankSetupService.SeatAnchorName).name = "SeatAnchor";
            PrefabUtility.SaveAsPrefabAsset(contents, _settings.outputPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            GameObject prefab = GenerateOrFail();

            Assert.That(prefab.GetComponentsInChildren<UdonBehaviour>(true).Count(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankHitbox)), Is.EqualTo(2));
            Assert.That(Turret(prefab).Find("TurretHitbox"), Is.Null, "前の名前の命中用のトリガーが残った");
            Assert.That(Turret(prefab).Find("SeatAnchor"), Is.Null, "前の名前の位置の参照が残った");
        }

        private static int Shells(GameObject prefab)
        {
            return prefab.GetComponentsInChildren<UdonBehaviour>(true).Count(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankShell));
        }

        [Test]
        public void 前に砲塔の下に置いた座席も生成し直すとルートの下へ移り座席のオブジェクトを保つ()
        {
            GameObject first = GenerateOrFail();
            long seatId = LocalId(Udon<YakimaTankSeat>(first).gameObject);
            GameObject contents = PrefabUtility.LoadPrefabContents(_settings.outputPrefabPath);
            Transform oldSeat = contents.GetComponentsInChildren<VRC.SDK3.Components.VRCStation>(true).Single().transform;
            oldSeat.SetParent(contents.transform.Find(YakimaTankSetupService.ModelName + "/" + TestWorkspace.TurretPath), true);
            PrefabUtility.SaveAsPrefabAsset(contents, _settings.outputPrefabPath);
            PrefabUtility.UnloadPrefabContents(contents);

            GameObject second = GenerateOrFail();

            Assert.That(Udon<YakimaTankSeat>(second).transform.parent, Is.SameAs(second.transform));
            Assert.That(LocalId(Udon<YakimaTankSeat>(second).gameObject), Is.EqualTo(seatId), "座席が作り直された");
        }

        [Test]
        public void 座席はVRCStationで乗り物用に移動を止め移動の入力では降りずその場で乗り降りする()
        {
            GameObject prefab = GenerateOrFail();
            var station = prefab.GetComponentsInChildren<VRC.SDK3.Components.VRCStation>(true).Single();

            Assert.That(station.PlayerMobility, Is.EqualTo(VRC.SDKBase.VRCStation.Mobility.ImmobilizeForVehicle));
            Assert.That(station.stationEnterPlayerLocation, Is.SameAs(station.transform));
            Assert.That(station.stationExitPlayerLocation, Is.SameAs(station.transform));
            Assert.That(station.disableStationExit, Is.True, "VRCStation の標準の降り方（移動・ジャンプ）を止め、座席の Udon が降ろす");
        }

        [Test]
        public void 乗車の判定は前後と左右が車体のコライダーより5cm外で上下がモデル全体より5cm外のDefaultのトリガー()
        {
            GameObject prefab = GenerateOrFail();
            UdonBehaviour boarding = Udon<YakimaTankBoarding>(prefab);
            var area = boarding.GetComponent<BoxCollider>();

            Assert.That(area.isTrigger, Is.True);
            Assert.That(boarding.gameObject.layer, Is.EqualTo(0), "Default");
            Assert.That(Reference(boarding, nameof(YakimaTankBoarding.area)), Is.SameAs(area));
            // 車体のコライダーは x ±1・z ±2（転輪の外側 ±1.3 は含めない）。モデル全体は地面 0 から砲塔の上面 1.74。
            Assert.That(Vector3.Distance(area.center, new Vector3(0f, TestWorkspace.TurretTop * 0.5f, 0f)), Is.LessThan(1e-3f), "中心 " + area.center.ToString("F3"));
            Assert.That(Vector3.Distance(area.size, new Vector3(2.1f, TestWorkspace.TurretTop + 0.1f, 4.1f)), Is.LessThan(1e-3f), "大きさ " + area.size.ToString("F3"));
        }

        [Test]
        public void 砲身が車体の前に突き出すモデルでも乗車の判定の前端は車体のコライダーの前端より5cm前()
        {
            _settings = _workspace.CreateMeshlessParentSettings(_workspace.CreateMeshlessParentModel());
            GameObject prefab = GenerateOrFail();
            var area = Udon<YakimaTankBoarding>(prefab).GetComponent<BoxCollider>();

            Assert.That(area.center.z + area.size.z * 0.5f, Is.EqualTo(TestWorkspace.MeshlessHullFront + 0.05f).Within(1e-4f), "砲身の先 5 ではない");
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
                    typeof(YakimaTankVehicle), typeof(YakimaTankVehicleSync), typeof(YakimaTankControlState), typeof(YakimaTankDesktopInput),
                    typeof(YakimaTankVRInput), typeof(YakimaTankBoarding), typeof(YakimaTankSeat), typeof(YakimaTankWeaponSync),
                    typeof(YakimaTankHitbox), typeof(YakimaTankHitbox), typeof(YakimaTankLife)
                }.Concat(Enumerable.Repeat(typeof(YakimaTankShell), YakimaTankSetupMath.ShellCount(_settings.shellLifetime, _settings.reloadTime)))));
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
            long sync = LocalId(Udon<YakimaTankVehicleSync>(first));

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
            Assert.That(LocalId(Udon<YakimaTankVehicleSync>(second)), Is.EqualTo(sync), "同期");
            Assert.That(second.GetComponentsInChildren<UdonBehaviour>(true).Count(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankVehicleSync)), Is.EqualTo(1), "同期の Udon が重なった");
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
                working.sendInterval = 0.2f;
                working.maxExtrapolation = 0.5f;
                working.contactMaxPushAcceleration = 3f;
                Assert.That(_settings.maxTurnRate, Is.Not.EqualTo(45f), "保存する前に設定が変わっている");
                Assert.That(_settings.seatOffset, Is.Not.EqualTo(working.seatOffset), "保存する前に座席のずらしが変わっている");

                YakimaTankPlayTuning.SaveWorkingValues(working, _settings);
                GameObject prefab = GenerateOrFail();

                Assert.That(Value<float>(Vehicle(prefab), nameof(YakimaTankVehicle.maxTurnRate)), Is.EqualTo(45f));
                Assert.That(Value<Vector3>(Vehicle(prefab), nameof(YakimaTankVehicle.centerOfMassOffset)), Is.EqualTo(new Vector3(0f, -0.3f, 0f)));
                Assert.That(Value<Vector3>(Udon<YakimaTankSeat>(prefab), nameof(YakimaTankSeat.seatOffset)), Is.EqualTo(new Vector3(0f, 0.2f, 0f)));
                Assert.That(Value<float>(Udon<YakimaTankVRInput>(prefab), nameof(YakimaTankVRInput.maxTwistAngle)), Is.EqualTo(30f));
                Assert.That(Value<float>(Udon<YakimaTankVehicleSync>(prefab), nameof(YakimaTankVehicleSync.sendInterval)), Is.EqualTo(0.2f));
                Assert.That(Value<float>(Udon<YakimaTankVehicleSync>(prefab), nameof(YakimaTankVehicleSync.maxExtrapolation)), Is.EqualTo(0.5f));
                Assert.That(Value<float>(Udon<YakimaTankVehicleSync>(prefab), nameof(YakimaTankVehicleSync.contactMaxPushAcceleration)), Is.EqualTo(3f));
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
            _settings.barrel = otherSettings.barrel;

            GameObject second = GenerateOrFail();

            Transform model = second.transform.Find(YakimaTankSetupService.ModelName);
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject), Is.SameAs(other));
            Assert.That(second.transform.Cast<Transform>().Count(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)), Is.EqualTo(1));
            Assert.That(LocalId(Value<WheelCollider[]>(Vehicle(second), nameof(YakimaTankVehicle.leftWheels))[0]), Is.EqualTo(wheel));
            Assert.That(LocalId(Udon<YakimaTankSeat>(second).gameObject), Is.EqualTo(seat), "座席が作り直された");
            Assert.That(Udon<YakimaTankSeat>(second).transform.parent, Is.SameAs(second.transform), "座席がルートの下でない");
            var anchor = (Transform)Reference(Udon<YakimaTankSeat>(second), nameof(YakimaTankSeat.seatAnchor));
            Assert.That(anchor.parent, Is.SameAs(model.Find(TestWorkspace.TurretPath)), "位置の参照が新しいモデルの砲塔の下でない");
        }
    }
}
