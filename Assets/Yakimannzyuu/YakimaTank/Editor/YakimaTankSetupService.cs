using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace Yakimannzyuu.YakimaTank.Editor
{
    public sealed class SetupResult
    {
        public bool Succeeded;
        public string OutputPath;
        public string Error;

        /// <summary>途中で失敗し、控えから前の成果物に戻した。</summary>
        public bool RestoredPrevious;

        public readonly List<SetupDiagnostic> Diagnostics = new List<SetupDiagnostic>();
        public readonly List<string> Created = new List<string>();
        public readonly List<string> Updated = new List<string>();
        public readonly List<string> Removed = new List<string>();
    }

    /// <summary>
    /// 設定アセットから車両の Prefab を作る共通の処理（ADR 0008）。セットアップウィンドウと AI の連携は、どちらもここを呼ぶ。
    /// 元のモデルは入れ子の Prefab にして変えない。出力 Prefab はその場で更新し、GUID と中のオブジェクトを保つ。
    /// </summary>
    public static class YakimaTankSetupService
    {
        public const string ModelName = "Model";
        public const string CollidersName = "Colliders";
        public const string BodyColliderName = "Body";
        public const string WheelsName = "Wheels";
        public const float WheelTargetPosition = 0.5f;

        private const string BackupFolder = "Library/YakimaTank/Backups";

        /// <summary>テストで「保存の前」に失敗させるための口。</summary>
        internal static Action<GameObject> BeforeSaveForTests;

        /// <summary>テストで「保存の後」に失敗させ、控えから戻すことを確かめるための口。</summary>
        internal static Action<string> AfterSaveForTests;

        public static SetupResult Generate(YakimaTankSetupSettings settings)
        {
            var result = new SetupResult { OutputPath = settings != null ? settings.outputPrefabPath : null };
            result.Diagnostics.AddRange(YakimaTankSetupValidator.Validate(settings));
            if (YakimaTankSetupValidator.HasErrors(result.Diagnostics))
            {
                result.Error = "検証に Error があるので生成しません。";
                return result;
            }

            string path = settings.outputPrefabPath;
            string fullPath = Path.GetFullPath(path);
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            if (!exists && File.Exists(fullPath))
            {
                result.Error = "出力先に Prefab ではないファイルがあります: " + path;
                return result;
            }

            string backup = exists ? Backup(path, fullPath) : null;

            // 新しく作るときも、開いているシーンに一時のオブジェクトを置かない（ユーザーのシーンを未保存にしないため）。
            Scene preview = exists ? default : EditorSceneManager.NewPreviewScene();
            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(path)
                : CreateInScene(preview, Path.GetFileNameWithoutExtension(path));
            bool saved = false;
            try
            {
                Build(settings, root, exists, result);
                Inspect(settings, root);
                BeforeSaveForTests?.Invoke(root);
                EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success)
                {
                    throw new InvalidOperationException("Prefab を保存できませんでした: " + path);
                }

                saved = true;
                AfterSaveForTests?.Invoke(path);
                InspectSaved(settings, path);
                result.Succeeded = true;
            }
            catch (Exception exception)
            {
                result.Error = exception.Message;
                result.Created.Clear();
                result.Updated.Clear();
                result.Removed.Clear();
                if (saved && backup != null)
                {
                    File.Copy(backup, fullPath, true);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    result.RestoredPrevious = true;
                }
                else if (saved)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
            finally
            {
                DeferCleanup(() =>
                {
                    if (exists)
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                    else
                    {
                        Object.DestroyImmediate(root);
                        EditorSceneManager.ClosePreviewScene(preview);
                    }
                });
            }

            return result;
        }

        private static readonly List<Action> PendingCleanups = new List<Action>();

        [InitializeOnLoadMethod]
        private static void RegisterCleanupFlush()
        {
            AssemblyReloadEvents.beforeAssemblyReload += FlushCleanups;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                {
                    FlushCleanups();
                }
            };
        }

        /// <summary>
        /// 一時のオブジェクトの片付けを遅らせる。UdonSharp は部品を足した後の処理を delayCall で行い、すぐに片付けると
        /// その処理が消えたオブジェクトに触れて例外を出す。delayCall は積んだ順に走るので、片付けを後ろに積む。
        /// ただし Play に入る前と Reload の前には必ず片付ける。残ると一時のオブジェクトが Play のシーンに出てくる。
        /// </summary>
        private static void DeferCleanup(Action cleanup)
        {
            // 片付けごとに積む。まとめて片付けると、後から生成した分の UdonSharp の処理より先に消してしまう。
            PendingCleanups.Add(cleanup);
            EditorApplication.delayCall += () => RunCleanup(cleanup);
        }

        private static void FlushCleanups()
        {
            foreach (Action cleanup in PendingCleanups.ToList())
            {
                RunCleanup(cleanup);
            }
        }

        private static void RunCleanup(Action cleanup)
        {
            if (!PendingCleanups.Remove(cleanup))
            {
                return;
            }

            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static string Backup(string path, string fullPath)
        {
            Directory.CreateDirectory(BackupFolder);
            string backup = Path.Combine(BackupFolder, AssetDatabase.AssetPathToGUID(path) + ".prefab");
            File.Copy(fullPath, backup, true);
            return backup;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        private struct WheelSpec
        {
            public Vector3 Center;
            public float Radius;
        }

        private static void Build(YakimaTankSetupSettings settings, GameObject root, bool updating, SetupResult result)
        {
            void Record(string item, bool created)
            {
                (created || !updating ? result.Created : result.Updated).Add(item);
            }

            GameObject model = settings.modelPrefab;
            string modelPath = AssetDatabase.GetAssetPath(model);

            Rigidbody body = root.GetComponent<Rigidbody>();
            bool bodyCreated = body == null;
            if (bodyCreated)
            {
                body = root.AddComponent<Rigidbody>();
            }

            body.mass = settings.mass;
            body.drag = 0.05f;
            body.angularDrag = 0.5f;
            body.useGravity = true;
            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            Record("Rigidbody", bodyCreated);

            GameObject modelInstance = FindOrReplaceModel(root, model, result);
            bool modelCreated = modelInstance == null;
            if (modelCreated)
            {
                modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                modelInstance.name = ModelName;
            }

            // モデルのルートの回転（Blender の FBX の軸の変換など）を消さずに、補正を上から掛ける。
            Quaternion correction = Quaternion.Euler(settings.modelRotation);
            modelInstance.transform.localPosition = correction * model.transform.localPosition;
            modelInstance.transform.localRotation = correction * model.transform.localRotation;
            modelInstance.transform.localScale = model.transform.localScale;
            Record(ModelName, modelCreated);

            Dictionary<Object, Transform> lookup = BuildLookup(modelInstance, modelPath);
            Transform Resolve(Transform mapped)
            {
                if (lookup.TryGetValue(mapped, out Transform resolved))
                {
                    return resolved;
                }

                throw new InvalidOperationException("対応を、生成する Prefab の中で見つけられません: " + YakimaTankModelPaths.GetPath(mapped, model.transform));
            }

            List<WheelSpec> left = settings.leftWheels.Select(w => Wheel(Resolve(w), root.transform, settings)).ToList();
            List<WheelSpec> right = settings.rightWheels.Select(w => Wheel(Resolve(w), root.transform, settings)).ToList();
            float wheelCenterHeight = left.Concat(right).Average(w => w.Center.y);

            // 子（砲塔・砲身）を含めると、砲身の分だけ前に見えない箱が張り出すので、車体自身のメッシュから作る。
            if (!YakimaTankBounds.TryGetOwnBounds(Resolve(settings.body), root.transform, out Bounds bodyBounds))
            {
                throw new InvalidOperationException("車体にメッシュがないので、車体のコライダーの大きさを決められません。");
            }

            Bounds box = YakimaTankSetupMath.BodyColliderBounds(bodyBounds, wheelCenterHeight);
            Transform colliders = EnsureChild(root.transform, CollidersName, out bool collidersCreated);
            Transform bodyColliderTransform = EnsureChild(colliders, BodyColliderName, out bool bodyColliderCreated);
            BoxCollider boxCollider = bodyColliderTransform.GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = bodyColliderTransform.gameObject.AddComponent<BoxCollider>();
            }

            boxCollider.center = box.center;
            boxCollider.size = box.size;
            Record(CollidersName + "/" + BodyColliderName, collidersCreated || bodyColliderCreated);

            YakimaTankVehicle vehicle = FindVehicleProxy(root);
            bool vehicleCreated = vehicle == null;
            if (vehicleCreated)
            {
                vehicle = UdonSharpUndo.AddComponent<YakimaTankVehicle>(root);
            }

            Transform wheels = EnsureChild(root.transform, WheelsName, out _);
            int total = left.Count + right.Count;
            float spring = settings.suspensionSpring > 0f
                ? settings.suspensionSpring
                : YakimaTankSetupMath.AutoSuspensionSpring(settings.mass, total, settings.suspensionDistance);
            float damper = settings.suspensionDamper > 0f
                ? settings.suspensionDamper
                : YakimaTankSetupMath.AutoSuspensionDamper(spring, settings.mass, total);
            WheelCollider[] leftColliders = ReconcileWheels(vehicle.leftWheels, left, "L", wheels, root.transform, settings, spring, damper, updating, result);
            WheelCollider[] rightColliders = ReconcileWheels(vehicle.rightWheels, right, "R", wheels, root.transform, settings, spring, damper, updating, result);

            YakimaTankRuntimeValues.WriteToProxy(settings, vehicle);
            vehicle.baseCenterOfMass = new Vector3(box.center.x, wheelCenterHeight, box.center.z);
            vehicle.body = body;
            vehicle.leftWheels = leftColliders;
            vehicle.rightWheels = rightColliders;
            vehicle.applyOnStart = true;
            UdonSharpEditorUtility.CopyProxyToUdon(vehicle);

            // UdonSharp は同期方式をシーンのオブジェクトでしか直さない。Prefab の側で合わせておかないと、
            // シーンを開くたびにインスタンスの側で直され、手の変更と同じ上書きが残る。
            UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(vehicle);
            if (TryGetSyncType(out Networking.SyncType sync) && udon.SyncMethod != sync)
            {
                udon.SyncMethod = sync;
            }
            Record(nameof(YakimaTankVehicle), vehicleCreated);
        }

        private static bool TryGetSyncType(out Networking.SyncType sync)
        {
            var attribute = (UdonBehaviourSyncModeAttribute)Attribute.GetCustomAttribute(typeof(YakimaTankVehicle), typeof(UdonBehaviourSyncModeAttribute));
            switch (attribute == null ? BehaviourSyncMode.Any : attribute.behaviourSyncMode)
            {
                case BehaviourSyncMode.None:
                    sync = Networking.SyncType.None;
                    return true;
                case BehaviourSyncMode.Continuous:
                    sync = Networking.SyncType.Continuous;
                    return true;
                case BehaviourSyncMode.Manual:
                    sync = Networking.SyncType.Manual;
                    return true;
                default:
                    sync = default;
                    return false;
            }
        }

        private static GameObject FindOrReplaceModel(GameObject root, GameObject model, SetupResult result)
        {
            GameObject found = null;
            // 生成が置いた「Model」だけを見る。利用者が足したほかの入れ子の Prefab は消さない。
            foreach (Transform child in root.transform.Cast<Transform>().Where(t => t.name == ModelName).ToList())
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                {
                    continue;
                }

                if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == model && found == null)
                {
                    found = child.gameObject;
                    continue;
                }

                result.Removed.Add(child.name + "（前のモデル）");
                Object.DestroyImmediate(child.gameObject);
            }

            return found;
        }

        private static Dictionary<Object, Transform> BuildLookup(GameObject modelInstance, string modelPath)
        {
            var lookup = new Dictionary<Object, Transform>();
            foreach (Transform transform in modelInstance.GetComponentsInChildren<Transform>(true))
            {
                Transform source = PrefabUtility.GetCorrespondingObjectFromSourceAtPath(transform, modelPath);
                if (source != null && !lookup.ContainsKey(source))
                {
                    lookup.Add(source, transform);
                }
            }

            return lookup;
        }

        private static WheelSpec Wheel(Transform wheel, Transform root, YakimaTankSetupSettings settings)
        {
            if (YakimaTankBounds.TryGetLocalBounds(wheel, root, out Bounds bounds))
            {
                float radius = settings.wheelRadius > 0f ? settings.wheelRadius : YakimaTankSetupMath.WheelRadiusFromBounds(bounds);
                if (radius <= 0.001f)
                {
                    throw new InvalidOperationException("転輪の半径が 0 です: " + wheel.name);
                }

                return new WheelSpec { Center = bounds.center, Radius = radius };
            }

            return new WheelSpec { Center = root.InverseTransformPoint(wheel.position), Radius = settings.wheelRadius };
        }

        private static Transform EnsureChild(Transform parent, string name, out bool created)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name && !PrefabUtility.IsPartOfPrefabInstance(child))
                {
                    created = false;
                    return child;
                }
            }

            created = true;
            return CreateChild(parent, name).transform;
        }

        private static GameObject CreateChild(Transform parent, string name)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        /// <summary>
        /// ObjectFactory.CreateGameObject は、ほかのシーンを指定しても開いているシーンに未保存の印を付ける。
        /// new GameObject はいったん開いているシーンにできるが、すぐに移せば印は付かない。
        /// </summary>
        internal static GameObject CreateInScene(Scene scene, string name)
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static YakimaTankVehicle FindVehicleProxy(GameObject root)
        {
            UdonBehaviour[] behaviours = root.GetComponents<UdonBehaviour>()
                .Where(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankVehicle))
                .ToArray();
            if (behaviours.Length > 1)
            {
                throw new InvalidOperationException("車両の Udon が 2 つ以上あります。生成 Prefab を手で変えていないか確かめてください。");
            }

            return behaviours.Length == 0 ? null : (YakimaTankVehicle)UdonSharpEditorUtility.GetProxyBehaviour(behaviours[0]);
        }

        // 作り直すとシーンからの参照が切れるので、前の WheelCollider を番号ごとに使い回す。
        private static WheelCollider[] ReconcileWheels(
            WheelCollider[] existing, List<WheelSpec> specs, string prefix, Transform container, Transform root,
            YakimaTankSetupSettings settings, float spring, float damper, bool updating, SetupResult result)
        {
            existing = existing ?? new WheelCollider[0];
            var colliders = new WheelCollider[specs.Count];
            for (int index = 0; index < specs.Count; index++)
            {
                WheelCollider collider = index < existing.Length && existing[index] != null && existing[index].transform.IsChildOf(root)
                    ? existing[index]
                    : null;
                bool created = collider == null;
                if (created)
                {
                    collider = CreateChild(container, prefix + index).AddComponent<WheelCollider>();
                }

                Configure(collider, specs[index], container, settings, spring, damper);
                collider.gameObject.name = prefix + index;
                (created || !updating ? result.Created : result.Updated).Add(WheelsName + "/" + collider.gameObject.name);
                colliders[index] = collider;
            }

            for (int index = specs.Count; index < existing.Length; index++)
            {
                if (existing[index] != null && existing[index].transform.IsChildOf(container))
                {
                    result.Removed.Add(WheelsName + "/" + existing[index].gameObject.name);
                    Object.DestroyImmediate(existing[index].gameObject);
                }
            }

            return colliders;
        }

        private static void Configure(WheelCollider collider, WheelSpec spec, Transform container, YakimaTankSetupSettings settings, float spring, float damper)
        {
            container.localPosition = Vector3.zero;
            container.localRotation = Quaternion.identity;
            container.localScale = Vector3.one;
            Transform transform = collider.transform;
            transform.SetParent(container, false);
            transform.localPosition = YakimaTankSetupMath.WheelColliderPosition(spec.Center, settings.suspensionDistance, WheelTargetPosition);
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            collider.radius = spec.Radius;
            collider.mass = settings.wheelMass;
            collider.suspensionDistance = settings.suspensionDistance;
            collider.suspensionSpring = new JointSpring { spring = spring, damper = damper, targetPosition = WheelTargetPosition };
            WheelFrictionCurve forward = collider.forwardFriction;
            forward.stiffness = settings.forwardFrictionStiffness;
            collider.forwardFriction = forward;
            WheelFrictionCurve sideways = collider.sidewaysFriction;
            sideways.stiffness = settings.sidewaysFrictionStiffness;
            collider.sidewaysFriction = sideways;
        }

        private static void Inspect(YakimaTankSetupSettings settings, GameObject root)
        {
            Require(root.GetComponentsInChildren<Rigidbody>(true).Length == 1, "Rigidbody はルートに 1 つだけのはずです。");
            Require(root.GetComponentsInChildren<UdonBehaviour>(true)
                .Count(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankVehicle)) == 1, "車両の Udon は 1 つだけのはずです。");
            Require(root.transform.Cast<Transform>().Count(t => t.name == ModelName && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) == 1, "元のモデルの入れ子の Prefab は 1 つだけのはずです。");
            YakimaTankVehicle vehicle = FindVehicleProxy(root);
            Require(vehicle.leftWheels.Length == settings.leftWheels.Length && vehicle.rightWheels.Length == settings.rightWheels.Length, "WheelCollider の数が設定と合いません。");
            Require(vehicle.leftWheels.Concat(vehicle.rightWheels).All(w => w != null && w.transform.IsChildOf(root.transform)), "WheelCollider の参照が欠けています。");
            Require(root.GetComponentsInChildren<WheelCollider>(true).Length == settings.leftWheels.Length + settings.rightWheels.Length, "参照されていない WheelCollider があります。");
        }

        // UdonSharp の値は裏の UdonBehaviour に写して初めて Prefab に残るので、保存した Prefab を読み直して確かめる。
        private static void InspectSaved(YakimaTankSetupSettings settings, string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(prefab != null, "保存した Prefab を読めません: " + path);
            UdonBehaviour udon = prefab.GetComponents<UdonBehaviour>()
                .FirstOrDefault(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankVehicle));
            Require(udon != null && udon.programSource != null, "保存した Prefab に車両の Udon がありません。");
            Require(!TryGetSyncType(out Networking.SyncType sync) || udon.SyncMethod == sync,
                "保存した Prefab の車両の Udon の同期方式が、スクリプトの指定と違います。");
            Require(udon.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicle.mass), out float mass) && Mathf.Approximately(mass, settings.mass),
                "保存した Prefab の Udon に質量が入っていません。");
            Require(udon.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicle.leftWheels), out WheelCollider[] left) &&
                    left != null && left.Length == settings.leftWheels.Length && left.All(w => w != null),
                "保存した Prefab の Udon に左の転輪の参照が入っていません。");
            Require(udon.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicle.rightWheels), out WheelCollider[] right) &&
                    right != null && right.Length == settings.rightWheels.Length && right.All(w => w != null),
                "保存した Prefab の Udon に右の転輪の参照が入っていません。");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
