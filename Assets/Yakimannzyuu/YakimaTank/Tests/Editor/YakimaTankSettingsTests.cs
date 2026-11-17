using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    public class YakimaTankSettingsTests
    {
        private TestWorkspace _workspace;
        private GameObject _model;

        [SetUp]
        public void SetUp()
        {
            _workspace = new TestWorkspace();
            _model = _workspace.CreateModel();
        }

        [TearDown]
        public void TearDown()
        {
            YakimaTankSetupValidator.LayerLookupForTests = null;
            _workspace.Dispose();
        }

        [Test]
        public void 再適用できる値は設定と受けるUdonに同じ名前と型である()
        {
            foreach (var group in YakimaTankRuntimeValues.Groups)
            {
                foreach (string name in group.Value)
                {
                    Assert.That(YakimaTankRuntimeValues.SettingsField(name), Is.Not.Null, "設定に " + name + " がない");
                    Assert.That(YakimaTankRuntimeValues.BehaviourField(group.Key, name), Is.Not.Null, group.Key.Name + " に " + name + " がない");
                    Assert.That(YakimaTankRuntimeValues.SettingsField(name).FieldType, Is.EqualTo(YakimaTankRuntimeValues.BehaviourField(group.Key, name).FieldType), name);
                }
            }
        }

        [Test]
        public void VRの調整値の初期値は仕様の仮の値()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            Assert.That(settings.gripThreshold, Is.EqualTo(0.5f));
            Assert.That(settings.maxTwistAngle, Is.EqualTo(45f));
            Assert.That(settings.maxPushDistance, Is.EqualTo(0.15f));
            Assert.That(settings.reverseThreshold, Is.EqualTo(0.5f));
            Assert.That(settings.deadzone, Is.EqualTo(0.1f));
            Assert.That(settings.seatOffset, Is.EqualTo(Vector3.zero));
        }

        /// <summary>保存した設定のアセットから、後の版で足した行を消し、版の番号を書き換えて、読み直す（古い版のアセットを再現する）。</summary>
        private static YakimaTankSetupSettings ReloadAsVersion(YakimaTankSetupSettings settings, int version, params string[] added)
        {
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            string path = AssetDatabase.GetAssetPath(settings);
            string[] lines = System.IO.File.ReadAllLines(path)
                .Where(line => !added.Any(name => line.TrimStart().StartsWith(name + ":")))
                .Select(line => line.TrimStart().StartsWith("schemaVersion:") ? "  schemaVersion: " + version : line)
                .ToArray();
            Assert.That(lines.Any(line => added.Any(name => line.TrimStart().StartsWith(name + ":"))), Is.False, "後の版の項目が残っている（判定にならない）");
            System.IO.File.WriteAllLines(path, lines);
            Resources.UnloadAsset(settings);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<YakimaTankSetupSettings>(path);
        }

        private static readonly string[] AddedInVersion3 = { "sendInterval", "maxExtrapolation" };
        private static readonly string[] AddedInVersion4 = { "contactStopRate", "contactPushStrength", "contactMaxPushAcceleration" };

        private static readonly string[] AddedInVersion5 =
        {
            "turretTurnRate", "gunTurnRate", "gunPitchMin", "gunPitchMax", "shellSpeed", "shellGravity", "shellLifetime", "reloadTime"
        };

        [Test]
        public void 版4の設定を読み込むと版5に移り値と参照を保って足した砲塔と武器の項目は既定値になる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model, "Version4");
            settings.mass = 1234f;
            settings.contactMaxPushAcceleration = 3f;
            settings.turretTurnRate = 1f;
            settings.reloadTime = 9f;

            YakimaTankSetupSettings migrated = ReloadAsVersion(settings, 4, AddedInVersion5);

            Assert.That(migrated.schemaVersion, Is.EqualTo(5));
            Assert.That(migrated.mass, Is.EqualTo(1234f));
            Assert.That(migrated.contactMaxPushAcceleration, Is.EqualTo(3f));
            Assert.That(migrated.barrel, Is.SameAs(_model.transform.Find(TestWorkspace.BarrelPath)));
            Assert.That(migrated.turretTurnRate, Is.EqualTo(30f), "足した項目が既定値になっていない");
            Assert.That(migrated.reloadTime, Is.EqualTo(3f));
            Assert.That(YakimaTankSetupValidator.Validate(migrated).Where(d => d.Severity == SetupSeverity.Error), Is.Empty);
        }

        [Test]
        public void 砲塔と武器の調整値の初期値は仕様の仮の値()
        {
            var settings = ScriptableObject.CreateInstance<YakimaTankSetupSettings>();
            try
            {
                Assert.That(settings.turretTurnRate, Is.EqualTo(30f));
                Assert.That(settings.gunTurnRate, Is.EqualTo(15f));
                Assert.That(settings.gunPitchMin, Is.EqualTo(-10f));
                Assert.That(settings.gunPitchMax, Is.EqualTo(20f));
                Assert.That(settings.shellSpeed, Is.EqualTo(200f));
                Assert.That(settings.shellGravity, Is.EqualTo(9.81f));
                Assert.That(settings.shellLifetime, Is.EqualTo(3f));
                Assert.That(settings.reloadTime, Is.EqualTo(3f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(nameof(YakimaTankSetupSettings.turretTurnRate), 0f)]
        [TestCase(nameof(YakimaTankSetupSettings.gunTurnRate), -1f)]
        [TestCase(nameof(YakimaTankSetupSettings.gunPitchMin), 1f)]
        [TestCase(nameof(YakimaTankSetupSettings.gunPitchMin), -91f)]
        [TestCase(nameof(YakimaTankSetupSettings.gunPitchMax), -1f)]
        [TestCase(nameof(YakimaTankSetupSettings.gunPitchMax), 91f)]
        [TestCase(nameof(YakimaTankSetupSettings.shellSpeed), 0f)]
        [TestCase(nameof(YakimaTankSetupSettings.shellGravity), -0.01f)]
        [TestCase(nameof(YakimaTankSetupSettings.shellLifetime), 0f)]
        [TestCase(nameof(YakimaTankSetupSettings.reloadTime), float.NaN)]
        public void 砲塔と武器の調整値が範囲外ならErrorになる(string field, float value)
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            typeof(YakimaTankSetupSettings).GetField(field).SetValue(settings, value);
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == field), Is.True);
        }

        [Test]
        public void 装填の時間が同期の送る間隔以下ならErrorになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.sendInterval = 0.5f;
            settings.reloadTime = 0.5f;
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.reloadTime)), Is.True);

            settings.reloadTime = 0.51f;
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Field == nameof(settings.reloadTime)), Is.False);
        }

        [Test]
        public void 砲身の下にメッシュも砲口の印もなければErrorになり砲口の印があれば通る()
        {
            GameObject rigged = _workspace.CreateRiggedModel();
            YakimaTankSetupSettings settings = _workspace.CreateRiggedSettings(rigged);
            Assert.That(YakimaTankSetupValidator.HasErrors(YakimaTankSetupValidator.Validate(settings)), Is.False, "砲口の印がある");

            string path = AssetDatabase.GetAssetPath(rigged);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            Object.DestroyImmediate(contents.transform.Find("Armature/TurretPivot/GunPivot/MuzzlePosition").gameObject);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);

            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.barrel)), Is.True);
        }

        [Test]
        public void 砲身が砲塔の下になければErrorになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.barrel = _model.transform.Find("Hull");
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.barrel)), Is.True);

            settings.barrel = settings.turret;
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.barrel)), Is.True, "砲塔そのもの");
        }

        [Test]
        public void 版3の設定を読み込むと今の版に移り値と参照を保って足した接触の項目は既定値になる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model, "Version3");
            settings.mass = 1234f;
            settings.sendInterval = 0.5f;
            settings.maxExtrapolation = 0.9f;
            settings.contactStopRate = 1f;
            settings.contactPushStrength = 2f;
            settings.contactMaxPushAcceleration = 3f;

            YakimaTankSetupSettings migrated = ReloadAsVersion(settings, 3, AddedInVersion4.Concat(AddedInVersion5).ToArray());

            Assert.That(migrated.schemaVersion, Is.EqualTo(YakimaTankSetupSettings.CurrentSchemaVersion));
            Assert.That(migrated.mass, Is.EqualTo(1234f));
            Assert.That(migrated.sendInterval, Is.EqualTo(0.5f));
            Assert.That(migrated.maxExtrapolation, Is.EqualTo(0.9f));
            Assert.That(migrated.body, Is.SameAs(_model.transform.Find("Hull")));
            Assert.That(migrated.contactStopRate, Is.EqualTo(10f), "足した項目が既定値になっていない");
            Assert.That(migrated.contactPushStrength, Is.EqualTo(20f));
            Assert.That(migrated.contactMaxPushAcceleration, Is.EqualTo(5f));
            Assert.That(YakimaTankSetupValidator.Validate(migrated).Where(d => d.Severity == SetupSeverity.Error), Is.Empty);
        }

        [Test]
        public void 接触の調整値の初期値は仕様の仮の値()
        {
            var settings = ScriptableObject.CreateInstance<YakimaTankSetupSettings>();
            try
            {
                Assert.That(settings.contactStopRate, Is.EqualTo(10f));
                Assert.That(settings.contactPushStrength, Is.EqualTo(20f));
                Assert.That(settings.contactMaxPushAcceleration, Is.EqualTo(5f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(nameof(YakimaTankSetupSettings.contactStopRate), -0.01f)]
        [TestCase(nameof(YakimaTankSetupSettings.contactPushStrength), -0.01f)]
        [TestCase(nameof(YakimaTankSetupSettings.contactMaxPushAcceleration), -0.01f)]
        [TestCase(nameof(YakimaTankSetupSettings.contactStopRate), float.NaN)]
        [TestCase(nameof(YakimaTankSetupSettings.contactMaxPushAcceleration), float.PositiveInfinity)]
        public void 接触の調整値が負か数でなければErrorになる(string field, float value)
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            typeof(YakimaTankSetupSettings).GetField(field).SetValue(settings, value);
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == field), Is.True);
        }

        [Test]
        public void 接触の調整値は0なら通る()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.contactStopRate = 0f;
            settings.contactPushStrength = 0f;
            settings.contactMaxPushAcceleration = 0f;
            Assert.That(YakimaTankSetupValidator.HasErrors(YakimaTankSetupValidator.Validate(settings)), Is.False);
        }

        [Test]
        public void 版2の設定を読み込むと今の版に移り値と参照を保って足した同期の項目は既定値になる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model, "Version2");
            settings.mass = 1234f;
            settings.gripThreshold = 0.9f;
            settings.seatOffset = Vector3.one;
            settings.sendInterval = 0.5f;
            settings.maxExtrapolation = 0.9f;

            YakimaTankSetupSettings migrated = ReloadAsVersion(settings, 2, AddedInVersion3.Concat(AddedInVersion4).Concat(AddedInVersion5).ToArray());

            Assert.That(migrated.schemaVersion, Is.EqualTo(YakimaTankSetupSettings.CurrentSchemaVersion));
            Assert.That(migrated.mass, Is.EqualTo(1234f));
            Assert.That(migrated.gripThreshold, Is.EqualTo(0.9f));
            Assert.That(migrated.seatOffset, Is.EqualTo(Vector3.one));
            Assert.That(migrated.body, Is.SameAs(_model.transform.Find("Hull")));
            Assert.That(migrated.turret, Is.SameAs(_model.transform.Find(TestWorkspace.TurretPath)));
            Assert.That(migrated.sendInterval, Is.EqualTo(0.1f), "足した項目が既定値になっていない");
            Assert.That(migrated.maxExtrapolation, Is.EqualTo(0.25f));
            Assert.That(YakimaTankSetupValidator.Validate(migrated).Where(d => d.Severity == SetupSeverity.Error), Is.Empty);
        }

        [Test]
        public void 同期の調整値の初期値は仕様の仮の値()
        {
            var settings = ScriptableObject.CreateInstance<YakimaTankSetupSettings>();
            try
            {
                Assert.That(settings.sendInterval, Is.EqualTo(0.1f));
                Assert.That(settings.maxExtrapolation, Is.EqualTo(0.25f));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase(nameof(YakimaTankSetupSettings.sendInterval), 0.04f)]
        [TestCase(nameof(YakimaTankSetupSettings.sendInterval), 1.01f)]
        [TestCase(nameof(YakimaTankSetupSettings.maxExtrapolation), -0.01f)]
        [TestCase(nameof(YakimaTankSetupSettings.maxExtrapolation), 1.01f)]
        public void 同期の調整値が範囲外ならErrorになる(string field, float value)
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            typeof(YakimaTankSetupSettings).GetField(field).SetValue(settings, value);
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == field), Is.True);
        }

        [Test]
        public void 版1の設定を読み込むと今の版に移り走行と足回りの値と参照を保って足した項目は既定値になる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model, "Old");
            settings.mass = 1234f;
            settings.suspensionDistance = 0.3f;
            settings.gripThreshold = 0.9f;
            settings.seatOffset = Vector3.one;
            string[] addedInVersion2 = { "seatOffset", "gripThreshold", "maxTwistAngle", "maxPushDistance", "reverseThreshold", "deadzone" };

            YakimaTankSetupSettings migrated = ReloadAsVersion(settings, 1, addedInVersion2.Concat(AddedInVersion3).Concat(AddedInVersion4).Concat(AddedInVersion5).ToArray());

            Assert.That(migrated.schemaVersion, Is.EqualTo(YakimaTankSetupSettings.CurrentSchemaVersion));
            Assert.That(migrated.mass, Is.EqualTo(1234f));
            Assert.That(migrated.suspensionDistance, Is.EqualTo(0.3f));
            Assert.That(migrated.body, Is.SameAs(_model.transform.Find("Hull")));
            Assert.That(migrated.leftWheels, Has.Length.EqualTo(3));
            Assert.That(migrated.gripThreshold, Is.EqualTo(0.5f), "足した項目が既定値になっていない");
            Assert.That(migrated.seatOffset, Is.EqualTo(Vector3.zero));
            Assert.That(migrated.sendInterval, Is.EqualTo(0.1f));
            Assert.That(YakimaTankSetupValidator.Validate(migrated).Where(d => d.Severity == SetupSeverity.Error), Is.Empty);
        }

        [Test]
        public void この版より新しい設定の版はErrorになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.schemaVersion = YakimaTankSetupSettings.CurrentSchemaVersion + 1;
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.schemaVersion)), Is.True);
        }

        [Test]
        public void Walkthroughのレイヤーがなければ生成の前にErrorになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            YakimaTankSetupValidator.LayerLookupForTests = name => -1;

            var diagnostics = YakimaTankSetupValidator.Validate(settings);

            Assert.That(diagnostics.Any(d => d.Severity == SetupSeverity.Error && d.Message.Contains(YakimaTankSetupValidator.WalkthroughLayer)), Is.True);
        }

        [Test]
        public void 元のモデルのトリガーでないコライダーは無効にするものを一覧にしたWarningになる()
        {
            GameObject withColliders = _workspace.CreateModel("ColliderTank", withColliders: true);
            YakimaTankSetupSettings settings = _workspace.CreateSettings(withColliders, "ColliderSettings");

            var diagnostics = YakimaTankSetupValidator.Validate(settings);

            Assert.That(YakimaTankSetupValidator.HasErrors(diagnostics), Is.False);
            SetupDiagnostic warning = diagnostics.Single(d => d.Severity == SetupSeverity.Warning && d.Field == nameof(settings.modelPrefab));
            StringAssert.Contains("Hull の BoxCollider", warning.Message);
            StringAssert.DoesNotContain("SphereCollider", warning.Message, "トリガーのコライダーは無効にしない");
        }

        [TestCase(nameof(YakimaTankSetupSettings.gripThreshold), 0f)]
        [TestCase(nameof(YakimaTankSetupSettings.gripThreshold), 1.5f)]
        [TestCase(nameof(YakimaTankSetupSettings.maxTwistAngle), 0f)]
        [TestCase(nameof(YakimaTankSetupSettings.maxPushDistance), 0f)]
        [TestCase(nameof(YakimaTankSetupSettings.reverseThreshold), 1f)]
        [TestCase(nameof(YakimaTankSetupSettings.deadzone), -0.1f)]
        [TestCase(nameof(YakimaTankSetupSettings.deadzone), 1f)]
        public void VRの調整値が範囲外ならErrorになる(string field, float value)
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            typeof(YakimaTankSetupSettings).GetField(field).SetValue(settings, value);
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == field), Is.True);
        }

        [Test]
        public void 同じ名前が並ぶ階層は番号つきのパスになる()
        {
            Transform wheel = _model.transform.Find("Left").GetChild(1);
            Assert.That(YakimaTankModelPaths.GetPath(wheel, _model.transform), Is.EqualTo("Left/Wheel[1]"));
            Assert.That(YakimaTankModelPaths.GetPath(_model.transform.Find("Hull"), _model.transform), Is.EqualTo("Hull"));
        }

        [Test]
        public void 番号つきのパスは同じTransformに戻る()
        {
            Transform wheel = _model.transform.Find("Right").GetChild(2);
            string path = YakimaTankModelPaths.GetPath(wheel, _model.transform);
            Assert.That(YakimaTankModelPaths.TryResolve(_model.transform, path, out Transform resolved, out _), Is.True);
            Assert.That(resolved, Is.SameAs(wheel));
        }

        [TestCase("Wheel[0]")]
        [TestCase("Left/Wheel")]
        [TestCase(@"Back\Slash")]
        [TestCase("]Wheel[")]
        public void 名前に予約文字があってもパスは同じTransformに戻る(string name)
        {
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                Transform Node(string nodeName, Transform parent)
                {
                    Transform node = ObjectFactory.CreateGameObject(preview, HideFlags.None, nodeName).transform;
                    node.SetParent(parent, false);
                    return node;
                }

                Transform root = Node("Root", null);
                Node("Wheel", root);
                Node("Left", root);
                Node("Child", Node(name, root));
                Transform duplicates = Node("Duplicates", root);
                Node(name, duplicates);
                Node(name, duplicates);

                foreach (Transform node in root.GetComponentsInChildren<Transform>(true).Where(t => t != root))
                {
                    string path = YakimaTankModelPaths.GetPath(node, root);
                    Assert.That(YakimaTankModelPaths.TryResolve(root, path, out Transform resolved, out string error), Is.True, path + ": " + error);
                    Assert.That(resolved, Is.SameAs(node), path);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        [Test]
        public void 名前だけで曖昧なパスは解決せずに候補を返す()
        {
            Assert.That(YakimaTankModelPaths.TryResolve(_model.transform, "Left/Wheel", out Transform resolved, out string error), Is.False);
            Assert.That(resolved, Is.Null);
            StringAssert.Contains("Wheel[0]", error);
        }

        [Test]
        public void ないパスは解決せずに子の一覧を返す()
        {
            Assert.That(YakimaTankModelPaths.TryResolve(_model.transform, "Hull/Gun", out _, out string error), Is.False);
            StringAssert.Contains("Turret", error);
        }

        [Test]
        public void 対応を全部埋めた設定は検証を通る()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            Assert.That(YakimaTankSetupValidator.Validate(settings), Is.Empty);
        }

        [Test]
        public void 車体と転輪がない設定は生成の前にErrorになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.body = null;
            settings.leftWheels = new Transform[0];
            var errors = YakimaTankSetupValidator.Validate(settings).Where(d => d.Severity == SetupSeverity.Error).Select(d => d.Field).ToList();
            Assert.That(errors, Does.Contain(nameof(YakimaTankSetupSettings.body)));
            Assert.That(errors, Does.Contain(nameof(YakimaTankSetupSettings.leftWheels)));
        }

        [Test]
        public void 元のモデルの外を指す対応はErrorになる()
        {
            GameObject other = _workspace.CreateModel("OtherTank");
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.body = other.transform.Find("Hull");
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.body)), Is.True);
        }

        [Test]
        public void 同じ転輪が左右に重なるとErrorになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.rightWheels = settings.rightWheels.Take(2).Concat(new[] { settings.leftWheels[0] }).ToArray();
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Message.Contains("左右の両方")), Is.True);
        }

        [TestCase(nameof(YakimaTankSetupSettings.mass), 0f)]
        [TestCase(nameof(YakimaTankSetupSettings.driveTorque), -1f)]
        [TestCase(nameof(YakimaTankSetupSettings.suspensionDistance), 0f)]
        [TestCase(nameof(YakimaTankSetupSettings.brakeTorque), float.NaN)]
        public void 範囲外の値はErrorになる(string field, float value)
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            typeof(YakimaTankSetupSettings).GetField(field).SetValue(settings, value);
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == field), Is.True);
        }

        [TestCase("Packages/com.example/Vehicle.prefab")]
        [TestCase("Assets/Vehicle.asset")]
        [TestCase("")]
        public void 出力先がAssetsの下のprefabでなければErrorになる(string path)
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.outputPrefabPath = path;
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.outputPrefabPath)), Is.True);
        }

        [Test]
        public void 半径を自動にした転輪にメッシュがなければErrorになり半径を決めれば通る()
        {
            string path = AssetDatabase.GetAssetPath(_model);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            new GameObject("Empty").transform.SetParent(contents.transform.Find("Left"), false);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            Assert.That(settings.leftWheels.Any(w => w.name == "Empty"), Is.True, "メッシュのない転輪を選べていない（判定にならない）");

            settings.wheelRadius = 0f;
            Assert.That(YakimaTankSetupValidator.Validate(settings)
                .Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.wheelRadius)), Is.True);

            settings.wheelRadius = TestWorkspace.WheelRadius;
            Assert.That(YakimaTankSetupValidator.Validate(settings)
                .Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.wheelRadius)), Is.False);
        }

        [Test]
        public void 左右の転輪の数が違えばWarningになるが生成は止めない()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.rightWheels = settings.rightWheels.Take(2).ToArray();

            var diagnostics = YakimaTankSetupValidator.Validate(settings);

            Assert.That(YakimaTankSetupValidator.HasErrors(diagnostics), Is.False);
            Assert.That(diagnostics.Any(d => d.Severity == SetupSeverity.Warning && d.Field == nameof(settings.rightWheels)), Is.True);
        }

        [Test]
        public void 車体自身にメッシュがなく砲塔と砲身を除くと車体の下にメッシュがなければ生成の前にErrorになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateMeshlessParentSettings(_workspace.CreateMeshlessParentModel(withHullMesh: false));

            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.body)), Is.True);
        }

        [Test]
        public void 車体自身にメッシュがなくても砲塔と砲身を除いて車体のメッシュがあれば検証を通る()
        {
            YakimaTankSetupSettings settings = _workspace.CreateMeshlessParentSettings(_workspace.CreateMeshlessParentModel());

            Assert.That(YakimaTankSetupValidator.HasErrors(YakimaTankSetupValidator.Validate(settings)), Is.False);
        }

        [Test]
        public void 砲塔か砲身の対応がなければErrorになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.turret = null;
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.turret)), Is.True);

            settings = _workspace.CreateSettings(_model, "NoBarrel");
            settings.barrel = null;
            Assert.That(YakimaTankSetupValidator.Validate(settings).Any(d => d.Severity == SetupSeverity.Error && d.Field == nameof(settings.barrel)), Is.True);
        }

        [Test]
        public void 階層パスで設定を書ける()
        {
            string path = _workspace.Folder + "/Written.asset";
            var request = new SettingsRequest
            {
                ModelPath = AssetDatabase.GetAssetPath(_model),
                BodyPath = "Hull",
                LeftWheelPaths = new[] { "Left/Wheel[0]", "Left/Wheel[1]", "Left/Wheel[2]" },
                RightWheelPaths = new[] { "Right/Wheel[0]", "Right/Wheel[1]", "Right/Wheel[2]" },
                TurretPath = TestWorkspace.TurretPath,
                BarrelPath = TestWorkspace.BarrelPath,
                OutputPrefabPath = _workspace.OutputPath
            };
            request.Values["mass"] = 1500.0;
            request.Values["centerOfMassOffset"] = new object[] { 0, -0.2, 0.1 };

            SettingsWriteResult result = YakimaTankSettingsWriter.Write(path, request);

            Assert.That(result.Errors, Is.Empty);
            var settings = AssetDatabase.LoadAssetAtPath<YakimaTankSetupSettings>(path);
            Assert.That(settings.leftWheels[2], Is.SameAs(_model.transform.Find("Left").GetChild(2)));
            Assert.That(settings.mass, Is.EqualTo(1500f));
            Assert.That(settings.centerOfMassOffset, Is.EqualTo(new Vector3(0f, -0.2f, 0.1f)));
            Assert.That(YakimaTankSetupValidator.Validate(settings), Is.Empty);
        }

        [Test]
        public void 解決できないパスが1つでもあれば何も書かない()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            float massBefore = settings.mass;
            var request = new SettingsRequest { BodyPath = "Left/Wheel" };
            request.Values["mass"] = 1.0;

            SettingsWriteResult result = YakimaTankSettingsWriter.Write(AssetDatabase.GetAssetPath(settings), request);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(settings.mass, Is.EqualTo(massBefore));
            Assert.That(settings.body, Is.SameAs(_model.transform.Find("Hull")));
        }

        [Test]
        public void 配置IDは設定の書き込みからは変えられない()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            var request = new SettingsRequest();
            request.Values["placementId"] = 1.0;
            Assert.That(YakimaTankSettingsWriter.Write(AssetDatabase.GetAssetPath(settings), request).Succeeded, Is.False);
        }
    }
}
