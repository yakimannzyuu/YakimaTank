using System.IO;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;

namespace Yakimannzyuu.YakimaTank.Editor
{
    public sealed class PanelPlacementResult
    {
        public bool Succeeded;
        public string Error;

        /// <summary>この呼び出しでパネルを置いた（false なら前からあったパネルをそのまま使った）。</summary>
        public bool Created;

        public GameObject Panel;
    }

    /// <summary>
    /// デバッグ UI の Prefab と、配置 ID ごとの入れ物に置いたパネル。
    /// 開発ワールドの更新は、共通の配置処理の後にここを呼ぶ。利用者のウィンドウの「配置」では置かない。
    /// </summary>
    public static class YakimaTankDebugPanelPlacement
    {
        public const string PrefabFileName = "YakimaTankDebugPanel.prefab";

        /// <summary>
        /// パッケージの Prefab のパス。U# の Program Asset の隣に置く。パッケージを Packages/ に入れても同じ形で見つかるように、
        /// パスを直書きしない。
        /// </summary>
        public static string PrefabPath
        {
            get
            {
                string program = AssetDatabase.GetAssetPath(UdonSharpEditorUtility.GetUdonSharpProgramAsset(typeof(YakimaTankDebugPanel)));
                return string.IsNullOrEmpty(program) ? null : Path.GetDirectoryName(program)?.Replace('\\', '/') + "/" + PrefabFileName;
            }
        }

        public static GameObject LoadPrefab()
        {
            string path = PrefabPath;
            return path == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>デバッグ UI の Prefab のインスタンス（いちばん外側）か。</summary>
        public static bool IsPanel(GameObject candidate)
        {
            if (!PrefabUtility.IsOutermostPrefabInstanceRoot(candidate))
            {
                return false;
            }

            var source = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
            return source != null && PanelUdon(source) != null;
        }

        /// <summary>パネルの表示する車両が、vehicleRoot の車両か。車両の指定がなければ false。</summary>
        public static bool PointsTo(GameObject panel, GameObject vehicleRoot)
        {
            UdonBehaviour udon = PanelUdon(panel);
            UdonBehaviour vehicle = YakimaTankRuntimeValues.FindVehicle(vehicleRoot);
            if (udon == null || vehicle == null)
            {
                return false;
            }

            var proxy = (YakimaTankDebugPanel)UdonSharpEditorUtility.GetProxyBehaviour(udon);
            return proxy.vehicle != null && UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy.vehicle) == vehicle;
        }

        /// <summary>
        /// 配置した車両の入れ物に、その車両を指すパネルを 1 枚置く。あれば置かない。シーンの保存は呼び出し側が行う。
        /// </summary>
        public static PanelPlacementResult EnsurePanel(PlacementResult placement)
        {
            var result = new PanelPlacementResult();
            if (placement == null || !placement.Succeeded || placement.Instance == null)
            {
                result.Error = "車両の配置が済んでいないので、パネルを置けません。";
                return result;
            }

            Transform container = placement.Instance.transform.parent;
            foreach (Transform child in container)
            {
                if (IsPanel(child.gameObject))
                {
                    result.Panel = child.gameObject;
                    result.Succeeded = true;
                    return result;
                }
            }

            GameObject prefab = LoadPrefab();
            if (prefab == null)
            {
                result.Error = "デバッグ UI の Prefab がありません: " + PrefabPath;
                return result;
            }

            UdonBehaviour vehicle = YakimaTankRuntimeValues.FindVehicle(placement.Instance);
            var panel = (GameObject)PrefabUtility.InstantiatePrefab(prefab, container.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(panel, "YakimaTank のデバッグ UI");
            panel.transform.SetParent(container, false);
            UdonBehaviour udon = PanelUdon(panel);
            var proxy = (YakimaTankDebugPanel)UdonSharpEditorUtility.GetProxyBehaviour(udon);
            proxy.vehicle = (YakimaTankVehicle)UdonSharpEditorUtility.GetProxyBehaviour(vehicle);
            UdonSharpEditorUtility.CopyProxyToUdon(proxy);
            PrefabUtility.RecordPrefabInstancePropertyModifications(proxy);
            PrefabUtility.RecordPrefabInstancePropertyModifications(udon);

            // 実行中は付いて行く設定で置き直すが、編集中にも同じ所に見えるようにしておく。
            Transform body = placement.Instance.transform;
            panel.transform.SetPositionAndRotation(body.TransformPoint(proxy.followPosition), body.rotation * Quaternion.Euler(proxy.followRotation));
            EditorSceneManager.MarkSceneDirty(container.gameObject.scene);

            result.Panel = panel;
            result.Created = true;
            result.Succeeded = true;
            return result;
        }

        private static UdonBehaviour PanelUdon(GameObject panel)
        {
            foreach (UdonBehaviour behaviour in panel.GetComponents<UdonBehaviour>())
            {
                if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(behaviour) == typeof(YakimaTankDebugPanel))
                {
                    return behaviour;
                }
            }

            return null;
        }
    }
}
