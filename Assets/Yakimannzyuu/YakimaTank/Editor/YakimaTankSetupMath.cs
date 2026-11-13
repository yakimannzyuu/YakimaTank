using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>生成で使う計算。外に依存しないので、EditMode のテストで直接確かめる。</summary>
    public static class YakimaTankSetupMath
    {
        public const float Gravity = 9.81f;

        /// <summary>静止したときの荷重で、サスペンションの長さの 25% だけ縮むばね（N/m）。</summary>
        public const float RestCompressionRatio = 0.25f;

        /// <summary>臨界減衰に対する割合。揺れが 1〜2 回で収まる程度。</summary>
        public const float DampingRatio = 0.3f;

        public static float AutoSuspensionSpring(float mass, int wheelCount, float suspensionDistance)
        {
            if (mass <= 0f || wheelCount <= 0 || suspensionDistance <= 0f)
            {
                return 0f;
            }

            float load = mass * Gravity / wheelCount;
            return load / (RestCompressionRatio * suspensionDistance);
        }

        public static float AutoSuspensionDamper(float spring, float mass, int wheelCount)
        {
            if (spring <= 0f || mass <= 0f || wheelCount <= 0)
            {
                return 0f;
            }

            float sprungMass = mass / wheelCount;
            return DampingRatio * 2f * Mathf.Sqrt(spring * sprungMass);
        }

        /// <summary>
        /// 車軸が左右（X）を向いた転輪のメッシュの範囲から半径を求める。上下（Y）と前後（Z）の半分の平均。
        /// </summary>
        public static float WheelRadiusFromBounds(Bounds bounds)
        {
            return (bounds.extents.y + bounds.extents.z) * 0.5f;
        }

        /// <summary>
        /// WheelCollider の置き場所。静止したとき（targetPosition の位置）に、転輪の中心がメッシュの中心に来る高さ。
        /// </summary>
        public static Vector3 WheelColliderPosition(Vector3 meshCenter, float suspensionDistance, float targetPosition)
        {
            return meshCenter + Vector3.up * (suspensionDistance * (1f - targetPosition));
        }

        /// <summary>車体の箱の底は、転輪の中心より下げない（地面に擦って進めなくなるため）。</summary>
        public static Bounds BodyColliderBounds(Bounds bodyBounds, float wheelCenterHeight)
        {
            float bottom = Mathf.Max(bodyBounds.min.y, wheelCenterHeight);
            float top = Mathf.Max(bodyBounds.max.y, bottom + 0.05f);
            var result = new Bounds();
            result.SetMinMax(new Vector3(bodyBounds.min.x, bottom, bodyBounds.min.z), new Vector3(bodyBounds.max.x, top, bodyBounds.max.z));
            return result;
        }
    }
}
