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
using VRC.SDK3.Components;
using VRC.Udon;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    public class YakimaTankScenePlacementTests
    {
        private const string BlueprintId = "wrld_00000000-0000-0000-0000-000000000000";

        private TestWorkspace _workspace;
        private YakimaTankSetupSettings _settings;
        private Scene _scene;
        private Transform _spawn;

        [SetUp]
        public void SetUp()
        {
            _workspace = new TestWorkspace();
            _settings = _workspace.CreateSettings(_workspace.CreateModel());
            Assert.That(YakimaTankSetupService.Generate(_settings).Succeeded, Is.True);

            _scene = TestWorkspace.OpenEmptyScene();
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(60f, 1f, 60f);
            var world = new GameObject("VRCWorld");
            _spawn = new GameObject("Spawn").transform;
            _spawn.SetParent(world.transform, false);
            _spawn.position = new Vector3(0f, 0f, -10f);
            world.AddComponent<VRCSceneDescriptor>().spawns = new[] { _spawn };
            SetBlueprintId(world, BlueprintId);
            new GameObject("Unmanaged").transform.position = new Vector3(5f, 1f, 5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 置いた車両のある間に UdonSharp の遅れた処理を走らせてから、シーンを置き換える。
            yield return TestWorkspace.WaitForDelayCalls();
            TestWorkspace.OpenEmptyScene();
            _workspace.Dispose();
        }

        private static Component PipelineManager(GameObject world)
        {
            return world.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "PipelineManager");
        }

        private static void SetBlueprintId(GameObject world, string id)
        {
            // コードで VRCSceneDescriptor を足しても PipelineManager は付かないので、ワールドのシーンと同じ形にする。
            // 型は SDK の DLL にあり、テストのアセンブリから参照しないので名前で探す。
            Component pipeline = PipelineManager(world);
            if (pipeline == null)
            {
                System.Type type = TypeCache.GetTypesDerivedFrom<MonoBehaviour>().Single(t => t.Name == "PipelineManager");
                pipeline = world.AddComponent(type);
            }

            var serialized = new SerializedObject(pipeline);
            serialized.FindProperty("blueprintId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string GetBlueprintId(GameObject world)
        {
            return new SerializedObject(PipelineManager(world)).FindProperty("blueprintId").stringValue;
        }

        private GameObject Root(string name)
        {
            return _scene.GetRootGameObjects().Single(g => g.name == name);
        }

        private int VehicleCount()
        {
            return _scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<UdonBehaviour>(true))
                .Count(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankVehicle));
        }

        [Test]
        public void 初回はSpawnの前方の床の上に置き位置を設定に保存する()
        {
            PlacementResult result = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(result.Created, Is.True);
            Assert.That(_settings.hasPlacement, Is.True);
            Assert.That(_settings.placementId, Is.Not.Empty);
            Assert.That(_settings.placementPosition.z, Is.GreaterThan(_spawn.position.z + 3f), "Spawn の前方");
            Assert.That(_settings.placementPosition.y, Is.InRange(0f, 1f), "床の上");
            Transform container = Root(YakimaTankScenePlacement.RootName).transform.Find(YakimaTankScenePlacement.ContainerName(_settings.placementId));
            Assert.That(container.childCount, Is.EqualTo(1));
            Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(container.GetChild(0).gameObject),
                Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(_settings.outputPrefabPath)));
        }

        [Test]
        public void 初回の高さは伸びきった転輪の下端を床のすぐ上にする()
        {
            YakimaTankScenePlacement.Place(_settings, _scene);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_settings.outputPrefabPath);
            float wheelBottom = prefab.GetComponentsInChildren<WheelCollider>(true)
                .Min(w => prefab.transform.InverseTransformPoint(w.transform.position).y - w.suspensionDistance - w.radius);
            float gap = _settings.placementPosition.y + wheelBottom;
            Assert.That(gap, Is.InRange(0f, 0.1f), "床（高さ 0）から転輪の下端までの隙間");
        }

        [Test]
        public void 初回の位置にほかのコライダーがあれば車両の長さだけ前へずらす()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_settings.outputPrefabPath);
            Assert.That(YakimaTankBounds.TryGetLocalBounds(prefab.transform, prefab.transform, out Bounds bounds), Is.True);
            float length = Mathf.Max(bounds.size.z, bounds.size.x);
            float firstZ = _spawn.position.z + length * 0.5f + YakimaTankScenePlacement.SpawnMargin;
            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.position = new Vector3(0f, 1f, firstZ);
            obstacle.transform.localScale = new Vector3(3f, 2f, 2f);

            PlacementResult result = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(_settings.placementPosition.z, Is.EqualTo(firstZ + length).Within(0.01f));
        }

        [Test]
        public void 入れ物の中が生成Prefabのインスタンスでなければ変えずに止める()
        {
            PlacementResult first = YakimaTankScenePlacement.Place(_settings, _scene);
            Transform container = first.Instance.transform.parent;
            Object.DestroyImmediate(first.Instance);
            new GameObject("Replaced").transform.SetParent(container, false);

            PlacementResult second = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Conflicts.Any(c => c.Contains("インスタンスではありません")), Is.True, string.Join(" / ", second.Conflicts));
            Assert.That(container.childCount, Is.EqualTo(1));
            Assert.That(container.GetChild(0).name, Is.EqualTo("Replaced"));
        }

        [Test]
        public void 配置はシーンを保存しない()
        {
            string scenePath = _workspace.Folder + "/Placement.unity";
            Assert.That(EditorSceneManager.SaveScene(_scene, scenePath), Is.True);
            string before = TestWorkspace.Hash(scenePath);

            PlacementResult result = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(result.Succeeded, Is.True, result.Error);
            Assert.That(_scene.isDirty, Is.True, "変更がシーンに入っていない（判定にならない）");
            Assert.That(TestWorkspace.Hash(scenePath), Is.EqualTo(before));
        }

        [Test]
        public void 二回更新しても車両は1台で配置IDと位置と外からの参照が保たれる()
        {
            PlacementResult first = YakimaTankScenePlacement.Place(_settings, _scene);
            Vector3 position = _settings.placementPosition;
            var holder = new GameObject("Holder");
            var constraint = holder.AddComponent<LookAtConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = first.Instance.transform, weight = 1f });

            PlacementResult second = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(second.Succeeded, Is.True, second.Error + " " + string.Join(" / ", second.Conflicts));
            Assert.That(second.Created, Is.False);
            Assert.That(second.PlacementId, Is.EqualTo(first.PlacementId));
            Assert.That(second.Instance, Is.SameAs(first.Instance));
            Assert.That(_settings.placementPosition, Is.EqualTo(position));
            Assert.That(VehicleCount(), Is.EqualTo(1));
            Assert.That(constraint.GetSource(0).sourceTransform, Is.SameAs(first.Instance.transform));
        }

        [Test]
        public void 管理の外のオブジェクトとBlueprintIDは変えない()
        {
            Vector3 unmanaged = Root("Unmanaged").transform.position;
            Vector3 spawn = _spawn.position;

            YakimaTankScenePlacement.Place(_settings, _scene);
            YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(Root("Unmanaged").transform.position, Is.EqualTo(unmanaged));
            Assert.That(_spawn.position, Is.EqualTo(spawn));
            Assert.That(GetBlueprintId(Root("VRCWorld")), Is.EqualTo(BlueprintId));
            Assert.That(_scene.GetRootGameObjects().Select(g => g.name),
                Is.EquivalentTo(new[] { "Floor", "VRCWorld", "Unmanaged", YakimaTankScenePlacement.RootName }));
        }

        [Test]
        public void 入れ物が手で動かされていたら変えずに止める()
        {
            PlacementResult first = YakimaTankScenePlacement.Place(_settings, _scene);
            Transform container = first.Instance.transform.parent;
            container.position += Vector3.right;
            Vector3 moved = container.position;

            PlacementResult second = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Conflicts, Is.Not.Empty);
            Assert.That(container.position, Is.EqualTo(moved));
        }

        [Test]
        public void インスタンスに手で加えた変更があれば変えずに止める()
        {
            PlacementResult first = YakimaTankScenePlacement.Place(_settings, _scene);
            WheelCollider wheel = first.Instance.GetComponentsInChildren<WheelCollider>(true).First();
            wheel.radius = 1.5f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(wheel);

            PlacementResult second = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Conflicts.Any(c => c.Contains("手で変えた")), Is.True);
            Assert.That(wheel.radius, Is.EqualTo(1.5f));
        }

        [Test]
        public void 車両のUdonSharpの部品に手で加えた変更も止める()
        {
            PlacementResult first = YakimaTankScenePlacement.Place(_settings, _scene);
            YakimaTankVehicle vehicle = first.Instance.GetComponent<YakimaTankVehicle>();
            vehicle.mass = 1f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(vehicle);

            PlacementResult second = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Conflicts.Any(c => c.Contains(nameof(YakimaTankVehicle))), Is.True);
        }

        [Test]
        public void 管理用のルートが2つあれば止める()
        {
            YakimaTankScenePlacement.Place(_settings, _scene);
            new GameObject(YakimaTankScenePlacement.RootName);

            PlacementResult second = YakimaTankScenePlacement.Place(_settings, _scene);

            Assert.That(second.Succeeded, Is.False);
            Assert.That(VehicleCount(), Is.EqualTo(1));
        }

        [Test]
        public void 生成Prefabがなければ置かない()
        {
            AssetDatabase.DeleteAsset(_settings.outputPrefabPath);
            PlacementResult result = YakimaTankScenePlacement.Place(_settings, _scene);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(_scene.GetRootGameObjects().Any(g => g.name == YakimaTankScenePlacement.RootName), Is.False);
        }
    }
}
