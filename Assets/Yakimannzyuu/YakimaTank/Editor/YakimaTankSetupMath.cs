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

        /// <summary>
        /// 砲塔の下にメッシュがないときの砲塔の命中用のトリガー（ルートの座標）。前後・左右は車体のコライダー、
        /// 下は車体のコライダーの下端、上はモデル全体と車体のコライダーの上端の高い方。
        /// </summary>
        public static Bounds UpperHitboxBounds(Bounds bodyCollider, Bounds model)
        {
            var bounds = new Bounds();
            bounds.SetMinMax(bodyCollider.min, new Vector3(bodyCollider.max.x, Mathf.Max(bodyCollider.max.y, model.max.y), bodyCollider.max.z));
            return bounds;
        }

        /// <summary>
        /// 使い回す砲弾の数（仕様書 0.5.0 の 1.6）。「寿命 ÷ 装填の時間」の切り上げ ＋ 1 で、2 つ以上。
        /// 自分の撃った砲弾が飛んでいる間に、次の発射で上書きしないため。上限は設けない（装填の時間は送る間隔より長いことを検証で確かめる）。
        /// </summary>
        public static int ShellCount(float shellLifetime, float reloadTime)
        {
            if (!(reloadTime > 0f) || !(shellLifetime > 0f))
            {
                return 2;
            }

            return Mathf.Max(2, Mathf.CeilToInt(shellLifetime / reloadTime) + 1);
        }

        /// <summary>乗車の判定の箱を、車体のコライダーの面から外へ離す幅（m）。</summary>
        public const float BoardingMargin = 0.05f;

        /// <summary>
        /// 乗車の判定の箱。前後・左右は車体のコライダー、上下はモデル全体と車体のコライダーを合わせた範囲を、各面 <see cref="BoardingMargin"/> 広げる。
        /// 前後・左右をモデル全体にしないのは、砲身などで張り出した箱の中に立つと、Interact の光線が判定に当たらず車体のコライダーで止まるため。
        /// 面を車体のコライダーと重ねないのは、光線が同じ距離で両方に当たると、どちらが先かが決まらないため。
        /// </summary>
        public static Bounds BoardingAreaBounds(Bounds bodyCollider, Bounds model)
        {
            var result = new Bounds();
            result.SetMinMax(
                new Vector3(bodyCollider.min.x, Mathf.Min(model.min.y, bodyCollider.min.y), bodyCollider.min.z),
                new Vector3(bodyCollider.max.x, Mathf.Max(model.max.y, bodyCollider.max.y), bodyCollider.max.z));
            result.Expand(BoardingMargin * 2f);
            return result;
        }
    }
}
