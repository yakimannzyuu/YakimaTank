using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// メッシュの範囲を、指定した座標で求める。Renderer.bounds はシーンの更新に左右されるので、
    /// メッシュの範囲の 8 隅を座標変換して包む。SkinnedMeshRenderer はボーンで動くので、
    /// Renderer 自身の Transform ではなく、ルートのボーンに対する localBounds を使う。
    /// </summary>
    public static class YakimaTankBounds
    {
        public static bool HasMesh(Transform target)
        {
            foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                {
                    return true;
                }
            }

            foreach (SkinnedMeshRenderer skinned in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>target の下のメッシュの範囲を space の座標で。メッシュがなければ false。</summary>
        public static bool TryGetLocalBounds(Transform target, Transform space, out Bounds bounds)
        {
            return TryGetBounds(target, space.worldToLocalMatrix, out bounds);
        }

        /// <summary>target 自身のメッシュだけの範囲（子は含めない）。target にメッシュがなければ子も含めて求める。</summary>
        public static bool TryGetOwnBounds(Transform target, Transform space, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            Matrix4x4 toSpace = space.worldToLocalMatrix;
            MeshFilter filter = target.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                Encapsulate(ref bounds, ref found, filter.sharedMesh.bounds, toSpace * target.localToWorldMatrix);
            }

            SkinnedMeshRenderer skinned = target.GetComponent<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null)
            {
                Transform boundsSpace = skinned.rootBone != null ? skinned.rootBone : skinned.transform;
                Encapsulate(ref bounds, ref found, skinned.localBounds, toSpace * boundsSpace.localToWorldMatrix);
            }

            return found || TryGetLocalBounds(target, space, out bounds);
        }

        /// <summary>toSpace はワールドから求めたい座標への変換。アセットのモデルなら、単位行列でルートの親の座標になる。</summary>
        public static bool TryGetBounds(Transform target, Matrix4x4 toSpace, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null)
                {
                    Encapsulate(ref bounds, ref found, filter.sharedMesh.bounds, toSpace * filter.transform.localToWorldMatrix);
                }
            }

            foreach (SkinnedMeshRenderer skinned in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null)
                {
                    Transform boundsSpace = skinned.rootBone != null ? skinned.rootBone : skinned.transform;
                    Encapsulate(ref bounds, ref found, skinned.localBounds, toSpace * boundsSpace.localToWorldMatrix);
                }
            }

            return found;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool found, Bounds local, Matrix4x4 matrix)
        {
            Vector3 min = local.min;
            Vector3 max = local.max;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 transformed = matrix.MultiplyPoint3x4(point);
                if (!found)
                {
                    bounds = new Bounds(transformed, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(transformed);
                }
            }
        }
    }
}
