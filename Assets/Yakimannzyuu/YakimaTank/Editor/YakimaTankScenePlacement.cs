using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using Object = UnityEngine.Object;

namespace Yakimannzyuu.YakimaTank.Editor
{
    public sealed class PlacementResult
    {
        public bool Succeeded;
        public string Error;
        public string PlacementId;

        /// <summary>この呼び出しで車両を置いた（false なら前からあった車両をそのまま使った）。</summary>
        public bool Created;

        public Vector3 Position;
        public Vector3 Rotation;
        public GameObject Instance;
        public readonly List<string> Changes = new List<string>();

        /// <summary>手で変えた所など、自動で直さずに止めた理由。</summary>
        public readonly List<string> Conflicts = new List<string>();
    }

    /// <summary>
    /// 生成 Prefab をシーンに置く。管理用のルートの下に、配置 ID ごとの入れ物を 1 つ作り、その中に 1 台だけ置く。
    /// 2 回目以降は配置 ID で同じ車両を探し、手で変えた所があれば直さずに止める。シーンの保存は呼び出し側が行う。
    /// </summary>
    public static class YakimaTankScenePlacement
    {
        public const string RootName = "YakimaTank_Automation";
        public const string PlacementPrefix = "YakimaTankPlacement_";

        /// <summary>Spawn から車両の前端までの余白（m）。来た人の目の前で、重ならない距離。</summary>
        public const float SpawnMargin = 3f;

        private const float PositionTolerance = 0.001f;
        private const float AngleTolerance = 0.01f;
        private const float ScaleTolerance = 0.001f;
        private const int PlacementAttempts = 6;

        /// <summary>車両の下の床の高さの違いの上限（m）。段差や台の上に置かないため。</summary>
        private const float MaxFloorStep = 0.3f;

        /// <summary>
        /// 床を探し始める、Spawn からの高さ（m）。これより高い面は、天井・屋根・上の階とみなして床にしない。
        /// 高い所から探すと、屋内では屋根の上面に先に当たり、屋根の上に置いてしまう。
        /// </summary>
        private const float FloorSearchAbove = 2f;

        private const float FloorSearchBelow = 150f;

        private const string UdonSharpPrefabLinkPath = "serializationData.Prefab";

        public static string ContainerName(string placementId)
        {
            return PlacementPrefix + placementId;
        }

        public static PlacementResult Place(YakimaTankSetupSettings settings, Scene scene)
        {
            var result = new PlacementResult();
            if (settings == null)
            {
                result.Error = "設定がありません。";
                return result;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(settings.outputPrefabPath);
            if (prefab == null)
            {
                result.Error = $"生成 Prefab がありません（{settings.outputPrefabPath}）。先に生成してください。";
                return result;
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                result.Error = "シーンが読み込まれていません。";
                return result;
            }

            List<GameObject> roots = scene.GetRootGameObjects().Where(g => g.name == RootName).ToList();
            if (roots.Count > 1)
            {
                result.Conflicts.Add($"管理用のルート {RootName} が {roots.Count} 個あります。1 つにしてください。");
                result.Error = "シーンが自動の管理と食い違っています。";
                return result;
            }

            GameObject managed = roots.FirstOrDefault();
            Transform container = null;
            if (managed != null && !string.IsNullOrEmpty(settings.placementId))
            {
                List<Transform> matches = managed.transform.Cast<Transform>().Where(t => t.name == ContainerName(settings.placementId)).ToList();
                if (matches.Count > 1)
                {
                    result.Conflicts.Add($"配置 ID {settings.placementId} の入れ物が {matches.Count} 個あります。");
                }

                container = matches.FirstOrDefault();
            }

            if (container != null)
            {
                CheckExisting(container, prefab, settings, result.Conflicts);
            }

            if (result.Conflicts.Count > 0)
            {
                result.Error = "シーンが自動の管理と食い違っています。変えずに止めました。";
                return result;
            }

            result.PlacementId = settings.placementId;
            if (container != null)
            {
                result.Instance = FindVehicleInstance(container, prefab);
                result.Position = container.position;
                result.Rotation = container.eulerAngles;
                result.Changes.Add("変更なし（同じ配置 ID の車両がある。生成 Prefab の変更はインスタンスに自動で入る）");
                result.Succeeded = true;
                return result;
            }

            if (!settings.hasPlacement)
            {
                if (!TryFindFirstPose(prefab, scene, out Vector3 position, out Quaternion rotation, out string error))
                {
                    result.Error = error;
                    return result;
                }

                Undo.RecordObject(settings, "YakimaTank の配置");
                settings.placementPosition = position;
                settings.placementRotation = rotation.eulerAngles;
                settings.hasPlacement = true;
                result.Changes.Add($"初回の位置を決めて設定に保存した: {position}");
            }

            if (string.IsNullOrEmpty(settings.placementId))
            {
                Undo.RecordObject(settings, "YakimaTank の配置");
                settings.placementId = Guid.NewGuid().ToString("N");
                result.Changes.Add("配置 ID を作って設定に保存した: " + settings.placementId);
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            result.PlacementId = settings.placementId;

            if (managed == null)
            {
                managed = YakimaTankSetupService.CreateInScene(scene, RootName);
                Undo.RegisterCreatedObjectUndo(managed, "YakimaTank の配置");
                result.Changes.Add("管理用のルートを作った: " + RootName);
            }

            GameObject holder = YakimaTankSetupService.CreateInScene(scene, ContainerName(settings.placementId));
            Undo.RegisterCreatedObjectUndo(holder, "YakimaTank の配置");
            holder.transform.SetParent(managed.transform, false);
            holder.transform.SetPositionAndRotation(settings.placementPosition, Quaternion.Euler(settings.placementRotation));

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(instance, "YakimaTank の配置");
            instance.transform.SetParent(holder.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            EditorSceneManager.MarkSceneDirty(scene);

            result.Changes.Add($"車両を置いた: {RootName}/{holder.name}/{instance.name}");
            result.Created = true;
            result.Instance = instance;
            result.Position = settings.placementPosition;
            result.Rotation = settings.placementRotation;
            result.Succeeded = true;
            return result;
        }

        private static void CheckExisting(Transform container, GameObject prefab, YakimaTankSetupSettings settings, List<string> conflicts)
        {
            if (!settings.hasPlacement)
            {
                conflicts.Add("シーンに車両があるのに、設定に配置の位置がありません。");
            }
            else
            {
                if (Vector3.Distance(container.position, settings.placementPosition) > PositionTolerance)
                {
                    conflicts.Add($"入れ物の位置 {container.position} が、設定の位置 {settings.placementPosition} と違います。");
                }

                if (Quaternion.Angle(container.rotation, Quaternion.Euler(settings.placementRotation)) > AngleTolerance)
                {
                    conflicts.Add($"入れ物の向き {container.eulerAngles} が、設定の向き {settings.placementRotation} と違います。");
                }
            }

            if (!IsSameScale(container.lossyScale, Vector3.one))
            {
                conflicts.Add($"入れ物の大きさ {container.lossyScale} が 1 倍ではありません。");
            }

            if (container.GetComponents<Component>().Length != 1)
            {
                conflicts.Add("入れ物にコンポーネントが足されています。");
            }

            // 入れ物の中は、車両 1 台と、その車両を指すデバッグ UI 1 枚まで。開発ワールドの更新はパネルを並べて置くので、
            // パネルを競合にすると 2 回目の更新が止まる。それ以外の追加物は、手の変更として今までどおり止める。
            GameObject instance = null;
            var panels = new List<GameObject>();
            foreach (Transform child in container)
            {
                if (IsInstanceOf(child.gameObject, prefab))
                {
                    if (instance == null)
                    {
                        instance = child.gameObject;
                    }
                    else
                    {
                        conflicts.Add($"入れ物の中に車両が 2 台以上あります: {child.name}");
                    }
                }
                else if (YakimaTankDebugPanelPlacement.IsPanel(child.gameObject))
                {
                    panels.Add(child.gameObject);
                }
                else
                {
                    conflicts.Add($"{child.name} は、生成 Prefab（{settings.outputPrefabPath}）のインスタンスではありません（デバッグ UI でもありません）。");
                }
            }

            if (instance == null)
            {
                conflicts.Add($"入れ物の中に、生成 Prefab（{settings.outputPrefabPath}）のインスタンスの車両がありません。");
                return;
            }

            if (panels.Count > 1)
            {
                conflicts.Add($"入れ物の中にデバッグ UI が {panels.Count} 枚あります（1 枚まで）。");
            }

            foreach (GameObject panel in panels)
            {
                if (!YakimaTankDebugPanelPlacement.PointsTo(panel, instance))
                {
                    conflicts.Add($"デバッグ UI {panel.name} が、この入れ物の車両を指していません（車両の指定がないか、別の車両）。");
                }
            }

            if (instance.transform.localPosition.sqrMagnitude > PositionTolerance * PositionTolerance ||
                Quaternion.Angle(instance.transform.localRotation, Quaternion.identity) > AngleTolerance)
            {
                conflicts.Add($"{instance.name} が入れ物の中で動かされています。");
            }

            if (!IsSameScale(instance.transform.localScale, prefab.transform.localScale))
            {
                conflicts.Add($"{instance.name} の大きさ {instance.transform.localScale} が、生成 Prefab の {prefab.transform.localScale} と違います。");
            }

            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instance) ?? new PropertyModification[0];
            foreach (var modification in PrefabUtility.GetObjectOverrides(instance, false))
            {
                if (modification.instanceObject == instance.transform || IsUdonSharpPrefabLinkOnly(modification.instanceObject, modifications))
                {
                    continue;
                }

                conflicts.Add("手で変えた所があります: " + Describe(modification.instanceObject));
            }

            foreach (var added in PrefabUtility.GetAddedComponents(instance))
            {
                conflicts.Add("足したコンポーネントがあります: " + Describe(added.instanceComponent));
            }

            foreach (var added in PrefabUtility.GetAddedGameObjects(instance))
            {
                conflicts.Add("足したオブジェクトがあります: " + Describe(added.instanceGameObject));
            }

            foreach (var removed in PrefabUtility.GetRemovedComponents(instance))
            {
                conflicts.Add("消したコンポーネントがあります: " + Describe(removed.assetComponent));
            }

            foreach (var removed in PrefabUtility.GetRemovedGameObjects(instance))
            {
                conflicts.Add("消したオブジェクトがあります: " + Describe(removed.assetGameObject));
            }
        }

        private static bool IsInstanceOf(GameObject candidate, GameObject prefab)
        {
            return PrefabUtility.IsOutermostPrefabInstanceRoot(candidate) && PrefabUtility.GetCorrespondingObjectFromSource(candidate) == prefab;
        }

        /// <summary>入れ物の中の車両。子の順番に頼らず、生成 Prefab のインスタンスで見分ける。</summary>
        internal static GameObject FindVehicleInstance(Transform container, GameObject prefab)
        {
            foreach (Transform child in container)
            {
                if (IsInstanceOf(child.gameObject, prefab))
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        private static bool IsSameScale(Vector3 scale, Vector3 expected)
        {
            return (scale - expected).sqrMagnitude <= ScaleTolerance * ScaleTolerance;
        }

        /// <summary>
        /// UdonSharp は、Prefab のインスタンスの U# の部品に、元の部品への参照（serializationData.Prefab）を自分で記録する。
        /// 人の変更ではないので、それだけの差分は手の変更として数えない。ほかの差分が 1 つでもあれば数える。
        /// </summary>
        private static bool IsUdonSharpPrefabLinkOnly(Object instanceObject, PropertyModification[] modifications)
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(instanceObject);
            List<PropertyModification> own = modifications.Where(m => m.target == source).ToList();
            return own.Count > 0 && own.All(m => m.propertyPath == UdonSharpPrefabLinkPath && m.objectReference == source);
        }

        private static string Describe(Object target)
        {
            if (target is Component component)
            {
                return component.gameObject.name + " の " + component.GetType().Name;
            }

            return target == null ? "(なし)" : target.name;
        }

        /// <summary>
        /// 初回の位置。最初の Spawn の前方に、車両の長さの半分と余白だけ離し、真下の床に転輪が接する高さにする。
        /// ほかのコライダーと重なれば、車両の長さずつ前へずらす。
        /// </summary>
        public static bool TryFindFirstPose(GameObject prefab, Scene scene, out Vector3 position, out Quaternion rotation, out string error)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            error = null;

            VRC_SceneDescriptor descriptor = scene.GetRootGameObjects()
                .SelectMany(g => g.GetComponentsInChildren<VRC_SceneDescriptor>(true))
                .FirstOrDefault();
            if (descriptor == null)
            {
                error = "シーンに VRC_SceneDescriptor がないので、Spawn の位置が分かりません。";
                return false;
            }

            Transform spawn = descriptor.spawns != null && descriptor.spawns.Length > 0 && descriptor.spawns[0] != null
                ? descriptor.spawns[0]
                : descriptor.transform;
            Vector3 forward = Vector3.ProjectOnPlane(spawn.forward, Vector3.up);
            forward = forward.sqrMagnitude < 0.0001f ? Vector3.forward : forward.normalized;
            rotation = Quaternion.LookRotation(forward, Vector3.up);

            if (!YakimaTankBounds.TryGetLocalBounds(prefab.transform, prefab.transform, out Bounds bounds))
            {
                error = "生成 Prefab にメッシュがないので、大きさが分かりません。";
                return false;
            }

            float wheelBottom = WheelBottom(prefab, bounds);
            float length = Mathf.Max(bounds.size.z, bounds.size.x);
            PhysicsScene physics = scene.GetPhysicsScene();
            Physics.SyncTransforms();
            for (int attempt = 0; attempt < PlacementAttempts; attempt++)
            {
                Vector3 candidate = spawn.position + forward * (length * 0.5f + SpawnMargin + attempt * length);
                if (!TryFindFloor(physics, candidate, out RaycastHit floor))
                {
                    error = $"Spawn の前方（{candidate}）の下に床が見つかりません。";
                    return false;
                }

                position = new Vector3(candidate.x, floor.point.y - wheelBottom + 0.05f, candidate.z);
                Vector3 center = position + rotation * bounds.center + Vector3.up * 0.1f;
                var hits = new Collider[16];
                int count = physics.OverlapBox(center, bounds.extents * 0.95f, hits, rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool blocked = hits.Take(count).Any(hit => hit != floor.collider) || !IsFlatUnder(physics, candidate, rotation, bounds, floor);
                if (!blocked)
                {
                    return true;
                }
            }

            error = "Spawn の前方に、車両が重ならずに置ける場所が見つかりません。";
            return false;
        }

        /// <summary>
        /// 真ん中の真下だけで床を決めると、小さな台の上を床とみなして車両を載せてしまう。
        /// 車両の下の四隅も、ほぼ同じ高さに床があることを確かめる（床が複数のコライダーでもよい）。
        /// </summary>
        private static bool IsFlatUnder(PhysicsScene physics, Vector3 candidate, Quaternion rotation, Bounds bounds, RaycastHit floor)
        {
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    var corner = new Vector3(bounds.center.x + sx * bounds.extents.x * 0.9f, 0f, bounds.center.z + sz * bounds.extents.z * 0.9f);
                    if (!TryFindFloor(physics, candidate + rotation * corner, out RaycastHit hit) ||
                        Mathf.Abs(hit.point.y - floor.point.y) > MaxFloorStep)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryFindFloor(PhysicsScene physics, Vector3 candidate, out RaycastHit floor)
        {
            var hits = new RaycastHit[16];
            int count = physics.Raycast(candidate + Vector3.up * FloorSearchAbove, Vector3.down, hits, FloorSearchAbove + FloorSearchBelow,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            floor = hits.Take(count).OrderBy(hit => hit.distance).FirstOrDefault();
            return count > 0;
        }

        /// <summary>サスペンションが伸びきったときの、転輪のいちばん下の高さ（Prefab の座標）。</summary>
        private static float WheelBottom(GameObject prefab, Bounds bounds)
        {
            WheelCollider[] wheels = prefab.GetComponentsInChildren<WheelCollider>(true);
            if (wheels.Length == 0)
            {
                return bounds.min.y;
            }

            return wheels.Min(wheel =>
                prefab.transform.InverseTransformPoint(wheel.transform.position).y - wheel.suspensionDistance - wheel.radius);
        }
    }
}
