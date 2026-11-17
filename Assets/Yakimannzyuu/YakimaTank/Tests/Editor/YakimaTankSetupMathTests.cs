using NUnit.Framework;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    public class YakimaTankSetupMathTests
    {
        [Test]
        public void 自動のばねは静止の荷重でサスペンションの長さの25パーセントだけ縮む()
        {
            float spring = YakimaTankSetupMath.AutoSuspensionSpring(10000f, 10, 0.2f);
            float load = 10000f * YakimaTankSetupMath.Gravity / 10;
            Assert.That(load / spring, Is.EqualTo(0.05f).Within(1e-5f));
        }

        [Test]
        public void 自動の減衰は臨界減衰の30パーセント()
        {
            float damper = YakimaTankSetupMath.AutoSuspensionDamper(40000f, 1000f, 10);
            Assert.That(damper, Is.EqualTo(0.3f * 2f * Mathf.Sqrt(40000f * 100f)).Within(1e-3f));
        }

        [Test]
        public void 転輪が0本や質量が0なら自動の値は0()
        {
            Assert.That(YakimaTankSetupMath.AutoSuspensionSpring(10000f, 0, 0.2f), Is.EqualTo(0f));
            Assert.That(YakimaTankSetupMath.AutoSuspensionSpring(0f, 10, 0.2f), Is.EqualTo(0f));
            Assert.That(YakimaTankSetupMath.AutoSuspensionDamper(1f, 1000f, 0), Is.EqualTo(0f));
        }

        [Test]
        public void 転輪の半径は上下と前後の半分の平均()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(0.3f, 0.8f, 0.9f));
            Assert.That(YakimaTankSetupMath.WheelRadiusFromBounds(bounds), Is.EqualTo(0.425f).Within(1e-5f));
        }

        [Test]
        public void WheelColliderは静止したとき転輪の中心がメッシュの中心に来る高さに置く()
        {
            Vector3 position = YakimaTankSetupMath.WheelColliderPosition(new Vector3(1f, 0.4f, 2f), 0.2f, 0.5f);
            Assert.That(position, Is.EqualTo(new Vector3(1f, 0.5f, 2f)));
        }

        [Test]
        public void 車体の箱の底は転輪の中心より下げない()
        {
            var body = new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(2f, 1.6f, 4f));
            Bounds box = YakimaTankSetupMath.BodyColliderBounds(body, 0.4f);
            Assert.That(box.min.y, Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(box.max.y, Is.EqualTo(1.6f).Within(1e-5f));
            Assert.That(box.size.x, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void 車体が転輪より高ければ箱の底は車体の底のまま()
        {
            var body = new Bounds(new Vector3(0f, 1.5f, 0f), new Vector3(2f, 1f, 4f));
            Assert.That(YakimaTankSetupMath.BodyColliderBounds(body, 0.4f).min.y, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void 乗車の判定の箱は前後と左右が車体のコライダーより各面に余白だけ外でモデルの張り出しを含めない()
        {
            float margin = YakimaTankSetupMath.BoardingMargin;
            var body = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(2f, 0.8f, 4f));
            var model = new Bounds(new Vector3(0f, 0.87f, 1.5f), new Vector3(2.6f, 1.74f, 7f));

            Bounds area = YakimaTankSetupMath.BoardingAreaBounds(body, model);

            Assert.That(area.min.x, Is.EqualTo(-1f - margin).Within(1e-5f));
            Assert.That(area.max.x, Is.EqualTo(1f + margin).Within(1e-5f), "転輪の外側 1.3 ではない");
            Assert.That(area.min.z, Is.EqualTo(-2f - margin).Within(1e-5f));
            Assert.That(area.max.z, Is.EqualTo(2f + margin).Within(1e-5f), "砲身の先 5 ではない");
        }

        [Test]
        public void 乗車の判定の箱の上下はモデル全体と車体のコライダーを合わせた範囲より各面に余白だけ外()
        {
            float margin = YakimaTankSetupMath.BoardingMargin;
            var body = new Bounds(new Vector3(0f, 0.9f, 0f), new Vector3(2f, 0.8f, 4f));
            var model = new Bounds(new Vector3(0f, 0.87f, 0f), new Vector3(2.6f, 1.74f, 4f));
            var lowModel = new Bounds(new Vector3(0f, 0.6f, 0f), new Vector3(2.6f, 0.4f, 4f));

            Bounds area = YakimaTankSetupMath.BoardingAreaBounds(body, model);

            Assert.That(area.min.y, Is.EqualTo(0f - margin).Within(1e-5f), "モデルの下端より外");
            Assert.That(area.max.y, Is.EqualTo(1.74f + margin).Within(1e-5f), "砲塔の上端より外");
            Assert.That(YakimaTankSetupMath.BoardingAreaBounds(body, lowModel).max.y, Is.EqualTo(1.3f + margin).Within(1e-5f),
                "モデルが車体のコライダーより低くても、車体のコライダーの上端より外");
        }

        [Test]
        public void 砲塔にメッシュがないときの命中用のトリガーは前後と左右が車体のコライダーで上端がモデル全体の上端()
        {
            var body = new Bounds(new Vector3(0f, 0.85f, 0f), new Vector3(2f, 0.9f, 4f));
            var model = new Bounds(new Vector3(0f, 1.2f, 0.4f), new Vector3(2.4f, 2.4f, 5f));
            Bounds upper = YakimaTankSetupMath.UpperHitboxBounds(body, model);
            Assert.That(Vector3.Distance(upper.min, body.min), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(upper.max, new Vector3(body.max.x, model.max.y, body.max.z)), Is.LessThan(1e-5f));

            var low = new Bounds(new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 4f));
            Assert.That(YakimaTankSetupMath.UpperHitboxBounds(body, low).max.y, Is.EqualTo(body.max.y).Within(1e-5f), "モデルが車体のコライダーより低くても、車体の箱より小さくしない");
        }
    }
}
