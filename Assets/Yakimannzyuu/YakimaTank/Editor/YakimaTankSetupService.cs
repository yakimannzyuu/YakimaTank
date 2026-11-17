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
using VRCStation = VRC.SDK3.Components.VRCStation;

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
        public const string ControlsName = "Controls";
        public const string BoardingName = "Boarding";
        public const string SeatName = "Seat";
        public const string SeatAnchorName = "SeatAnchor";
        public const string WeaponName = "Weapon";
        public const string MuzzleName = "YakimaTankMuzzle";

        /// <summary>砲身の下で、名前にこれを含む GameObject を、元のモデルの砲口の印として探す（大文字と小文字は区別しない）。</summary>
        public const string MuzzleMarker = "Muzzle";
        public const string ShellsName = "Shells";
        public const string HitboxName = "Hitbox";
        public const string TurretHitboxName = "TurretHitbox";
        public const string ShellMaterialName = "YakimaTankShell.mat";

        /// <summary>使い回す砲弾の数。装填の時間（既定 3 秒）と寿命（既定 3 秒）なら、自分の発射と受けた発射が重なっても足りる。</summary>
        public const int ShellCount = 4;

        /// <summary>見た目の砲弾の球の直径（m）。</summary>
        public const float ShellSize = 0.2f;
        public const string BoardingText = "Ride";
        public const float WheelTargetPosition = 0.5f;

        private const int DefaultLayer = 0;

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
        internal static void DeferCleanup(Action cleanup)
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

            // 座席はルートの下に置く。0.4.0 までは元のモデルの砲塔の子だったので、先にルートへ移しておく。
            // モデルを差し替えると古いモデルと一緒に消えるため。シーンからの参照と中のオブジェクトを保つ。
            YakimaTankSeat existingSeat = FindProxy<YakimaTankSeat>(root, true);
            if (existingSeat != null)
            {
                existingSeat.transform.SetParent(root.transform, true);
            }

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

            Transform turret = settings.turret == null ? null : Resolve(settings.turret);
            Transform barrel = settings.barrel == null ? null : Resolve(settings.barrel);

            // 子（砲塔・砲身）を含めると、砲身の分だけ前に見えない箱が張り出すので、車体自身のメッシュから作る。
            // 車体自身にメッシュがなければ子から求めるが、そのときも砲塔と砲身の階層は除く。
            if (!YakimaTankBounds.TryGetOwnBounds(Resolve(settings.body), root.transform, out Bounds bodyBounds, turret, barrel))
            {
                throw new InvalidOperationException("砲塔と砲身を除くと車体にメッシュがないので、車体のコライダーの大きさを決められません。");
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

            // 同期はルート（所有権の単位）に置く。車両の Udon も同じ GameObject にあるので、どちらも Manual にそろえてある。
            YakimaTankVehicleSync sync = FindOrAdd<YakimaTankVehicleSync>(root, out bool syncCreated);

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

            // 戦車とプレイヤーの当たりを外す。プレイヤーは戦車をすり抜け、戦車はプレイヤーに乗り上げない。壁・地面とは当たる。
            int walkthrough = YakimaTankSetupValidator.FindLayer(YakimaTankSetupValidator.WalkthroughLayer);
            if (walkthrough < 0)
            {
                throw new InvalidOperationException($"レイヤー {YakimaTankSetupValidator.WalkthroughLayer} がありません。");
            }

            foreach (Transform solid in new[] { colliders, bodyColliderTransform, wheels }.Concat(leftColliders.Concat(rightColliders).Select(w => w.transform)))
            {
                solid.gameObject.layer = walkthrough;
            }

            DisableSolidModelColliders(modelInstance, Record);

            // 入力と共通操作値はルートに置かない。車両の Udon を同期させるとき（0.3.0 で Manual）に、
            // 同期しないこれらと同じ GameObject で同期の方式が混ざり、VRChat の同期が正しく働かないことがあるため。
            Transform controls = EnsureChild(root.transform, ControlsName, out bool controlsCreated);
            YakimaTankControlState control = FindOrAdd<YakimaTankControlState>(controls.gameObject, out bool controlCreated);
            YakimaTankDesktopInput desktop = FindOrAdd<YakimaTankDesktopInput>(controls.gameObject, out bool desktopCreated);
            YakimaTankVRInput vr = FindOrAdd<YakimaTankVRInput>(controls.gameObject, out bool vrCreated);
            desktop.control = control;
            desktop.vehicleBody = root.transform;
            vr.control = control;
            vr.vehicleBody = root.transform;
            YakimaTankRuntimeValues.WriteToProxy(settings, vr);
            Record(ControlsName, controlsCreated || controlCreated || desktopCreated || vrCreated);

            // 乗車の判定は車体のコライダーを囲み、高さはモデル全体まで。Interact できるように、プレイヤーの当たりが残る Default のトリガーにする。
            if (!YakimaTankBounds.TryGetLocalBounds(modelInstance.transform, root.transform, out Bounds modelBounds))
            {
                throw new InvalidOperationException("元のモデルにメッシュがないので、乗車の判定の大きさを決められません。");
            }

            Transform boardingTransform = EnsureChild(root.transform, BoardingName, out bool boardingCreated);
            boardingTransform.gameObject.layer = DefaultLayer;
            BoxCollider area = boardingTransform.GetComponent<BoxCollider>();
            if (area == null)
            {
                area = boardingTransform.gameObject.AddComponent<BoxCollider>();
            }

            area.isTrigger = true;
            area.enabled = true;
            Bounds areaBounds = YakimaTankSetupMath.BoardingAreaBounds(box, modelBounds);
            area.center = areaBounds.center;
            area.size = areaBounds.size;
            YakimaTankBoarding boarding = FindOrAdd<YakimaTankBoarding>(boardingTransform.gameObject, out bool boardingUdonCreated);
            Record(BoardingName, boardingCreated || boardingUdonCreated);

            // 座席は車体（ルート）の下に置き、砲塔の下の位置の参照に付いて行かせる（仕様書 0.5.0 の 1.1）。
            // 砲塔の子にしない。砲塔と一緒に回ると、車体から見た頭と手の入力に回転が返り、回り続けるため。
            bool seatCreated = existingSeat == null;
            Transform seatTransform = seatCreated ? CreateChild(root.transform, SeatName).transform : existingSeat.transform;
            seatTransform.SetParent(root.transform, false);
            seatTransform.name = SeatName;
            seatTransform.localRotation = Quaternion.identity;
            seatTransform.localScale = Vector3.one;
            Transform anchor = EnsureChild(turret, SeatAnchorName, out bool anchorCreated);
            anchor.localPosition = SeatBasePosition(turret, root.transform, barrel);
            anchor.rotation = root.transform.rotation;
            anchor.localScale = Vector3.one;
            Record(SeatAnchorName, anchorCreated);
            VRCStation station = seatTransform.GetComponent<VRCStation>();
            if (station == null)
            {
                station = seatTransform.gameObject.AddComponent<VRCStation>();
            }

            station.PlayerMobility = VRC.SDKBase.VRCStation.Mobility.ImmobilizeForVehicle;
            station.seated = true;
            // 標準の降り方のままだと、運転の W・A・S・D（VR はスティック）でも降りてしまう。降りる入力は座席の Udon が受ける。
            station.disableStationExit = true;
            station.stationEnterPlayerLocation = seatTransform;
            station.stationExitPlayerLocation = seatTransform;
            YakimaTankSeat seat = existingSeat != null ? existingSeat : FindOrAdd<YakimaTankSeat>(seatTransform.gameObject, out _);
            seat.station = station;
            seat.boarding = boarding;
            seat.vehicle = vehicle;
            seat.control = control;
            seat.desktopInput = desktop;
            seat.vrInput = vr;
            seat.seatAnchor = anchor;
            YakimaTankRuntimeValues.WriteToProxy(settings, seat);
            seatTransform.position = anchor.position + anchor.rotation * settings.seatOffset;
            Record(SeatName, seatCreated);

            boarding.seat = seat;
            boarding.area = area;

            YakimaTankRuntimeValues.WriteToProxy(settings, vehicle);
            vehicle.baseCenterOfMass = new Vector3(box.center.x, wheelCenterHeight, box.center.z);
            vehicle.body = body;
            vehicle.leftWheels = leftColliders;
            vehicle.rightWheels = rightColliders;
            vehicle.applyOnStart = true;
            vehicle.control = control;
            vehicle.sync = sync;
            Record(nameof(YakimaTankVehicle), vehicleCreated);

            YakimaTankRuntimeValues.WriteToProxy(settings, sync);
            sync.vehicle = vehicle;
            sync.seat = seat;
            sync.body = body;
            sync.physicsColliders = new Collider[] { boxCollider }.Concat(leftColliders).Concat(rightColliders).ToArray();
            sync.bodyCollider = boxCollider;
            seat.sync = sync;
            Record(nameof(YakimaTankVehicleSync), syncCreated);

            YakimaTankWeaponSync weapon = BuildWeapon(settings, root.transform, turret, barrel, sync, seat, control, Record, out YakimaTankShell[] shells);
            seat.weapon = weapon;
            YakimaTankHitbox[] hitboxes = BuildHitboxes(root.transform, turret, barrel, box, modelBounds, sync, Record);

            foreach (UdonSharpBehaviour proxy in new UdonSharpBehaviour[] { vehicle, sync, control, desktop, vr, boarding, seat, weapon }.Concat(shells).Concat(hitboxes))
            {
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);
                ApplySyncMethod(proxy);
            }

            UdonSharpEditorUtility.GetBackingUdonBehaviour(boarding).interactText = BoardingText;
        }

        /// <summary>
        /// 武器の領域（ルートの子の Weapon）。所有権を車体と分けるため、ルートとは別の GameObject に置く（仕様書 0.5.0 の 1.1）。
        /// 砲塔の左右の軸と砲身の上下の軸は、車体の上と右の向きを、それぞれの親から見た向きにして解く（元のモデルは変えない）。
        /// </summary>
        private static YakimaTankWeaponSync BuildWeapon(YakimaTankSetupSettings settings, Transform root, Transform turret, Transform barrel,
            YakimaTankVehicleSync sync, YakimaTankSeat seat, YakimaTankControlState control, Action<string, bool> record, out YakimaTankShell[] shells)
        {
            Transform weaponTransform = EnsureChild(root, WeaponName, out bool weaponObjectCreated);
            weaponTransform.localPosition = Vector3.zero;
            weaponTransform.localRotation = Quaternion.identity;
            weaponTransform.localScale = Vector3.one;
            YakimaTankWeaponSync weapon = FindOrAdd<YakimaTankWeaponSync>(weaponTransform.gameObject, out bool weaponCreated);
            record(WeaponName, weaponObjectCreated || weaponCreated);

            Transform muzzle = EnsureChild(barrel, MuzzleName, out bool muzzleCreated);
            muzzle.position = MuzzlePosition(barrel, root);
            muzzle.rotation = root.rotation;
            muzzle.localScale = Vector3.one;
            record(MuzzleName, muzzleCreated);

            weapon.vehicleSync = sync;
            weapon.seat = seat;
            weapon.control = control;
            weapon.turret = turret;
            weapon.barrel = barrel;
            weapon.muzzle = muzzle;
            weapon.turretAxis = turret.parent.InverseTransformDirection(root.up).normalized;
            weapon.turretBaseRotation = turret.localRotation;
            weapon.barrelAxis = barrel.parent.InverseTransformDirection(root.right).normalized;
            weapon.barrelBaseRotation = barrel.localRotation;
            weapon.shellMask = ShellMask();
            YakimaTankRuntimeValues.WriteToProxy(settings, weapon);

            Transform container = EnsureChild(weaponTransform, ShellsName, out _);
            Material material = EnsureShellMaterial(settings.outputPrefabPath);
            Mesh sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            shells = new YakimaTankShell[ShellCount];
            for (int index = 0; index < ShellCount; index++)
            {
                Transform shellTransform = EnsureChild(container, "Shell" + index, out bool shellCreated);
                shellTransform.localPosition = Vector3.zero;
                shellTransform.localRotation = Quaternion.identity;
                shellTransform.localScale = Vector3.one * ShellSize;
                MeshFilter filter = shellTransform.GetComponent<MeshFilter>();
                if (filter == null)
                {
                    filter = shellTransform.gameObject.AddComponent<MeshFilter>();
                }

                filter.sharedMesh = sphere;
                MeshRenderer renderer = shellTransform.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    renderer = shellTransform.gameObject.AddComponent<MeshRenderer>();
                }

                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.enabled = false;
                YakimaTankShell shell = FindOrAdd<YakimaTankShell>(shellTransform.gameObject, out bool shellUdonCreated);
                shell.weapon = weapon;
                shell.shellRenderer = renderer;
                shells[index] = shell;
                record(WeaponName + "/" + ShellsName + "/" + shellTransform.name, shellCreated || shellUdonCreated);
            }

            weapon.shells = shells;
            return weapon;
        }

        /// <summary>
        /// 元のモデルの砲口の印（砲身の下で、名前に Muzzle を含む GameObject）。生成が置く砲口は除く。なければ null。
        /// ボーンで砲身を回すモデルは、メッシュが砲身の下にないので、この印で砲口を決める（仕様書 0.5.0 の 1.9）。
        /// </summary>
        public static Transform FindMuzzleMarker(Transform barrel)
        {
            return barrel == null
                ? null
                : barrel.GetComponentsInChildren<Transform>(true).FirstOrDefault(t =>
                    t != barrel && t.name != MuzzleName && t.name.IndexOf(MuzzleMarker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>砲口の位置。砲口の印があればその位置、なければ砲身のメッシュの範囲の前の端（車体の前の向き）。どちらもなければ失敗。</summary>
        private static Vector3 MuzzlePosition(Transform barrel, Transform root)
        {
            Transform marker = FindMuzzleMarker(barrel);
            if (marker != null)
            {
                return marker.position;
            }

            if (!YakimaTankBounds.TryGetLocalBounds(barrel, root, out Bounds bounds))
            {
                throw new InvalidOperationException("砲身にメッシュも砲口の印もないので、砲口の位置を決められません。");
            }

            return root.TransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.max.z));
        }

        /// <summary>
        /// 命中用のトリガー（仕様書 0.5.0 の 1.7）。車体の箱（ルートの下、車体のコライダーと同じ大きさ）と、砲塔の箱（砲塔の下、砲身を除く砲塔のメッシュの範囲）。
        /// 乗車の判定の箱は使い回さない。車体の幅のまま砲塔の高さまである箱なので、砲塔の横の空間に当たるため。
        /// ただし砲塔の下にメッシュがない（ボーンで回す）モデルは、砲塔の形が分からないので、車体の箱の前後・左右のまま、
        /// 高さをモデル全体の上端まで伸ばした箱をルートの下に置く（回さない）。
        /// </summary>
        private static YakimaTankHitbox[] BuildHitboxes(Transform root, Transform turret, Transform barrel, Bounds bodyBox, Bounds modelBounds, YakimaTankVehicleSync sync,
            Action<string, bool> record)
        {
            YakimaTankHitbox turretHitbox;
            if (YakimaTankBounds.TryGetOwnBounds(turret, turret, out Bounds turretBounds, barrel))
            {
                RemoveChild(root, TurretHitboxName);
                turretHitbox = Hitbox(turret, TurretHitboxName, turretBounds, sync, record);
            }
            else
            {
                RemoveChild(turret, TurretHitboxName);
                turretHitbox = Hitbox(root, TurretHitboxName, YakimaTankSetupMath.UpperHitboxBounds(bodyBox, modelBounds), sync, record);
            }

            return new[] { Hitbox(root, HitboxName, bodyBox, sync, record), turretHitbox };
        }

        /// <summary>砲塔の箱の置き場所が変わったとき（モデルを差し替えたとき）に、前の箱を残さない。</summary>
        private static void RemoveChild(Transform parent, string name)
        {
            foreach (Transform child in parent.Cast<Transform>().Where(t => t.name == name && !PrefabUtility.IsPartOfPrefabInstance(t)).ToList())
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        private static YakimaTankHitbox Hitbox(Transform parent, string name, Bounds bounds, YakimaTankVehicleSync sync, Action<string, bool> record)
        {
            Transform hitboxTransform = EnsureChild(parent, name, out bool created);
            hitboxTransform.localPosition = Vector3.zero;
            hitboxTransform.localRotation = Quaternion.identity;
            hitboxTransform.localScale = Vector3.one;
            hitboxTransform.gameObject.layer = DefaultLayer;
            BoxCollider collider = hitboxTransform.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = hitboxTransform.gameObject.AddComponent<BoxCollider>();
            }

            collider.isTrigger = true;
            collider.enabled = true;
            collider.center = bounds.center;
            collider.size = bounds.size;
            YakimaTankHitbox hitbox = FindOrAdd<YakimaTankHitbox>(hitboxTransform.gameObject, out bool udonCreated);
            hitbox.vehicle = sync;
            record(name, created || udonCreated);
            return hitbox;
        }

        /// <summary>砲弾の光線のレイヤー。車両の物理の形（Walkthrough）とプレイヤーは見ない（画面によって有効・無効が違い、当たり方が変わるため）。</summary>
        internal static int ShellMask()
        {
            int mask = Physics.DefaultRaycastLayers;
            foreach (string name in new[] { YakimaTankSetupValidator.WalkthroughLayer, "Player", "PlayerLocal", "MirrorReflection" })
            {
                int layer = YakimaTankSetupValidator.FindLayer(name);
                if (layer >= 0)
                {
                    mask &= ~(1 << layer);
                }
            }

            return mask;
        }

        /// <summary>見た目の砲弾の材質。パッケージは書けないことがある（VCC や git URL で入れると読むだけ）ので、出力先のフォルダに作る。</summary>
        private static Material EnsureShellMaterial(string outputPrefabPath)
        {
            string folder = Path.GetDirectoryName(outputPrefabPath)?.Replace(Path.DirectorySeparatorChar, '/');
            string path = folder + "/" + ShellMaterialName;
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            EnsureFolder(folder);
            material = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.75f, 0.2f) };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// 座席の自動の位置。砲塔自身のメッシュの範囲の上面の中央を、親（砲塔）の座標で返す。
        /// 子（砲身）を含めると前へずれるので含めない。砲塔自身にメッシュがなければ、砲身の階層を除いた子から求める。
        /// メッシュがなければ親の原点。
        /// </summary>
        private static Vector3 SeatBasePosition(Transform seatParent, Transform root, Transform barrel)
        {
            Vector3 top = YakimaTankBounds.TryGetOwnBounds(seatParent, root, out Bounds bounds, barrel)
                ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
                : root.InverseTransformPoint(seatParent.position);
            return seatParent.InverseTransformPoint(root.TransformPoint(top));
        }

        /// <summary>
        /// 元のモデルのトリガーでないコライダーを、生成 Prefab の中で無効にする（元のアセットは変えない）。
        /// Walkthrough にそろえる案は採らない。車体の物理に残って質量の分布が変わり、凸でない MeshCollider は動く Rigidbody の下で使えない。
        /// </summary>
        private static void DisableSolidModelColliders(GameObject modelInstance, Action<string, bool> record)
        {
            foreach (Collider collider in modelInstance.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger))
            {
                collider.enabled = false;
                record($"{ModelName}/{YakimaTankModelPaths.GetPath(collider.transform, modelInstance.transform)} の {collider.GetType().Name}（無効）", false);
            }
        }

        private static T FindOrAdd<T>(GameObject owner, out bool created) where T : UdonSharpBehaviour
        {
            T found = FindProxy<T>(owner, false);
            created = found == null;
            return created ? UdonSharpUndo.AddComponent<T>(owner) : found;
        }

        private static T FindProxy<T>(GameObject owner, bool includeChildren) where T : UdonSharpBehaviour
        {
            UdonBehaviour[] behaviours = (includeChildren ? owner.GetComponentsInChildren<UdonBehaviour>(true) : owner.GetComponents<UdonBehaviour>())
                .Where(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(T))
                .ToArray();
            if (behaviours.Length > 1)
            {
                throw new InvalidOperationException($"{typeof(T).Name} の Udon が 2 つ以上あります。生成 Prefab を手で変えていないか確かめてください。");
            }

            return behaviours.Length == 0 ? null : (T)UdonSharpEditorUtility.GetProxyBehaviour(behaviours[0]);
        }

        /// <summary>
        /// UdonSharp は同期方式をシーンのオブジェクトでしか直さない。Prefab の側で合わせておかないと、
        /// シーンを開くたびにインスタンスの側で直され、手の変更と同じ上書きが残る。
        /// </summary>
        private static void ApplySyncMethod(UdonSharpBehaviour proxy)
        {
            UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            if (TryGetSyncType(proxy.GetType(), out Networking.SyncType sync) && udon.SyncMethod != sync)
            {
                udon.SyncMethod = sync;
            }
        }

        internal static bool TryGetSyncType(Type type, out Networking.SyncType sync)
        {
            var attribute = type == null ? null : (UdonBehaviourSyncModeAttribute)Attribute.GetCustomAttribute(type, typeof(UdonBehaviourSyncModeAttribute));
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
                case BehaviourSyncMode.NoVariableSync:
                    // U# は、同じ GameObject に Manual がなければ NoVariableSync を Continuous にそろえる（変数は送らない）。
                    sync = Networking.SyncType.Continuous;
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
            return FindProxy<YakimaTankVehicle>(root, false);
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

        /// <summary>生成が足す U# の部品と、生成 Prefab の中の数。</summary>
        private static readonly KeyValuePair<Type, int>[] GeneratedBehaviours =
        {
            new KeyValuePair<Type, int>(typeof(YakimaTankVehicle), 1),
            new KeyValuePair<Type, int>(typeof(YakimaTankVehicleSync), 1),
            new KeyValuePair<Type, int>(typeof(YakimaTankControlState), 1),
            new KeyValuePair<Type, int>(typeof(YakimaTankDesktopInput), 1),
            new KeyValuePair<Type, int>(typeof(YakimaTankVRInput), 1),
            new KeyValuePair<Type, int>(typeof(YakimaTankBoarding), 1),
            new KeyValuePair<Type, int>(typeof(YakimaTankSeat), 1),
            new KeyValuePair<Type, int>(typeof(YakimaTankWeaponSync), 1),
            new KeyValuePair<Type, int>(typeof(YakimaTankShell), ShellCount),
            new KeyValuePair<Type, int>(typeof(YakimaTankHitbox), 2)
        };

        private static void Inspect(YakimaTankSetupSettings settings, GameObject root)
        {
            Require(root.GetComponentsInChildren<Rigidbody>(true).Length == 1, "Rigidbody はルートに 1 つだけのはずです。");
            foreach (KeyValuePair<Type, int> generated in GeneratedBehaviours)
            {
                Require(root.GetComponentsInChildren<UdonBehaviour>(true).Count(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == generated.Key) == generated.Value,
                    $"{generated.Key.Name} の Udon は {generated.Value} つのはずです。");
            }

            Require(root.transform.Cast<Transform>().Count(t => t.name == ModelName && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)) == 1, "元のモデルの入れ子の Prefab は 1 つだけのはずです。");
            YakimaTankVehicle vehicle = FindVehicleProxy(root);
            Require(vehicle.leftWheels.Length == settings.leftWheels.Length && vehicle.rightWheels.Length == settings.rightWheels.Length, "WheelCollider の数が設定と合いません。");
            Require(vehicle.leftWheels.Concat(vehicle.rightWheels).All(w => w != null && w.transform.IsChildOf(root.transform)), "WheelCollider の参照が欠けています。");
            Require(root.GetComponentsInChildren<WheelCollider>(true).Length == settings.leftWheels.Length + settings.rightWheels.Length, "参照されていない WheelCollider があります。");

            Require(root.GetComponentsInChildren<VRCStation>(true).Length == 1, "座席（VRCStation）は 1 つだけのはずです。");
            YakimaTankSeat seat = FindProxy<YakimaTankSeat>(root, true);
            Require(seat.station != null && seat.boarding != null && seat.vehicle == vehicle && seat.control != null && seat.desktopInput != null && seat.vrInput != null,
                "座席の参照が欠けています。");
            Require(vehicle.control != null && vehicle.control == seat.control, "車両の共通操作値の参照が欠けています。");
            Require(seat.transform.parent == root.transform, "座席がルートの下にありません。");
            Transform turret = settings.turret == null ? null : seat.seatAnchor == null ? null : seat.seatAnchor.parent;
            Require(turret != null && turret.name == settings.turret.name && seat.seatAnchor.name == SeatAnchorName, "座席の位置の参照が砲塔の下にありません。");
            YakimaTankVehicleSync sync = FindProxy<YakimaTankVehicleSync>(root, false);
            Require(sync != null && sync.vehicle == vehicle && sync.seat == seat && sync.body == root.GetComponent<Rigidbody>() && vehicle.sync == sync && seat.sync == sync,
                "同期の Udon の参照が欠けています。");
            Collider box = root.transform.Find(CollidersName + "/" + BodyColliderName)?.GetComponent<BoxCollider>();
            Require(sync.physicsColliders != null && sync.physicsColliders.Length == 1 + vehicle.leftWheels.Length + vehicle.rightWheels.Length &&
                    sync.physicsColliders.Contains(box) && vehicle.leftWheels.Concat(vehicle.rightWheels).All(w => sync.physicsColliders.Contains(w)),
                "同期の Udon が、車体のコライダーと WheelCollider をそろえて持っていません。");
            Require(box != null && sync.bodyCollider == box, "同期の Udon の、接触に使う車体のコライダーの参照が欠けています。");
            Require(seat.boarding.seat == seat && seat.boarding.area != null && seat.boarding.area.isTrigger && seat.boarding.area.gameObject.layer == DefaultLayer,
                "乗車の判定が、座席を指す Default のトリガーではありません。");

            YakimaTankWeaponSync weapon = FindProxy<YakimaTankWeaponSync>(root, true);
            Require(weapon.transform.parent == root.transform && weapon.name == WeaponName, "武器の同期の Udon が、ルートの子の Weapon にありません。");
            Require(weapon.vehicleSync == sync && weapon.seat == seat && weapon.control == seat.control && weapon.turret == turret && seat.weapon == weapon &&
                    weapon.barrel != null && weapon.barrel.IsChildOf(turret) && weapon.muzzle != null && weapon.muzzle.parent == weapon.barrel,
                "武器の同期の Udon の参照が欠けています。");
            Require(weapon.shells != null && weapon.shells.Length == ShellCount && weapon.shells.All(shell => shell != null && shell.weapon == weapon && shell.shellRenderer != null),
                "武器の同期の Udon が、砲弾をそろえて持っていません。");
            YakimaTankHitbox[] hitboxes = root.GetComponentsInChildren<UdonBehaviour>(true)
                .Where(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankHitbox))
                .Select(b => (YakimaTankHitbox)UdonSharpEditorUtility.GetProxyBehaviour(b))
                .ToArray();
            Require(hitboxes.All(hitbox => hitbox.vehicle == sync && hitbox.GetComponent<BoxCollider>() is BoxCollider area && area.isTrigger && area.enabled &&
                                           hitbox.gameObject.layer == DefaultLayer) &&
                    hitboxes.Any(hitbox => hitbox.transform.parent == root.transform && hitbox.name == HitboxName) &&
                    hitboxes.Any(hitbox => (hitbox.transform.parent == turret || hitbox.transform.parent == root.transform) && hitbox.name == TurretHitboxName),
                "命中用のトリガーが、車体と砲塔の Default のトリガーで車体の同期の Udon を指していません。");

            int walkthrough = YakimaTankSetupValidator.FindLayer(YakimaTankSetupValidator.WalkthroughLayer);
            Transform bodyCollider = root.transform.Find(CollidersName + "/" + BodyColliderName);
            Require(bodyCollider != null && bodyCollider.gameObject.layer == walkthrough && vehicle.leftWheels.Concat(vehicle.rightWheels).All(w => w.gameObject.layer == walkthrough),
                $"車体のコライダーと WheelCollider が {YakimaTankSetupValidator.WalkthroughLayer} にありません。");
            Transform model = root.transform.Find(ModelName);
            Require(model.GetComponentsInChildren<Collider>(true).All(c => c.isTrigger || !c.enabled), "元のモデルのトリガーでないコライダーが有効のままです。");
        }

        // UdonSharp の値は裏の UdonBehaviour に写して初めて Prefab に残るので、保存した Prefab を読み直して確かめる。
        private static void InspectSaved(YakimaTankSetupSettings settings, string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(prefab != null, "保存した Prefab を読めません: " + path);
            foreach (Type type in GeneratedBehaviours.Select(generated => generated.Key))
            {
                UdonBehaviour saved = SavedUdon(prefab, type);
                Require(saved != null && saved.programSource != null, $"保存した Prefab に {type.Name} の Udon がありません。");
                Require(!TryGetSyncType(type, out Networking.SyncType expected) || saved.SyncMethod == expected,
                    $"保存した Prefab の {type.Name} の Udon の同期方式が、スクリプトの指定と違います。");
            }

            UdonBehaviour seat = SavedUdon(prefab, typeof(YakimaTankSeat));
            foreach (string name in new[]
                     {
                         nameof(YakimaTankSeat.station), nameof(YakimaTankSeat.boarding), nameof(YakimaTankSeat.vehicle),
                         nameof(YakimaTankSeat.control), nameof(YakimaTankSeat.desktopInput), nameof(YakimaTankSeat.vrInput)
                     })
            {
                Require(HasReference(seat, name), $"保存した Prefab の座席の Udon に {name} の参照が入っていません。");
            }

            Require(HasReference(seat, nameof(YakimaTankSeat.sync)), "保存した Prefab の座席の Udon に同期の Udon の参照が入っていません。");
            Require(HasReference(seat, nameof(YakimaTankSeat.weapon)) && HasReference(seat, nameof(YakimaTankSeat.seatAnchor)),
                "保存した Prefab の座席の Udon に武器の同期の Udon か位置の参照が入っていません。");
            UdonBehaviour savedWeapon = SavedUdon(prefab, typeof(YakimaTankWeaponSync));
            foreach (string name in new[]
                     {
                         nameof(YakimaTankWeaponSync.vehicleSync), nameof(YakimaTankWeaponSync.seat), nameof(YakimaTankWeaponSync.control),
                         nameof(YakimaTankWeaponSync.turret), nameof(YakimaTankWeaponSync.barrel), nameof(YakimaTankWeaponSync.muzzle)
                     })
            {
                Require(HasReference(savedWeapon, name), $"保存した Prefab の武器の同期の Udon に {name} の参照が入っていません。");
            }

            Require(savedWeapon.publicVariables.TryGetVariableValue(nameof(YakimaTankWeaponSync.shells), out object savedShells) &&
                    savedShells is Array shellArray && shellArray.Length == ShellCount && shellArray.Cast<object>().All(shell => shell is Object reference && reference != null),
                "保存した Prefab の武器の同期の Udon に砲弾の参照が入っていません。");
            Require(savedWeapon.publicVariables.TryGetVariableValue(nameof(YakimaTankWeaponSync.reloadTime), out float reload) && Mathf.Approximately(reload, settings.reloadTime),
                "保存した Prefab の武器の同期の Udon に装填の時間が入っていません。");
            UdonBehaviour savedSync = SavedUdon(prefab, typeof(YakimaTankVehicleSync));
            foreach (string name in new[]
                     {
                         nameof(YakimaTankVehicleSync.vehicle), nameof(YakimaTankVehicleSync.seat), nameof(YakimaTankVehicleSync.body),
                         nameof(YakimaTankVehicleSync.bodyCollider)
                     })
            {
                Require(HasReference(savedSync, name), $"保存した Prefab の同期の Udon に {name} の参照が入っていません。");
            }

            Require(savedSync.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicleSync.physicsColliders), out Collider[] shapes) &&
                    shapes != null && shapes.Length == 1 + settings.leftWheels.Length + settings.rightWheels.Length && shapes.All(c => c != null),
                "保存した Prefab の同期の Udon に物理の形の参照が入っていません。");
            Require(savedSync.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicleSync.sendInterval), out float interval) && Mathf.Approximately(interval, settings.sendInterval),
                "保存した Prefab の同期の Udon に送る間隔が入っていません。");
            Require(savedSync.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicleSync.contactMaxPushAcceleration), out float maxPush) &&
                    Mathf.Approximately(maxPush, settings.contactMaxPushAcceleration),
                "保存した Prefab の同期の Udon に接触の値が入っていません。");

            UdonBehaviour boarding = SavedUdon(prefab, typeof(YakimaTankBoarding));
            Require(HasReference(boarding, nameof(YakimaTankBoarding.seat)) && HasReference(boarding, nameof(YakimaTankBoarding.area)),
                "保存した Prefab の乗車の判定の Udon に参照が入っていません。");

            UdonBehaviour udon = SavedUdon(prefab, typeof(YakimaTankVehicle));
            Require(HasReference(udon, nameof(YakimaTankVehicle.control)) && HasReference(udon, nameof(YakimaTankVehicle.sync)), "保存した Prefab の車両の Udon に共通操作値か同期の Udon の参照が入っていません。");
            Require(udon.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicle.mass), out float mass) && Mathf.Approximately(mass, settings.mass),
                "保存した Prefab の Udon に質量が入っていません。");
            Require(udon.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicle.leftWheels), out WheelCollider[] left) &&
                    left != null && left.Length == settings.leftWheels.Length && left.All(w => w != null),
                "保存した Prefab の Udon に左の転輪の参照が入っていません。");
            Require(udon.publicVariables.TryGetVariableValue(nameof(YakimaTankVehicle.rightWheels), out WheelCollider[] right) &&
                    right != null && right.Length == settings.rightWheels.Length && right.All(w => w != null),
                "保存した Prefab の Udon に右の転輪の参照が入っていません。");
        }

        private static UdonBehaviour SavedUdon(GameObject prefab, Type type)
        {
            return prefab.GetComponentsInChildren<UdonBehaviour>(true).FirstOrDefault(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == type);
        }

        private static bool HasReference(UdonBehaviour udon, string name)
        {
            return udon != null && udon.publicVariables.TryGetVariableValue(name, out object value) && value is Object reference && reference != null;
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
