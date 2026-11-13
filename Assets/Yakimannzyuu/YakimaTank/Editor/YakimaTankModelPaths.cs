using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// モデルの中の階層パス。同じ親の下に同じ名前が並ぶときは「名前[番号]」（同じ名前の中での順番）で区別する。
    /// 名前だけで探すと、取り違えても気づけないため、曖昧なパスは解決しない。
    /// </summary>
    public static class YakimaTankModelPaths
    {
        private static readonly Regex SegmentPattern = new Regex(@"^(?<name>.*?)(\[(?<index>\d+)\])?$", RegexOptions.Compiled);

        /// <summary>root からのパス。root 自身は空文字。</summary>
        public static string GetPath(Transform target, Transform root)
        {
            var segments = new List<string>();
            for (Transform current = target; current != null && current != root; current = current.parent)
            {
                segments.Add(Segment(current));
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static string Segment(Transform transform)
        {
            Transform parent = transform.parent;
            if (parent == null)
            {
                return transform.name;
            }

            int sameNameCount = 0;
            int index = 0;
            for (int child = 0; child < parent.childCount; child++)
            {
                Transform sibling = parent.GetChild(child);
                if (sibling.name != transform.name)
                {
                    continue;
                }

                if (sibling == transform)
                {
                    index = sameNameCount;
                }

                sameNameCount++;
            }

            return sameNameCount > 1 ? $"{transform.name}[{index}]" : transform.name;
        }

        public static bool TryResolve(Transform root, string path, out Transform result, out string error)
        {
            result = root;
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            foreach (string segment in path.Split('/'))
            {
                Match match = SegmentPattern.Match(segment);
                string name = match.Groups["name"].Value;
                List<Transform> candidates = Children(result).Where(child => child.name == name).ToList();
                if (candidates.Count == 0)
                {
                    error = $"「{path}」の「{segment}」がありません。{Describe(result, root)} の子: {string.Join(", ", Children(result).Select(Segment).Take(20))}";
                    result = null;
                    return false;
                }

                if (match.Groups["index"].Success)
                {
                    int index = int.Parse(match.Groups["index"].Value);
                    if (index >= candidates.Count)
                    {
                        error = $"「{path}」の「{segment}」は範囲外です（{name} は {candidates.Count} 個）。";
                        result = null;
                        return false;
                    }

                    result = candidates[index];
                }
                else if (candidates.Count > 1)
                {
                    error = $"「{path}」の「{segment}」が曖昧です。同じ名前が {candidates.Count} 個あります: " +
                            string.Join(", ", Enumerable.Range(0, candidates.Count).Select(i => $"{name}[{i}]"));
                    result = null;
                    return false;
                }
                else
                {
                    result = candidates[0];
                }
            }

            return true;
        }

        private static IEnumerable<Transform> Children(Transform parent)
        {
            for (int index = 0; index < parent.childCount; index++)
            {
                yield return parent.GetChild(index);
            }
        }

        private static string Describe(Transform transform, Transform root)
        {
            string path = GetPath(transform, root);
            return string.IsNullOrEmpty(path) ? "(ルート)" : path;
        }

        public static string Join(IEnumerable<Transform> transforms, Transform root)
        {
            var builder = new StringBuilder();
            foreach (Transform transform in transforms)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(transform == null ? "(なし)" : GetPath(transform, root));
            }

            return builder.ToString();
        }
    }
}
