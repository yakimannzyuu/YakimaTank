using NUnit.Framework;
using UnityEngine;
using Sync = Yakimannzyuu.YakimaTank.YakimaTankVehicleSync;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    /// <summary>車両同士の簡易衝突の計算（仕様書 0.4.0 の 1.2〜1.4）。</summary>
    public class YakimaTankContactMathTests
    {
        private const float StopRate = 10f;
        private const float PushStrength = 20f;
        private const float MaxPush = 5f;
        private const float DeltaTime = 0.02f;
        private const float Tolerance = 1e-4f;

        /// <summary>相手が前（+Z）にいるときの、相手から自車へ向かう向き。</summary>
        private static readonly Vector3 FromFront = Vector3.back;

        private static void AssertVector(Vector3 actual, Vector3 expected, string message = null)
        {
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(Tolerance), $"{message} 期待 {expected:F4}・実際 {actual:F4}");
        }

        [Test]
        public void 自分が寄る分だけを消し相手が寄って来る速さは写さない()
        {
            Vector3 change = Sync.ContactStopChange(new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, -3f), FromFront, StopRate, DeltaTime);
            AssertVector(change, FromFront * (2f * StopRate * DeltaTime), "寄る速さ 2 の分だけ");

            AssertVector(Sync.ContactStopChange(Vector3.zero, new Vector3(0f, 0f, -3f), FromFront, StopRate, DeltaTime), Vector3.zero,
                "止まっている自分は、寄って来る相手の速さで動かない");
        }

        [Test]
        public void 相手が逃げる速さまでは寄れる()
        {
            Vector3 slower = Sync.ContactStopChange(new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, 1.5f), FromFront, StopRate, DeltaTime);
            AssertVector(slower, FromFront * (0.5f * StopRate * DeltaTime), "逃げる速さ 1.5 を超えた 0.5 の分だけ消す");

            AssertVector(Sync.ContactStopChange(new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, 3f), FromFront, StopRate, DeltaTime), Vector3.zero,
                "逃げる速さより遅く寄るなら消さない");
        }

        [Test]
        public void 離れる向きの速度では止める分を出さない()
        {
            AssertVector(Sync.ContactStopChange(new Vector3(0f, 0f, -1f), Vector3.zero, FromFront, StopRate, DeltaTime), Vector3.zero);
            AssertVector(Sync.ContactStopChange(new Vector3(3f, 0f, 0f), Vector3.zero, FromFront, StopRate, DeltaTime), Vector3.zero, "横に動くだけ");
        }

        [Test]
        public void 止める分は寄る速さを超えない()
        {
            Vector3 change = Sync.ContactStopChange(new Vector3(0f, 0f, 2f), Vector3.zero, FromFront, StopRate, 0.5f);
            AssertVector(change, FromFront * 2f);
        }

        [Test]
        public void 押し返しは深さに比例し上限で止まる()
        {
            AssertVector(Sync.ContactPush(FromFront, 0.1f, PushStrength, MaxPush), FromFront * 2f, "深さ 0.1 で 2");
            AssertVector(Sync.ContactPush(FromFront, 0.5f, PushStrength, MaxPush), FromFront * MaxPush, "上限");
            AssertVector(Sync.ContactPush(FromFront, 0f, PushStrength, MaxPush), Vector3.zero, "深さ 0");
        }

        [Test]
        public void 複数の相手の押し返しの合計は上限で切り止める分は切らない()
        {
            var stop = new Vector3(0f, 0f, -3f);
            Vector3 push = Sync.ContactPush(FromFront, 0.5f, PushStrength, MaxPush) + Sync.ContactPush(Vector3.right, 0.5f, PushStrength, MaxPush);
            Vector3 total = Sync.ContactTotalChange(stop, push, MaxPush, DeltaTime);

            AssertVector(total, stop + push.normalized * MaxPush * DeltaTime);
        }

        private static readonly Vector3 Half = new Vector3(1f, 0.5f, 2f);
        private static readonly Vector3 Center = new Vector3(0f, 1f, 0f);

        private static Vector3 Penetration(Vector3 otherCenter, Quaternion otherRotation, Vector3? otherHalf = null, Quaternion? rotation = null, Vector3? half = null)
        {
            return Sync.HorizontalPenetration(Center, rotation ?? Quaternion.identity, half ?? Half, otherCenter, otherRotation, otherHalf ?? Half);
        }

        [Test]
        public void 前後に重なった箱はいちばん浅い向きと深さを相手から自車へ向かう水平の向きで返す()
        {
            AssertVector(Penetration(Center + new Vector3(0f, 0f, 3.5f), Quaternion.identity), new Vector3(0f, 0f, -0.5f), "前の相手");
            AssertVector(Penetration(Center + new Vector3(0f, 0f, -3.5f), Quaternion.identity), new Vector3(0f, 0f, 0.5f), "後ろの相手");
        }

        [Test]
        public void 左右に重なった箱は左右の向きで返す()
        {
            AssertVector(Penetration(Center + new Vector3(1.8f, 0f, 0.5f), Quaternion.identity), new Vector3(-0.2f, 0f, 0f));
        }

        [Test]
        public void 相手を90度回すとその向きの幅で重なりを求める()
        {
            AssertVector(Penetration(Center + new Vector3(2.5f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f)), new Vector3(-0.5f, 0f, 0f));
        }

        [Test]
        public void 相手を45度回すと角どうしが離れていれば重ならず重なれば斜めの向きで返す()
        {
            var square = new Vector3(1f, 0.5f, 1f);
            Quaternion diagonal = Quaternion.Euler(0f, 45f, 0f);
            AssertVector(Penetration(Center + new Vector3(1.9f, 0f, 1.9f), diagonal, square, half: square), Vector3.zero,
                "自車の軸では重なるが、相手の軸で離れている");

            Vector3 result = Penetration(Center + new Vector3(1.5f, 0f, 1.5f), diagonal, square, half: square);
            float depth = 2f * Mathf.Sqrt(0.5f) + 1f - 1.5f * Mathf.Sqrt(2f);
            AssertVector(result, new Vector3(-1f, 0f, -1f).normalized * depth);
        }

        [Test]
        public void 水平面で離れている箱は重ならない()
        {
            AssertVector(Penetration(Center + new Vector3(0f, 0f, 4.01f), Quaternion.identity), Vector3.zero, "前後");
            AssertVector(Penetration(Center + new Vector3(2.01f, 0f, 0f), Quaternion.identity), Vector3.zero, "左右");
        }

        [Test]
        public void 上下の範囲が重ならない箱は重ならない()
        {
            AssertVector(Penetration(Center + new Vector3(0f, 1.01f, 3.5f), Quaternion.identity), Vector3.zero);
            Vector3 touching = Penetration(Center + new Vector3(0f, 0.99f, 3.5f), Quaternion.identity);
            AssertVector(touching, new Vector3(0f, 0f, -0.5f), "上下が少しでも重なれば、水平に押す");
        }

        [Test]
        public void 同じ傾きの斜面の上でも重なりの向きは水平()
        {
            Quaternion slope = Quaternion.Euler(10f, 0f, 0f);
            Vector3 otherCenter = Center + slope * new Vector3(0f, 0f, 3.5f);
            Vector3 result = Penetration(otherCenter, slope, rotation: slope);

            float sin = Mathf.Sin(10f * Mathf.Deg2Rad);
            float cos = Mathf.Cos(10f * Mathf.Deg2Rad);
            float depth = 2f * (Half.y * sin + Half.z * cos) - 3.5f * cos;
            AssertVector(result, new Vector3(0f, 0f, -depth));
        }

        [Test]
        public void 状態がなければ接触を計算しない()
        {
            Assert.That(Sync.ContactStatus(false, true, 0f), Is.EqualTo(Sync.ContactNone));
        }

        [Test]
        public void 停止の印の状態は古くても使い進めず速度は0()
        {
            Assert.That(Sync.ContactStatus(true, true, 60f), Is.EqualTo(Sync.ContactUse));
            var position = new Vector3(1f, 2f, 3f);
            var velocity = new Vector3(0f, 0f, 0.04f);
            AssertVector(Sync.ContactPosition(position, velocity, true, 60f, 0.25f), position);
            Quaternion rotation = Quaternion.Euler(0f, 30f, 0f);
            Assert.That(Quaternion.Angle(Sync.ContactRotation(rotation, new Vector3(0f, 0.5f, 0f), true, 60f, 0.25f), rotation), Is.LessThan(0.01f));
            AssertVector(Sync.ContactVelocity(velocity, true), Vector3.zero);
        }

        [Test]
        public void 停止の印のない状態は1秒以内なら先読みの上限まで速度で進め1秒より古ければ打ち切る()
        {
            Assert.That(Sync.ContactStatus(true, false, 0.9f), Is.EqualTo(Sync.ContactUse));
            Assert.That(Sync.ContactStatus(true, false, Sync.ContactMaxAge + 0.01f), Is.EqualTo(Sync.ContactStale));

            var position = new Vector3(1f, 2f, 3f);
            var velocity = new Vector3(0f, 0f, 2f);
            AssertVector(Sync.ContactPosition(position, velocity, false, 0.1f, 0.25f), position + velocity * 0.1f, "経った分だけ進める");
            AssertVector(Sync.ContactPosition(position, velocity, false, 0.9f, 0.25f), position + velocity * 0.25f, "先読みの上限まで");
            Quaternion rotation = Quaternion.identity;
            var angular = new Vector3(0f, Mathf.PI, 0f);
            Assert.That(Quaternion.Angle(Sync.ContactRotation(rotation, angular, false, 0.1f, 0.25f), Quaternion.Euler(0f, 18f, 0f)), Is.LessThan(0.01f));
            AssertVector(Sync.ContactVelocity(velocity, false), velocity);
        }
    }
}
