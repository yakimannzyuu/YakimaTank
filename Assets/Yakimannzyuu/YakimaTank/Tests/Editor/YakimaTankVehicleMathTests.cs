using NUnit.Framework;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    public class YakimaTankVehicleMathTests
    {
        private const float Drive = 12000f;
        private const float MaxForward = 10f;
        private const float MaxReverse = 4f;
        private const float Brake = 60000f;
        private const float IdleBrake = 20000f;

        [Test]
        public void 入力は前後と旋回をマイナス1から1にブレーキを0から1に丸める()
        {
            var gameObject = new GameObject("Vehicle");
            try
            {
                var vehicle = gameObject.AddComponent<YakimaTankVehicle>();
                vehicle.SetDriveInput(2f, -3f, 5f);
                Assert.That(new[] { vehicle.throttleInput, vehicle.steerInput, vehicle.brakeInput }, Is.EqualTo(new[] { 1f, -1f, 1f }));
                vehicle.SetDriveInput(0.5f, 0.25f, -1f);
                Assert.That(new[] { vehicle.throttleInput, vehicle.steerInput, vehicle.brakeInput }, Is.EqualTo(new[] { 0.5f, 0.25f, 0f }));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void 前進の入力では入力の割合だけ駆動トルクを掛ける()
        {
            Assert.That(YakimaTankVehicle.CalculateDriveTorque(0.5f, 2f, Drive, MaxForward, MaxReverse), Is.EqualTo(6000f));
        }

        [Test]
        public void 後退の入力では負の駆動トルクを掛ける()
        {
            Assert.That(YakimaTankVehicle.CalculateDriveTorque(-1f, -1f, Drive, MaxForward, MaxReverse), Is.EqualTo(-Drive));
        }

        [Test]
        public void 進む向きの最高速度に達したら駆動しない()
        {
            Assert.That(YakimaTankVehicle.CalculateDriveTorque(1f, MaxForward, Drive, MaxForward, MaxReverse), Is.EqualTo(0f));
            Assert.That(YakimaTankVehicle.CalculateDriveTorque(-1f, -MaxReverse, Drive, MaxForward, MaxReverse), Is.EqualTo(0f));
        }

        [Test]
        public void 逆へ動いている間は入力の向きに駆動しない()
        {
            Assert.That(YakimaTankVehicle.CalculateDriveTorque(1f, -2f, Drive, MaxForward, MaxReverse), Is.EqualTo(0f));
            Assert.That(YakimaTankVehicle.CalculateDriveTorque(-1f, 2f, Drive, MaxForward, MaxReverse), Is.EqualTo(0f));
        }

        [Test]
        public void ほぼ止まっていれば逆向きの入力でも駆動する()
        {
            Assert.That(YakimaTankVehicle.CalculateDriveTorque(1f, -0.2f, Drive, MaxForward, MaxReverse), Is.EqualTo(Drive));
        }

        [Test]
        public void 入力がなければ駆動しない()
        {
            Assert.That(YakimaTankVehicle.CalculateDriveTorque(0f, 0f, Drive, MaxForward, MaxReverse), Is.EqualTo(0f));
        }

        [Test]
        public void ブレーキ入力の割合だけブレーキトルクを掛ける()
        {
            Assert.That(YakimaTankVehicle.CalculateBrakeTorque(1f, 0f, 0.5f, 3f, Brake, IdleBrake), Is.EqualTo(30000f));
        }

        [Test]
        public void 何も入力していない間は停止中のブレーキを掛ける()
        {
            Assert.That(YakimaTankVehicle.CalculateBrakeTorque(0f, 0f, 0f, 0f, Brake, IdleBrake), Is.EqualTo(IdleBrake));
        }

        [Test]
        public void 旋回の入力だけなら停止中のブレーキを掛けない()
        {
            Assert.That(YakimaTankVehicle.CalculateBrakeTorque(0f, 1f, 0f, 0f, Brake, IdleBrake), Is.EqualTo(0f));
        }

        [Test]
        public void 進行方向と逆の入力ではまずブレーキで止める()
        {
            Assert.That(YakimaTankVehicle.CalculateBrakeTorque(-1f, 0f, 0f, 3f, Brake, IdleBrake), Is.EqualTo(Brake));
        }

        [Test]
        public void ブレーキ入力は0から1に丸める()
        {
            Assert.That(YakimaTankVehicle.CalculateBrakeTorque(1f, 0f, 2f, 3f, Brake, IdleBrake), Is.EqualTo(Brake));
        }

        [Test]
        public void 旋回の角速度は目標へ角加速度の分だけ近づく()
        {
            Assert.That(YakimaTankVehicle.CalculateYawRate(0f, 1f, 30f, 90f, 0.02f), Is.EqualTo(1.8f).Within(1e-5f));
        }

        [Test]
        public void 旋回の角速度は目標を越えない()
        {
            Assert.That(YakimaTankVehicle.CalculateYawRate(29.5f, 1f, 30f, 90f, 0.02f), Is.EqualTo(30f).Within(1e-5f));
        }

        [Test]
        public void 旋回の入力を離すと角速度は0へ戻る()
        {
            Assert.That(YakimaTankVehicle.CalculateYawRate(10f, 0f, 30f, 90f, 0.02f), Is.EqualTo(8.2f).Within(1e-5f));
        }

        [Test]
        public void 左への旋回は負の角速度になる()
        {
            Assert.That(YakimaTankVehicle.CalculateYawRate(0f, -1f, 30f, 90f, 1f), Is.EqualTo(-30f).Within(1e-5f));
        }
    }
}
