using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    public sealed class ModelNodeReport
    {
        public string Path;
        public int Depth;
        public int ChildCount;
        public bool HasMesh;

        /// <summary>このノードの下のメッシュの範囲（モデルを原点に置いたときの座標。ルート自身の回転を含む）。メッシュがなければ null。</summary>
        public Bounds? MeshBounds;

        public Vector3 LocalPosition;
        public Vector3 LocalEulerAngles;
        public Vector3 LocalScale;

        /// <summary>名前から見た候補（wheel / turret / barrel / body）。決めるのは人か、根拠を確かめた AI。</summary>
        public string Candidate;
    }

    public sealed class ModelReport
    {
        public string AssetPath;
        public string AssetType;
        public float? ImporterGlobalScale;
        public bool? ImporterUseFileScale;
        public bool? ImporterBakeAxisConversion;
        public Bounds? TotalBounds;

        /// <summary>メッシュの範囲がいちばん長い水平の軸（前後の向きの手がかり）。</summary>
        public string LongestHorizontalAxis;

        public readonly List<ModelNodeReport> Nodes = new List<ModelNodeReport>();
    }

    /// <summary>モデルを読むだけで変えない。対応を決めるための手がかり（階層・範囲・軸・スケール）を返す。</summary>
    public static class YakimaTankModelInspector
    {
        private static readonly Regex WheelPattern = new Regex("wheel|rad|road|idler|sprocket|roller", RegexOptions.IgnoreCase);
        private static readonly Regex TurretPattern = new Regex("turret|tower", RegexOptions.IgnoreCase);
        private static readonly Regex BarrelPattern = new Regex("barrel|gun|cannon|mantlet", RegexOptions.IgnoreCase);
        private static readonly Regex BodyPattern = new Regex("hull|body|chassis", RegexOptions.IgnoreCase);

        public static ModelReport Inspect(GameObject model)
        {
            string path = AssetDatabase.GetAssetPath(model);
            var report = new ModelReport
            {
                AssetPath = path,
                AssetType = PrefabUtility.GetPrefabAssetType(model).ToString()
            };

            if (AssetImporter.GetAtPath(path) is ModelImporter importer)
            {
                report.ImporterGlobalScale = importer.globalScale;
                report.ImporterUseFileScale = importer.useFileScale;
                report.ImporterBakeAxisConversion = importer.bakeAxisConversion;
            }

            if (YakimaTankBounds.TryGetBounds(model.transform, Matrix4x4.identity, out Bounds total))
            {
                report.TotalBounds = total;
                report.LongestHorizontalAxis = total.size.x >= total.size.z ? "X" : "Z";
            }

            Visit(model.transform, model.transform, 0, report);
            return report;
        }

        private static void Visit(Transform node, Transform root, int depth, ModelReport report)
        {
            // 生成ではモデルのルートの回転を保ったまま置くので、ルートの親の座標（アセットではワールド）で測る。
            bool hasBounds = YakimaTankBounds.TryGetBounds(node, Matrix4x4.identity, out Bounds bounds);
            report.Nodes.Add(new ModelNodeReport
            {
                Path = YakimaTankModelPaths.GetPath(node, root),
                Depth = depth,
                ChildCount = node.childCount,
                HasMesh = node.GetComponent<MeshFilter>() != null || node.GetComponent<SkinnedMeshRenderer>() != null,
                MeshBounds = hasBounds ? bounds : (Bounds?)null,
                LocalPosition = node.localPosition,
                LocalEulerAngles = node.localEulerAngles,
                LocalScale = node.localScale,
                Candidate = Candidate(node.name)
            });

            for (int index = 0; index < node.childCount; index++)
            {
                Visit(node.GetChild(index), root, depth + 1, report);
            }
        }

        private static string Candidate(string name)
        {
            if (WheelPattern.IsMatch(name))
            {
                return "wheel";
            }

            if (BarrelPattern.IsMatch(name))
            {
                return "barrel";
            }

            if (TurretPattern.IsMatch(name))
            {
                return "turret";
            }

            return BodyPattern.IsMatch(name) ? "body" : null;
        }
    }
}
