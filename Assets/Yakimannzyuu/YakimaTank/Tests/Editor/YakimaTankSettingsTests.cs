using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
            _workspace.Dispose();
        }

        [Test]
        public void 走行の値は設定と車両のUdonに同じ名前と型である()
        {
            foreach (string name in YakimaTankRuntimeValues.Names)
            {
                Assert.That(YakimaTankRuntimeValues.SettingsField(name), Is.Not.Null, "設定に " + name + " がない");
                Assert.That(YakimaTankRuntimeValues.VehicleField(name), Is.Not.Null, "車両に " + name + " がない");
                Assert.That(YakimaTankRuntimeValues.SettingsField(name).FieldType, Is.EqualTo(YakimaTankRuntimeValues.VehicleField(name).FieldType), name);
            }
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
        public void 砲塔は記録できるが未実装のWarningになる()
        {
            YakimaTankSetupSettings settings = _workspace.CreateSettings(_model);
            settings.turret = _model.transform.Find("Hull/Turret");
            var diagnostics = YakimaTankSetupValidator.Validate(settings);
            Assert.That(YakimaTankSetupValidator.HasErrors(diagnostics), Is.False);
            Assert.That(diagnostics.Any(d => d.Severity == SetupSeverity.Warning && d.Field == nameof(settings.turret)), Is.True);
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
