using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    public enum SetupSeverity
    {
        Error,
        Warning
    }

    public sealed class SetupDiagnostic
    {
        public SetupSeverity Severity;

        /// <summary>設定アセットのフィールド名（どの項目か）。全体に関わるものは空。</summary>
        public string Field;

        public string Message;

        public override string ToString()
        {
            return $"{Severity} {Field}: {Message}";
        }
    }

    /// <summary>生成の前の検証。Error が 1 つでもあれば生成しない。未実装の機能は Warning にして、合格と区別する。</summary>
    public static class YakimaTankSetupValidator
    {
        /// <summary>戦車とプレイヤーの当たりを外すのに使う、VRChat の標準のレイヤー。</summary>
        public const string WalkthroughLayer = "Walkthrough";

        /// <summary>テストで、レイヤーがないプロジェクトを再現するための口。</summary>
        internal static Func<string, int> LayerLookupForTests;

        public static int FindLayer(string name)
        {
            return LayerLookupForTests != null ? LayerLookupForTests(name) : LayerMask.NameToLayer(name);
        }

        /// <summary>元のモデルの中で、トリガーでないコライダー。生成 Prefab の中で無効にする。</summary>
        public static IEnumerable<Collider> SolidModelColliders(GameObject model)
        {
            return model == null
                ? Enumerable.Empty<Collider>()
                : model.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger);
        }

        public static List<SetupDiagnostic> Validate(YakimaTankSetupSettings settings)
        {
            var diagnostics = new List<SetupDiagnostic>();
            void Error(string field, string message) => diagnostics.Add(new SetupDiagnostic { Severity = SetupSeverity.Error, Field = field, Message = message });
            void Warning(string field, string message) => diagnostics.Add(new SetupDiagnostic { Severity = SetupSeverity.Warning, Field = field, Message = message });

            if (settings == null)
            {
                Error(string.Empty, "設定アセットがありません。");
                return diagnostics;
            }

            if (settings.schemaVersion != YakimaTankSetupSettings.CurrentSchemaVersion)
            {
                Error(nameof(settings.schemaVersion), $"設定の版 {settings.schemaVersion} は、この版の YakimaTank（{YakimaTankSetupSettings.CurrentSchemaVersion}）では読めません。");
                return diagnostics;
            }

            if (FindLayer(WalkthroughLayer) < 0)
            {
                Error(string.Empty, $"レイヤー {WalkthroughLayer} がありません。VRChat のレイヤーの設定（VRChat SDK の Control Panel の Builder にある Setup Layers）をしてください。戦車とプレイヤーの当たりを外すのに使います。");
            }

            ValidateModel(settings, Error, Warning);
            ValidateValues(settings, Error);
            ValidateOutput(settings, Error);
            return diagnostics;
        }

        public static bool HasErrors(IEnumerable<SetupDiagnostic> diagnostics)
        {
            return diagnostics.Any(d => d.Severity == SetupSeverity.Error);
        }

        private static void ValidateModel(YakimaTankSetupSettings settings, System.Action<string, string> error, System.Action<string, string> warning)
        {
            GameObject model = settings.modelPrefab;
            if (model == null)
            {
                error(nameof(settings.modelPrefab), "元のモデルがありません。");
                return;
            }

            string modelPath = AssetDatabase.GetAssetPath(model);
            if (string.IsNullOrEmpty(modelPath) || PrefabUtility.GetPrefabAssetType(model) == PrefabAssetType.NotAPrefab)
            {
                error(nameof(settings.modelPrefab), "元のモデルは、モデル（FBX など）か Prefab のアセットにしてください。シーンのオブジェクトは使えません。");
                return;
            }

            if (model.transform.parent != null)
            {
                error(nameof(settings.modelPrefab), "元のモデルは、アセットのいちばん上の GameObject にしてください。");
                return;
            }

            if (settings.body == null)
            {
                error(nameof(settings.body), "車体がありません。");
            }
            else if (!BelongsTo(settings.body, model, modelPath))
            {
                error(nameof(settings.body), "車体が、元のモデルの中を指していません。");
            }
            else if (!YakimaTankBounds.TryGetOwnBounds(settings.body, settings.body, out _, settings.turret, settings.barrel))
            {
                error(nameof(settings.body), "車体の範囲を求められません。砲塔と砲身を除くと、車体の下にメッシュがありません。車体には、車体のメッシュがある階層を選んでください。");
            }

            ValidateWheels(settings.leftWheels, nameof(settings.leftWheels), model, modelPath, error);
            ValidateWheels(settings.rightWheels, nameof(settings.rightWheels), model, modelPath, error);

            IEnumerable<Transform> left = settings.leftWheels ?? new Transform[0];
            IEnumerable<Transform> right = settings.rightWheels ?? new Transform[0];
            foreach (Transform shared in left.Where(t => t != null).Intersect(right.Where(t => t != null)))
            {
                error(nameof(settings.rightWheels), $"同じ転輪が左右の両方にあります: {YakimaTankModelPaths.GetPath(shared, model.transform)}");
            }

            int leftCount = settings.leftWheels?.Length ?? 0;
            int rightCount = settings.rightWheels?.Length ?? 0;
            if (leftCount > 0 && rightCount > 0 && leftCount != rightCount)
            {
                warning(nameof(settings.rightWheels), $"左右の転輪の数が違います（左 {leftCount}・右 {rightCount}）。まっすぐ進まないことがあります。");
            }

            if (settings.wheelRadius <= 0f)
            {
                foreach (Transform wheel in left.Concat(right).Where(t => t != null && BelongsTo(t, model, modelPath)))
                {
                    if (!YakimaTankBounds.HasMesh(wheel))
                    {
                        error(nameof(settings.wheelRadius), $"転輪にメッシュがないので半径を求められません: {YakimaTankModelPaths.GetPath(wheel, model.transform)}。半径を指定してください。");
                    }
                }
            }

            if (settings.turret == null)
            {
                warning(nameof(settings.turret), "砲塔の対応がないので、座席を車体に置きます。");
            }
            else if (!BelongsTo(settings.turret, model, modelPath))
            {
                error(nameof(settings.turret), "砲塔が、元のモデルの中を指していません。");
            }

            List<string> solid = SolidModelColliders(model).Select(c => YakimaTankModelPaths.GetPath(c.transform, model.transform) + " の " + c.GetType().Name).ToList();
            if (solid.Count > 0)
            {
                warning(nameof(settings.modelPrefab),
                    "元のモデルにトリガーでないコライダーがあります。プレイヤーと当たり、車体の物理にも入るので、生成 Prefab の中で無効にします（元のアセットは変えません）: " +
                    string.Join(", ", solid));
            }

            if (settings.barrel != null)
            {
                if (!BelongsTo(settings.barrel, model, modelPath))
                {
                    error(nameof(settings.barrel), "砲身が、元のモデルの中を指していません。");
                }
                else
                {
                    warning(nameof(settings.barrel), "砲身は記録だけです。射撃はまだありません。");
                }
            }
        }

        private static void ValidateWheels(Transform[] wheels, string field, GameObject model, string modelPath, System.Action<string, string> error)
        {
            if (wheels == null || wheels.Length == 0)
            {
                error(field, "転輪がありません。");
                return;
            }

            var seen = new HashSet<Transform>();
            for (int index = 0; index < wheels.Length; index++)
            {
                Transform wheel = wheels[index];
                if (wheel == null)
                {
                    error(field, $"{index} 番目の転輪が空です。");
                }
                else if (!BelongsTo(wheel, model, modelPath))
                {
                    error(field, $"{index} 番目の転輪が、元のモデルの中を指していません。");
                }
                else if (!seen.Add(wheel))
                {
                    error(field, $"同じ転輪が 2 回あります: {YakimaTankModelPaths.GetPath(wheel, model.transform)}");
                }
            }
        }

        public static bool BelongsTo(Transform transform, GameObject model, string modelPath)
        {
            return transform != null && AssetDatabase.GetAssetPath(transform) == modelPath && transform.root == model.transform;
        }

        private static void ValidateValues(YakimaTankSetupSettings settings, System.Action<string, string> error)
        {
            void Positive(string field, float value)
            {
                if (!IsFinite(value) || value <= 0f)
                {
                    error(field, $"0 より大きい値にしてください（今は {value}）。");
                }
            }

            void NotNegative(string field, float value)
            {
                if (!IsFinite(value) || value < 0f)
                {
                    error(field, $"0 以上の値にしてください（今は {value}）。");
                }
            }

            Positive(nameof(settings.mass), settings.mass);
            Positive(nameof(settings.driveTorque), settings.driveTorque);
            NotNegative(nameof(settings.brakeTorque), settings.brakeTorque);
            NotNegative(nameof(settings.idleBrakeTorque), settings.idleBrakeTorque);
            Positive(nameof(settings.maxForwardSpeed), settings.maxForwardSpeed);
            NotNegative(nameof(settings.maxReverseSpeed), settings.maxReverseSpeed);
            NotNegative(nameof(settings.maxTurnRate), settings.maxTurnRate);
            Positive(nameof(settings.turnAcceleration), settings.turnAcceleration);
            NotNegative(nameof(settings.wheelRadius), settings.wheelRadius);
            Positive(nameof(settings.wheelMass), settings.wheelMass);
            Positive(nameof(settings.suspensionDistance), settings.suspensionDistance);
            NotNegative(nameof(settings.suspensionSpring), settings.suspensionSpring);
            NotNegative(nameof(settings.suspensionDamper), settings.suspensionDamper);
            Positive(nameof(settings.forwardFrictionStiffness), settings.forwardFrictionStiffness);
            Positive(nameof(settings.sidewaysFrictionStiffness), settings.sidewaysFrictionStiffness);
            void Finite(string field, Vector3 value)
            {
                if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
                {
                    error(field, "数にしてください。");
                }
            }

            void Range(string field, float value, float min, float max, bool includeMin, bool includeMax)
            {
                bool aboveMin = includeMin ? value >= min : value > min;
                bool belowMax = includeMax ? value <= max : value < max;
                if (!IsFinite(value) || !aboveMin || !belowMax)
                {
                    error(field, $"{(includeMin ? "[" : "(")}{min}, {max}{(includeMax ? "]" : ")")} の範囲にしてください（今は {value}）。");
                }
            }

            Finite(nameof(settings.centerOfMassOffset), settings.centerOfMassOffset);
            Finite(nameof(settings.seatOffset), settings.seatOffset);
            Range(nameof(settings.gripThreshold), settings.gripThreshold, 0f, 1f, false, true);
            Range(nameof(settings.maxTwistAngle), settings.maxTwistAngle, 0f, 180f, false, true);
            Positive(nameof(settings.maxPushDistance), settings.maxPushDistance);
            Range(nameof(settings.reverseThreshold), settings.reverseThreshold, 0f, 1f, true, false);
            Range(nameof(settings.deadzone), settings.deadzone, 0f, 1f, true, false);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void ValidateOutput(YakimaTankSetupSettings settings, System.Action<string, string> error)
        {
            string path = settings.outputPrefabPath ?? string.Empty;
            if (!path.StartsWith("Assets/") || !path.EndsWith(".prefab") || path.Length <= "Assets/.prefab".Length)
            {
                error(nameof(settings.outputPrefabPath), "出力先は、Assets/ の下の .prefab にしてください（パッケージの中には書けません）。");
                return;
            }

            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Contains("/../") || path.Contains("\\"))
            {
                error(nameof(settings.outputPrefabPath), "出力先のパスに使えない文字があります。");
                return;
            }

            if (settings.modelPrefab != null && AssetDatabase.GetAssetPath(settings.modelPrefab) == path)
            {
                error(nameof(settings.outputPrefabPath), "出力先が元のモデルと同じです。元のモデルは変えません。");
                return;
            }

            // 生成は出力先を読み直して組み立て直すので、このツールで作っていない Prefab を指すと中身を壊してしまう。
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.GetComponent<YakimaTankVehicle>() == null)
            {
                error(nameof(settings.outputPrefabPath), "出力先に、このツールで作っていない Prefab があります。上書きしないので、別の出力先にしてください。");
            }
        }
    }
}
