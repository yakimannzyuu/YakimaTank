using System.Collections.Generic;
using System.Linq;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.Udon;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// セットアップウィンドウ。検証・生成・配置は共通の処理（YakimaTankSetupService など）を呼ぶだけで、ここに別の手順を持たない。
    /// </summary>
    public sealed class YakimaTankSetupWindow : EditorWindow
    {
        private static readonly string[] RuntimeFieldNames = YakimaTankRuntimeValues.Names.ToArray();

        private static readonly string[] SuspensionFieldNames =
        {
            nameof(YakimaTankSetupSettings.wheelRadius),
            nameof(YakimaTankSetupSettings.wheelMass),
            nameof(YakimaTankSetupSettings.suspensionDistance),
            nameof(YakimaTankSetupSettings.suspensionSpring),
            nameof(YakimaTankSetupSettings.suspensionDamper),
            nameof(YakimaTankSetupSettings.forwardFrictionStiffness),
            nameof(YakimaTankSetupSettings.sidewaysFrictionStiffness)
        };

        [SerializeField] private YakimaTankSetupSettings settings;
        [SerializeField] private Vector2 scroll;
        private readonly YakimaTankWorkingValues _working = new YakimaTankWorkingValues();
        private UdonBehaviour _target;
        private readonly List<string> _messages = new List<string>();

        [MenuItem("Tools/YakimaTank/Setup")]
        private static void Open()
        {
            GetWindow<YakimaTankSetupWindow>("YakimaTank Setup");
        }

        private void OnDisable()
        {
            _working.Discard();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                // 作業値は Play ごと。抜けたら捨てる（保存していない値を次の Play に持ち越さない）。
                _working.Discard();
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.HelpBox("未実装：砲塔の操作・射撃・同期・乗車と入力の割り当て。いまできるのは走行（SetDriveInput）まで。", MessageType.Info);
            settings = (YakimaTankSetupSettings)EditorGUILayout.ObjectField("設定アセット", settings, typeof(YakimaTankSetupSettings), false);
            if (settings == null)
            {
                if (GUILayout.Button("新しい設定アセットを作る"))
                {
                    CreateSettings();
                }

                EditorGUILayout.EndScrollView();
                return;
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawSettings();
                DrawActions();
            }

            if (EditorApplication.isPlaying)
            {
                DrawPlayTuning();
            }

            foreach (string message in _messages)
            {
                EditorGUILayout.HelpBox(message, message.StartsWith("Error") ? MessageType.Error : MessageType.None);
            }

            EditorGUILayout.EndScrollView();
        }

        private void CreateSettings()
        {
            string path = EditorUtility.SaveFilePanelInProject("設定アセットを作る", "YakimaTankSetup", "asset", "保存先を選んでください");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            settings = CreateInstance<YakimaTankSetupSettings>();
            AssetDatabase.CreateAsset(settings, path);

            // SaveAssets は利用者のほかの未保存のアセットまで保存するので、作った設定だけを保存する。
            AssetDatabase.SaveAssetIfDirty(settings);
        }

        private void DrawSettings()
        {
            var serialized = new SerializedObject(settings);
            serialized.Update();
            EditorGUILayout.LabelField("モデル", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty(nameof(YakimaTankSetupSettings.modelPrefab)));
            EditorGUILayout.PropertyField(serialized.FindProperty(nameof(YakimaTankSetupSettings.modelRotation)));
            serialized.ApplyModifiedProperties();

            if (settings.modelPrefab != null)
            {
                EditorGUILayout.LabelField("対応（モデルの階層から選ぶ）", EditorStyles.boldLabel);
                Transform[] nodes = settings.modelPrefab.GetComponentsInChildren<Transform>(true);
                string[] paths = nodes.Select(n => n == settings.modelPrefab.transform ? "(ルート)" : YakimaTankModelPaths.GetPath(n, settings.modelPrefab.transform)).ToArray();
                settings.body = TransformPopup("車体", settings.body, nodes, paths, true);
                settings.leftWheels = TransformList("左の転輪", settings.leftWheels, nodes, paths);
                settings.rightWheels = TransformList("右の転輪", settings.rightWheels, nodes, paths);
                settings.turret = TransformPopup("砲塔（記録だけ）", settings.turret, nodes, paths, true);
                settings.barrel = TransformPopup("砲身（記録だけ）", settings.barrel, nodes, paths, true);
            }

            serialized.Update();
            EditorGUILayout.LabelField("走行（Play 中に再適用できる）", EditorStyles.boldLabel);
            foreach (string name in RuntimeFieldNames)
            {
                EditorGUILayout.PropertyField(serialized.FindProperty(name));
            }

            EditorGUILayout.LabelField("足回り（生成で反映）", EditorStyles.boldLabel);
            foreach (string name in SuspensionFieldNames)
            {
                EditorGUILayout.PropertyField(serialized.FindProperty(name));
            }

            EditorGUILayout.LabelField("出力", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty(nameof(YakimaTankSetupSettings.outputPrefabPath)));
            serialized.ApplyModifiedProperties();
            if (string.IsNullOrEmpty(settings.outputPrefabPath) && settings.modelPrefab != null && GUILayout.Button("出力先を既定にする"))
            {
                Undo.RecordObject(settings, "YakimaTank の出力先");
                settings.outputPrefabPath = $"Assets/YakimaTank Generated/{settings.modelPrefab.name}.prefab";
                EditorUtility.SetDirty(settings);
            }
        }

        private Transform TransformPopup(string label, Transform current, Transform[] nodes, string[] paths, bool allowNone)
        {
            string[] options = allowNone ? new[] { "(なし)" }.Concat(paths).ToArray() : paths;
            int index = System.Array.IndexOf(nodes, current) + (allowNone ? 1 : 0);
            int selected = EditorGUILayout.Popup(label, Mathf.Max(index, 0), options);
            Transform result = allowNone ? (selected == 0 ? null : nodes[selected - 1]) : nodes[selected];
            if (result != current)
            {
                Undo.RecordObject(settings, "YakimaTank の対応");
                EditorUtility.SetDirty(settings);
            }

            return result;
        }

        private Transform[] TransformList(string label, Transform[] current, Transform[] nodes, string[] paths)
        {
            var list = (current ?? new Transform[0]).ToList();
            EditorGUILayout.LabelField(label + $"（{list.Count}）");
            using (new EditorGUI.IndentLevelScope())
            {
                for (int index = 0; index < list.Count; index++)
                {
                    EditorGUILayout.BeginHorizontal();
                    list[index] = TransformPopup($"{index}", list[index], nodes, paths, true);
                    if (GUILayout.Button("×", GUILayout.Width(24)))
                    {
                        Undo.RecordObject(settings, "YakimaTank の対応");
                        list.RemoveAt(index);
                        EditorUtility.SetDirty(settings);
                        EditorGUILayout.EndHorizontal();
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("追加"))
                {
                    Undo.RecordObject(settings, "YakimaTank の対応");
                    list.Add(null);
                    EditorUtility.SetDirty(settings);
                }
            }

            return list.ToArray();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("検証"))
            {
                Show(YakimaTankSetupValidator.Validate(settings), null);
            }

            if (GUILayout.Button("生成"))
            {
                AssetDatabase.SaveAssetIfDirty(settings);
                SetupResult result = YakimaTankSetupService.Generate(settings);
                Show(result.Diagnostics, result);
            }

            if (GUILayout.Button("今のシーンに配置・更新"))
            {
                PlacementResult placement = YakimaTankScenePlacement.Place(settings, EditorSceneManager.GetActiveScene());
                _messages.Clear();
                _messages.Add(placement.Succeeded ? "配置：" + string.Join(" / ", placement.Changes) : "Error 配置：" + placement.Error);
                _messages.AddRange(placement.Conflicts.Select(c => "Error " + c));
            }

            EditorGUILayout.EndHorizontal();
        }

        private void Show(IEnumerable<SetupDiagnostic> diagnostics, SetupResult result)
        {
            _messages.Clear();
            _messages.AddRange(diagnostics.Select(d => $"{d.Severity} {d.Field}: {d.Message}"));
            if (result == null)
            {
                _messages.Add(_messages.Any(m => m.StartsWith("Error")) ? "検証：Error があります。" : "検証：生成できます。");
                return;
            }

            if (!result.Succeeded)
            {
                _messages.Add("Error 生成：" + result.Error + (result.RestoredPrevious ? "（前の成果物に戻しました）" : string.Empty));
                return;
            }

            _messages.Add($"生成：{result.OutputPath}\n作った：{string.Join(", ", result.Created)}\n更新した：{string.Join(", ", result.Updated)}\n消した：{string.Join(", ", result.Removed)}");
        }

        private void DrawPlayTuning()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Play 中の調整（作業値。保存するまで設定には書かない）", EditorStyles.boldLabel);
            YakimaTankSetupSettings working = _working.For(settings);
            UdonBehaviour[] vehicles = FindObjectsOfType<UdonBehaviour>()
                .Where(b => UdonSharpEditorUtility.GetUdonSharpBehaviourType(b) == typeof(YakimaTankVehicle))
                .ToArray();
            if (vehicles.Length == 0)
            {
                EditorGUILayout.HelpBox("シーンに車両がありません。", MessageType.Warning);
                return;
            }

            int current = Mathf.Max(0, System.Array.IndexOf(vehicles, _target));
            current = EditorGUILayout.Popup("対象の車両", current, vehicles.Select(v => v.gameObject.name).ToArray());
            _target = vehicles[current];

            var serialized = new SerializedObject(working);
            serialized.Update();
            foreach (string name in RuntimeFieldNames)
            {
                EditorGUILayout.PropertyField(serialized.FindProperty(name));
            }

            serialized.ApplyModifiedProperties();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("再適用"))
            {
                YakimaTankPlayTuning.Reapply(_target, working);
            }

            if (GUILayout.Button("出し直し"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(settings.outputPrefabPath);
                GameObject root = _target.gameObject;
                GameObject respawned = YakimaTankPlayTuning.Respawn(root, prefab, working);
                _target = YakimaTankRuntimeValues.FindVehicle(respawned);
            }

            if (GUILayout.Button("作業値を設定に保存"))
            {
                YakimaTankPlayTuning.SaveWorkingValues(working, settings);
                _messages.Clear();
                _messages.Add("作業値を設定に保存しました: " + AssetDatabase.GetAssetPath(settings));
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
