using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>設定に書く内容。対応はモデルの中の階層パス（YakimaTankModelPaths）で指定する。</summary>
    public sealed class SettingsRequest
    {
        public string ModelPath;
        public string BodyPath;
        public string[] LeftWheelPaths;
        public string[] RightWheelPaths;
        public string TurretPath;
        public string BarrelPath;
        public string OutputPrefabPath;

        /// <summary>数値などの値。キーは設定アセットのフィールド名。値は数か、Vector3 なら [x, y, z]。</summary>
        public readonly Dictionary<string, object> Values = new Dictionary<string, object>();
    }

    public sealed class SettingsWriteResult
    {
        public bool Succeeded;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Written = new List<string>();
    }

    /// <summary>
    /// 階層パスで対応を指定して設定アセットを書く。解決できないパスが 1 つでもあれば、何も書かない。
    /// 書ける値は、対応・出力先と、数値・ベクトルのフィールドだけ（配置 ID などは配置の処理だけが書く）。
    /// </summary>
    public static class YakimaTankSettingsWriter
    {
        private static readonly HashSet<string> ProtectedFields = new HashSet<string>
        {
            nameof(YakimaTankSetupSettings.schemaVersion),
            nameof(YakimaTankSetupSettings.placementId),
            nameof(YakimaTankSetupSettings.hasPlacement),
            nameof(YakimaTankSetupSettings.placementPosition),
            nameof(YakimaTankSetupSettings.placementRotation)
        };

        public static SettingsWriteResult Write(string settingsPath, SettingsRequest request)
        {
            var result = new SettingsWriteResult();
            if (string.IsNullOrEmpty(settingsPath) || !settingsPath.StartsWith("Assets/") || !settingsPath.EndsWith(".asset"))
            {
                result.Errors.Add("設定アセットのパスは Assets/ の下の .asset にしてください: " + settingsPath);
                return result;
            }

            var settings = AssetDatabase.LoadAssetAtPath<YakimaTankSetupSettings>(settingsPath);
            bool creating = settings == null;
            if (creating && AssetDatabase.LoadMainAssetAtPath(settingsPath) != null)
            {
                result.Errors.Add("そのパスには別のアセットがあります: " + settingsPath);
                return result;
            }

            YakimaTankSetupSettings target = creating ? ScriptableObject.CreateInstance<YakimaTankSetupSettings>() : settings;
            GameObject model = target.modelPrefab;
            if (!string.IsNullOrEmpty(request.ModelPath))
            {
                model = AssetDatabase.LoadAssetAtPath<GameObject>(request.ModelPath);
                if (model == null)
                {
                    result.Errors.Add("モデルを読めません: " + request.ModelPath);
                }
            }

            var pending = new List<Action>();
            if (model != null)
            {
                Transform root = model.transform;
                Transform Resolve(string path)
                {
                    if (YakimaTankModelPaths.TryResolve(root, path, out Transform resolved, out string error))
                    {
                        return resolved;
                    }

                    result.Errors.Add(error);
                    return null;
                }

                if (request.ModelPath != null)
                {
                    pending.Add(() => target.modelPrefab = model);
                }

                if (request.BodyPath != null)
                {
                    Transform body = Resolve(request.BodyPath);
                    pending.Add(() => target.body = body);
                }

                if (request.LeftWheelPaths != null)
                {
                    Transform[] wheels = request.LeftWheelPaths.Select(Resolve).ToArray();
                    pending.Add(() => target.leftWheels = wheels);
                }

                if (request.RightWheelPaths != null)
                {
                    Transform[] wheels = request.RightWheelPaths.Select(Resolve).ToArray();
                    pending.Add(() => target.rightWheels = wheels);
                }

                if (request.TurretPath != null)
                {
                    Transform turret = request.TurretPath.Length == 0 ? null : Resolve(request.TurretPath);
                    pending.Add(() => target.turret = turret);
                }

                if (request.BarrelPath != null)
                {
                    Transform barrel = request.BarrelPath.Length == 0 ? null : Resolve(request.BarrelPath);
                    pending.Add(() => target.barrel = barrel);
                }
            }
            else if (request.BodyPath != null || request.LeftWheelPaths != null || request.RightWheelPaths != null)
            {
                result.Errors.Add("対応を書くには、元のモデルが要ります。");
            }

            if (request.OutputPrefabPath != null)
            {
                pending.Add(() => target.outputPrefabPath = request.OutputPrefabPath);
            }

            foreach (KeyValuePair<string, object> pair in request.Values)
            {
                FieldInfo field = typeof(YakimaTankSetupSettings).GetField(pair.Key, BindingFlags.Public | BindingFlags.Instance);
                if (field == null || ProtectedFields.Contains(pair.Key) || (field.FieldType != typeof(float) && field.FieldType != typeof(Vector3)))
                {
                    result.Errors.Add("書けない値です: " + pair.Key);
                    continue;
                }

                if (!TryConvert(pair.Value, field.FieldType, out object converted))
                {
                    result.Errors.Add($"{pair.Key} の値を読めません: {pair.Value}");
                    continue;
                }

                pending.Add(() => field.SetValue(target, converted));
            }

            if (result.Errors.Count > 0)
            {
                if (creating)
                {
                    UnityEngine.Object.DestroyImmediate(target);
                }

                return result;
            }

            if (!creating)
            {
                Undo.RecordObject(target, "YakimaTank の設定");
            }

            foreach (Action apply in pending)
            {
                apply();
            }

            if (creating)
            {
                string folder = Path.GetDirectoryName(settingsPath)?.Replace('\\', '/');
                EnsureFolder(folder);
                AssetDatabase.CreateAsset(target, settingsPath);
            }

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
            result.Written.Add(settingsPath);
            result.Succeeded = true;
            return result;
        }

        private static bool TryConvert(object value, Type type, out object converted)
        {
            converted = null;
            try
            {
                if (type == typeof(float))
                {
                    converted = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                    return true;
                }

                if (value is System.Collections.IEnumerable list && !(value is string))
                {
                    float[] values = list.Cast<object>().Select(v => Convert.ToSingle(v, CultureInfo.InvariantCulture)).ToArray();
                    if (values.Length == 3)
                    {
                        converted = new Vector3(values[0], values[1], values[2]);
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // 数にならない値は、下で読めないとして返す。
            }

            return false;
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
    }
}
