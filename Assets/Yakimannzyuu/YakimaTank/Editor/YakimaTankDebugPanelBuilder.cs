using System;
using System.IO;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// パッケージに入れるデバッグ UI の Prefab を作る（作り直すときもここを使う）。利用者のプロジェクトでは呼ばない。
    /// 2 回目以降は名前で部品を探して使い回し、Prefab の GUID と中のオブジェクト（シーンの上書きの参照先）を保つ。
    /// </summary>
    public static class YakimaTankDebugPanelBuilder
    {
        private const float RowHeight = 0.15f;
        private const float Top = 0.6f;
        private const float LabelX = -1.15f;
        private const string BoardName = "Board";
        private const string SyncName = "Sync";

        /// <summary>同期の文字の欄の高さ（m）。7 行。</summary>
        private const float SyncHeight = 0.9f;

        /// <summary>UI の Canvas の 1 単位の大きさ（m）。文字の大きさをピクセルで決めるため。</summary>
        private const float CanvasScale = 0.005f;

        private enum RowKind
        {
            Gauge,
            Gear,
            Fire
        }

        private static readonly (string Name, string Label, RowKind Kind)[] Rows =
        {
            ("Steering", "STEER", RowKind.Gauge),
            ("LeftLever", "L LEVER", RowKind.Gauge),
            ("RightLever", "R LEVER", RowKind.Gauge),
            ("Throttle", "THROTTLE", RowKind.Gauge),
            ("DriveAxis", "DRIVE", RowKind.Gauge),
            ("Gear", "GEAR", RowKind.Gear),
            ("TurretYaw", "TURRET", RowKind.Gauge),
            ("GunPitch", "GUN", RowKind.Gauge),
            ("Fire", "FIRE", RowKind.Fire)
        };

        public static string Build()
        {
            string path = YakimaTankDebugPanelPlacement.PrefabPath;
            if (path == null)
            {
                throw new InvalidOperationException("デバッグ UI の U# の Program Asset が見つかりません。");
            }

            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            Material board = EnsureMaterial(folder, "Board", new Color(0.08f, 0.08f, 0.1f));
            Material frame = EnsureMaterial(folder, "Frame", new Color(0.3f, 0.3f, 0.34f));
            Material bar = EnsureMaterial(folder, "Bar", new Color(0.25f, 0.85f, 0.35f));
            Material tick = EnsureMaterial(folder, "Tick", Color.white);
            Material fire = EnsureMaterial(folder, "Fire", new Color(0.95f, 0.25f, 0.2f));

            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            Scene preview = exists ? default : EditorSceneManager.NewPreviewScene();
            GameObject root = exists
                ? PrefabUtility.LoadPrefabContents(path)
                : YakimaTankSetupService.CreateInScene(preview, Path.GetFileNameWithoutExtension(path));
            try
            {
                UdonBehaviour existing = YakimaTankRuntimeValues.FindBehaviour(root, typeof(YakimaTankDebugPanel));
                YakimaTankDebugPanel panel = existing != null
                    ? (YakimaTankDebugPanel)UdonSharpEditorUtility.GetProxyBehaviour(existing)
                    : UdonSharpUndo.AddComponent<YakimaTankDebugPanel>(root);

                float rowsBottom = Top - (Rows.Length - 0.5f) * RowHeight;
                float boardTop = Top + 0.5f * RowHeight;
                float boardBottom = rowsBottom - SyncHeight;
                Box(root.transform, BoardName, new Vector3(-0.3f, (boardTop + boardBottom) * 0.5f, 0.01f), new Vector3(1.9f, boardTop - boardBottom + 0.1f, 0.01f), board);
                panel.syncText = SyncText(root.transform, new Vector3(-0.3f, rowsBottom - SyncHeight * 0.5f, -0.01f), new Vector2(1.8f, SyncHeight));
                for (int index = 0; index < Rows.Length; index++)
                {
                    (string name, string label, RowKind kind) = Rows[index];
                    Transform row = Child(root.transform, name);
                    row.localPosition = new Vector3(0f, Top - index * RowHeight, 0f);
                    Text(row, "Label", label, new Vector3(LabelX, 0f, -0.01f), TextAnchor.MiddleLeft);
                    switch (kind)
                    {
                        case RowKind.Gauge:
                            Box(row, "Frame", Vector3.zero, new Vector3(YakimaTankDebugPanel.GaugeHalfWidth * 2f, 0.08f, 0.01f), frame);
                            Box(row, "Zero", new Vector3(0f, 0f, -0.006f), new Vector3(0.01f, 0.1f, 0.002f), tick);
                            Transform barTransform = Box(row, "Bar", new Vector3(0f, 0f, -0.008f), new Vector3(0f, 0.06f, 0.01f), bar);
                            SetBar(panel, name, barTransform);
                            break;
                        case RowKind.Gear:
                            panel.gearForward = Text(row, "Forward", "FWD", new Vector3(0f, 0f, -0.01f), TextAnchor.MiddleCenter).gameObject;
                            panel.gearReverse = Text(row, "Reverse", "REV", new Vector3(0f, 0f, -0.01f), TextAnchor.MiddleCenter).gameObject;
                            panel.gearReverse.SetActive(false);
                            break;
                        case RowKind.Fire:
                            panel.fireIndicator = Box(row, "Indicator", new Vector3(0f, 0f, -0.008f), new Vector3(0.1f, 0.08f, 0.01f), fire).gameObject;
                            panel.fireIndicator.SetActive(false);
                            break;
                    }
                }

                UdonSharpEditorUtility.CopyProxyToUdon(panel);
                UdonBehaviour udon = UdonSharpEditorUtility.GetBackingUdonBehaviour(panel);
                if (YakimaTankSetupService.TryGetSyncType(typeof(YakimaTankDebugPanel), out Networking.SyncType sync))
                {
                    udon.SyncMethod = sync;
                }

                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success)
                {
                    throw new InvalidOperationException("デバッグ UI の Prefab を保存できませんでした: " + path);
                }

                return path;
            }
            finally
            {
                YakimaTankSetupService.DeferCleanup(() =>
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
        }

        private static void SetBar(YakimaTankDebugPanel panel, string row, Transform bar)
        {
            switch (row)
            {
                case "Steering":
                    panel.steeringBar = bar;
                    break;
                case "LeftLever":
                    panel.leftLeverBar = bar;
                    break;
                case "RightLever":
                    panel.rightLeverBar = bar;
                    break;
                case "Throttle":
                    panel.throttleBar = bar;
                    break;
                case "DriveAxis":
                    panel.driveAxisBar = bar;
                    break;
                case "TurretYaw":
                    panel.turretYawBar = bar;
                    break;
                case "GunPitch":
                    panel.gunPitchBar = bar;
                    break;
            }
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null)
            {
                return found;
            }

            var created = new GameObject(name);
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        /// <summary>当たり判定のない箱。CreatePrimitive は BoxCollider を付けるので外す。</summary>
        private static Transform Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            Transform box = parent.Find(name);
            if (box == null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(cube, parent.gameObject.scene);
                cube.name = name;
                cube.transform.SetParent(parent, false);
                box = cube.transform;
            }

            Collider collider = box.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            box.localPosition = position;
            box.localRotation = Quaternion.identity;
            box.localScale = scale;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box;
        }

        /// <summary>英字の文字。日本語のフォントを同梱しないので、Unity の組み込みのフォントを使う。</summary>
        private static Transform Text(Transform parent, string name, string text, Vector3 position, TextAnchor anchor)
        {
            Transform label = Child(parent, name);
            label.localPosition = position;
            label.localRotation = Quaternion.identity;
            label.localScale = Vector3.one;
            TextMesh mesh = label.GetComponent<TextMesh>();
            if (mesh == null)
            {
                mesh = label.gameObject.AddComponent<TextMesh>();
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.text = text;
            mesh.font = font;
            mesh.fontSize = 64;
            mesh.characterSize = 0.015f;
            mesh.anchor = anchor;
            mesh.alignment = TextAlignment.Left;
            mesh.color = Color.white;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return label;
        }

        /// <summary>同期の状態の文字の欄。ワールド空間の Canvas の Text（Udon が書き換えられる文字）。操作はしないので、当たり判定と UI の入力の部品は付けない。</summary>
        private static Text SyncText(Transform parent, Vector3 position, Vector2 size)
        {
            RectTransform canvasRect = RectChild(parent, SyncName);
            Canvas canvas = canvasRect.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = canvasRect.gameObject.AddComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvasRect.localPosition = position;
            canvasRect.localRotation = Quaternion.identity;
            canvasRect.localScale = Vector3.one * CanvasScale;
            canvasRect.sizeDelta = size / CanvasScale;

            RectTransform textRect = RectChild(canvasRect, "Text");
            Text text = textRect.GetComponent<Text>();
            if (text == null)
            {
                text = textRect.gameObject.AddComponent<Text>();
            }

            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            textRect.localPosition = Vector3.zero;
            textRect.localRotation = Quaternion.identity;
            textRect.localScale = Vector3.one;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = YakimaTankDebugPanel.SyncSummary(true, YakimaTankVehicleSync.NoPlayer, false, YakimaTankVehicleSync.NoPlayer, 0, -1f, 0f, 0f, 0);
            return text;
        }

        /// <summary>
        /// UI の部品を置く子。RectTransform を後から足さない。足すと元の Transform が消え、持っていた参照が壊れた参照になる。
        /// </summary>
        private static RectTransform RectChild(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            if (found != null)
            {
                var existing = found.GetComponent<RectTransform>();
                if (existing != null)
                {
                    return existing;
                }

                Object.DestroyImmediate(found.gameObject);
            }

            var created = new GameObject(name, typeof(RectTransform));
            created.transform.SetParent(parent, false);
            return (RectTransform)created.transform;
        }

        private static Material EnsureMaterial(string folder, string name, Color color)
        {
            string path = $"{folder}/YakimaTankDebugPanel_{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Unlit/Color"));
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }
    }
}
