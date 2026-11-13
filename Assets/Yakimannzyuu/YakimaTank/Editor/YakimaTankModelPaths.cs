using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// モデルの中の階層パス。同じ親の下に同じ名前が並ぶときは「名前[番号]」（同じ名前の中での順番）で区別する。
    /// 名前の中の \ / [ ] は前に \ を付ける。付けないと、「Wheel[0]」という名前を「Wheel の 0 番目」と取り違える。
    /// 名前だけで探すと、取り違えても気づけないため、曖昧なパスは解決しない。
    /// </summary>
    public static class YakimaTankModelPaths
    {
        private const char Escape = '\\';
        private const string Reserved = "\\/[]";

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
            string name = EscapeName(transform.name);
            Transform parent = transform.parent;
            if (parent == null)
            {
                return name;
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

            return sameNameCount > 1 ? $"{name}[{index}]" : name;
        }

        private static string EscapeName(string name)
        {
            var builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (Reserved.IndexOf(c) >= 0)
                {
                    builder.Append(Escape);
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        public static bool TryResolve(Transform root, string path, out Transform result, out string error)
        {
            result = root;
            error = null;
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            foreach (string segment in SplitSegments(path))
            {
                if (!TryParseSegment(segment, out string name, out int index))
                {
                    error = $"「{path}」の「{segment}」を読めません。名前の中の \\ / [ ] には、前に \\ を付けます。";
                    result = null;
                    return false;
                }

                List<Transform> candidates = Children(result).Where(child => child.name == name).ToList();
                if (candidates.Count == 0)
                {
                    error = $"「{path}」の「{segment}」がありません。{Describe(result, root)} の子: {string.Join(", ", Children(result).Select(Segment).Take(20))}";
                    result = null;
                    return false;
                }

                if (index >= 0)
                {
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
                            string.Join(", ", Enumerable.Range(0, candidates.Count).Select(i => $"{EscapeName(name)}[{i}]"));
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

        private static List<string> SplitSegments(string path)
        {
            var segments = new List<string>();
            var current = new StringBuilder();
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                if (c == Escape && i + 1 < path.Length)
                {
                    current.Append(c).Append(path[++i]);
                }
                else if (c == '/')
                {
                    segments.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            segments.Add(current.ToString());
            return segments;
        }

        /// <summary>番号は末尾の「[数字]」だけ。番号がなければ index は -1。</summary>
        private static bool TryParseSegment(string segment, out string name, out int index)
        {
            var builder = new StringBuilder(segment.Length);
            name = null;
            index = -1;
            for (int i = 0; i < segment.Length; i++)
            {
                char c = segment[i];
                if (c == Escape)
                {
                    if (i + 1 == segment.Length)
                    {
                        return false;
                    }

                    builder.Append(segment[++i]);
                }
                else if (c == '[')
                {
                    if (segment[segment.Length - 1] != ']' ||
                        !int.TryParse(segment.Substring(i + 1, segment.Length - i - 2), NumberStyles.None, CultureInfo.InvariantCulture, out index))
                    {
                        return false;
                    }

                    break;
                }
                else if (c == ']')
                {
                    return false;
                }
                else
                {
                    builder.Append(c);
                }
            }

            name = builder.ToString();
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
