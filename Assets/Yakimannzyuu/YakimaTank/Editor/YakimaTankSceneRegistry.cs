using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDKBase.Network;
using VRC.Udon;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// シーンの車両の一覧（<see cref="YakimaTankVehicleRegistry"/>）を作り直し、実際の車両と合っているかを確かめる。
    /// 一覧は配置と同じく、管理用のルートの下に生成が管理するオブジェクトとして置く。
    /// </summary>
    public static class YakimaTankSceneRegistry
    {
        public const string RegistryName = "YakimaTankVehicleRegistry";

        /// <summary>シーンのすべての車両の同期の Udon。順番は階層の順（どのクライアントでも同じ番号になる）。</summary>
        public static List<YakimaTankVehicleSync> FindVehicles(Scene scene)
        {
            var found = new List<YakimaTankVehicleSync>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (UdonBehaviour behaviour in root.GetComponentsInChildren<UdonBehaviour>(true))
                {
                    if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(behaviour) == typeof(YakimaTankVehicleSync))
                    {
                        found.Add((YakimaTankVehicleSync)UdonSharpEditorUtility.GetProxyBehaviour(behaviour));
                    }
                }
            }

            return found;
        }

        public static List<YakimaTankVehicleRegistry> FindRegistries(Scene scene)
        {
            var found = new List<YakimaTankVehicleRegistry>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (UdonBehaviour behaviour in root.GetComponentsInChildren<UdonBehaviour>(true))
                {
                    if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(behaviour) == typeof(YakimaTankVehicleRegistry))
                    {
                        found.Add((YakimaTankVehicleRegistry)UdonSharpEditorUtility.GetProxyBehaviour(behaviour));
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// 一覧が実際の車両と合っていない所。空なら合っている。車両がなく一覧もなければ空。
        /// 一覧が古いと、同じ人が持つ車両同士が当たる（すり抜けの組み合わせを一覧から作るため）。
        /// </summary>
        public static List<string> Check(Scene scene)
        {
            var problems = new List<string>();
            List<YakimaTankVehicleSync> vehicles = FindVehicles(scene);
            List<YakimaTankVehicleRegistry> registries = FindRegistries(scene);
            if (registries.Count == 0)
            {
                if (vehicles.Count > 0)
                {
                    problems.Add("シーンの車両の一覧がありません。");
                }

                return problems;
            }

            if (registries.Count > 1)
            {
                problems.Add($"シーンの車両の一覧が {registries.Count} 個あります（1 つにしてください）。");
            }

            YakimaTankVehicleSync[] listed = registries[0].vehicles ?? new YakimaTankVehicleSync[0];
            if (listed.Any(v => v == null))
            {
                problems.Add("一覧に、消えた車両（空の項目）があります。");
            }

            foreach (YakimaTankVehicleSync duplicate in listed.Where(v => v != null).GroupBy(v => v).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                problems.Add("一覧に同じ車両が 2 回以上あります: " + PathOf(duplicate));
            }

            foreach (YakimaTankVehicleSync missing in vehicles.Where(v => !listed.Contains(v)))
            {
                problems.Add("一覧に入っていない車両があります: " + PathOf(missing));
            }

            foreach (YakimaTankVehicleSync stranger in listed.Where(v => v != null && v.gameObject.scene != scene))
            {
                problems.Add("一覧に、このシーンにない車両があります: " + PathOf(stranger));
            }

            foreach (NetworkIDPair stale in StaleNetworkIds(scene, vehicles).SelectMany(found => found.Value))
            {
                problems.Add($"車両の Network ID {stale.ID} の型が古いままです: {PathOf(stale.gameObject.transform)}");
            }

            return problems;
        }

        /// <summary>
        /// シーンの記述子ごとに、車両の中の GameObject を指し、記録した型が今の型と違う Network ID。
        /// 型が記録されていない ID は古いシーンのもので、SDK も合うとみなすので含めない。
        /// </summary>
        private static List<KeyValuePair<INetworkIDContainer, List<NetworkIDPair>>> StaleNetworkIds(Scene scene, List<YakimaTankVehicleSync> vehicles)
        {
            var found = new List<KeyValuePair<INetworkIDContainer, List<NetworkIDPair>>>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (INetworkIDContainer container in root.GetComponentsInChildren<INetworkIDContainer>(true))
                {
                    List<NetworkIDPair> stale = (container.NetworkIDCollection ?? new List<NetworkIDPair>())
                        .Where(pair => pair.gameObject != null
                                       && vehicles.Any(vehicle => pair.gameObject.transform.IsChildOf(vehicle.transform))
                                       && pair.SerializedTypeNames != null && pair.SerializedTypeNames.Count > 0
                                       && !pair.SerializedTypeNames.SequenceEqual(NetworkIDAssignment.GetSerializedTypes(pair.gameObject)))
                        .ToList();
                    if (stale.Count > 0)
                    {
                        found.Add(new KeyValuePair<INetworkIDContainer, List<NetworkIDPair>>(container, stale));
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// 車両の中を指す、型の古い Network ID を記述子から外し、外した GameObject のパスを返す。外した分は、ビルドのときに SDK が新しい ID を付ける。
        /// 0.2.0 までにビルドしたシーンでは、車両のルートに同期の Udon を足す前の型が記録されていて、SDK のビルドの前処理が型の違いで止まるため。
        /// 車両の外の ID と、型の合う ID は変えない。
        /// </summary>
        public static List<string> RemoveStaleNetworkIds(Scene scene)
        {
            var removed = new List<string>();
            foreach (KeyValuePair<INetworkIDContainer, List<NetworkIDPair>> found in StaleNetworkIds(scene, FindVehicles(scene)))
            {
                var descriptor = (Component)found.Key;
                Undo.RecordObject(descriptor, "YakimaTank の Network ID");
                found.Key.NetworkIDCollection = found.Key.NetworkIDCollection.Where(pair => !found.Value.Contains(pair)).ToList();
                removed.AddRange(found.Value.Select(pair => $"{pair.ID} {PathOf(pair.gameObject.transform)}"));
                EditorUtility.SetDirty(descriptor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(descriptor);
            }

            if (removed.Count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return removed;
        }

        /// <summary>
        /// 一覧を作り直す。管理用のルートの下になければ作る。中身が今と同じなら書かない（シーンを変えない）。
        /// 作ったか書き換えたら true。シーンの保存は呼び出し側が行う。
        /// </summary>
        public static bool Rebuild(Scene scene)
        {
            YakimaTankVehicleSync[] vehicles = FindVehicles(scene).ToArray();
            List<YakimaTankVehicleRegistry> registries = FindRegistries(scene);
            YakimaTankVehicleRegistry registry = registries.FirstOrDefault();
            bool changed = false;
            if (registry == null)
            {
                GameObject managed = scene.GetRootGameObjects().FirstOrDefault(g => g.name == YakimaTankScenePlacement.RootName);
                if (managed == null)
                {
                    managed = YakimaTankSetupService.CreateInScene(scene, YakimaTankScenePlacement.RootName);
                    Undo.RegisterCreatedObjectUndo(managed, "YakimaTank の車両の一覧");
                }

                GameObject holder = YakimaTankSetupService.CreateInScene(scene, RegistryName);
                Undo.RegisterCreatedObjectUndo(holder, "YakimaTank の車両の一覧");
                holder.transform.SetParent(managed.transform, false);
                registry = UdonSharpUndo.AddComponent<YakimaTankVehicleRegistry>(holder);
                UdonSharpEditorUtility.GetBackingUdonBehaviour(registry).SyncMethod = VRC.SDKBase.Networking.SyncType.None;
                changed = true;
            }

            if (changed || !(registry.vehicles ?? new YakimaTankVehicleSync[0]).SequenceEqual(vehicles))
            {
                Undo.RecordObject(registry, "YakimaTank の車両の一覧");
                registry.vehicles = vehicles;
                UdonSharpEditorUtility.CopyProxyToUdon(registry);
                EditorUtility.SetDirty(registry);
                EditorUtility.SetDirty(UdonSharpEditorUtility.GetBackingUdonBehaviour(registry));
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }

            return changed;
        }

        /// <summary>配置とウィンドウの更新から呼ぶ。一覧を作り直し、車両の型の古い Network ID を外す。変えた所を返す（なければ空）。</summary>
        public static List<string> Update(Scene scene)
        {
            var changes = new List<string>();
            if (Rebuild(scene))
            {
                changes.Add($"シーンの車両の一覧を作り直した: {YakimaTankScenePlacement.RootName}/{RegistryName}");
            }

            List<string> removed = RemoveStaleNetworkIds(scene);
            if (removed.Count > 0)
            {
                changes.Add("型の古い車両の Network ID を外した（ビルドで新しく付く）: " + string.Join(", ", removed));
            }

            return changes;
        }

        private static string PathOf(Component component)
        {
            var names = new List<string>();
            for (Transform node = component.transform; node != null; node = node.parent)
            {
                names.Insert(0, node.name);
            }

            return string.Join("/", names);
        }
    }

    /// <summary>
    /// VRChat SDK のビルドの前に、一覧が実際の車両と合っているかを確かめ、合わなければビルドを止める。
    /// ダイアログは出さない（自動のビルドが止まったまま待つため）。理由と直し方はログに出す。
    /// </summary>
    public sealed class YakimaTankRegistryBuildCheck : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => 0;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            return requestedBuildType != VRCSDKRequestedBuildType.Scene || Allows(SceneManager.GetActiveScene());
        }

        public static bool Allows(Scene scene)
        {
            // 型の古い Network ID は、ここで外せば SDK の前処理が新しい ID を付けるので、止めずに直す。
            List<string> removed = YakimaTankSceneRegistry.RemoveStaleNetworkIds(scene);
            if (removed.Count > 0)
            {
                Debug.LogWarning("[YakimaTank] 型の古い車両の Network ID を外しました。ビルドで新しい ID が付きます。シーンを保存してください: " + string.Join(", ", removed));
            }

            List<string> problems = YakimaTankSceneRegistry.Check(scene);
            if (problems.Count == 0)
            {
                return true;
            }

            Debug.LogError("[YakimaTank] シーンの車両の一覧が実際の車両と合わないので、ビルドを止めました。" +
                           "セットアップウィンドウの「シーンの車両一覧を更新」を押してから、もう一度ビルドしてください。\n" + string.Join("\n", problems));
            return false;
        }
    }
}
