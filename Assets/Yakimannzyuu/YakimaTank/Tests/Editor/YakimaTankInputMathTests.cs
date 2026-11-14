using NUnit.Framework;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    public class YakimaTankInputMathTests
    {
        private const float Tolerance = 1e-4f;

        private GameObject _holder;
        private YakimaTankControlState _control;

        [SetUp]
        public void SetUp()
        {
            _holder = new GameObject("Controls");
            _control = _holder.AddComponent<YakimaTankControlState>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_holder);
        }

        private YakimaTankVRInput VRInput()
        {
            var input = _holder.AddComponent<YakimaTankVRInput>();
            input.control = _control;
            return input;
        }

        [Test]
        public void 降りたときの初期値はギアが前進で残りが0とOFF()
        {
            _control.steering = 1f;
            _control.leftLever = 1f;
            _control.rightLever = 1f;
            _control.throttle = 1f;
            _control.driveAxis = -1f;
            _control.gear = YakimaTankControlState.GearReverse;
            _control.brake = 1f;
            _control.turretYawInput = 1f;
            _control.gunPitchInput = -1f;
            _control.fire = true;

            _control._ResetToDefaults();

            Assert.That(new[] { _control.steering, _control.leftLever, _control.rightLever, _control.throttle, _control.driveAxis, _control.brake, _control.turretYawInput, _control.gunPitchInput },
                Is.All.EqualTo(0f));
            Assert.That(_control.gear, Is.EqualTo(YakimaTankControlState.GearForward));
            Assert.That(_control.fire, Is.False);
        }

        [TestCase(true, false, 1f, 1, 1f)]
        [TestCase(false, true, -1f, -1, 1f)]
        [TestCase(true, true, 0f, 1, 0f)]
        [TestCase(false, false, 0f, 1, 0f)]
        public void DesktopのWとSから前進後退入力とギアとアクセルを決める(bool w, bool s, float driveAxis, int gear, float throttle)
        {
            Assert.That(YakimaTankDesktopInput.DriveAxis(w, s), Is.EqualTo(driveAxis));
            Assert.That(YakimaTankDesktopInput.Gear(w, s), Is.EqualTo(gear));
            Assert.That(YakimaTankDesktopInput.Throttle(w, s), Is.EqualTo(throttle));
        }

        [TestCase(true, false, -1f)]
        [TestCase(false, true, 1f)]
        [TestCase(true, true, 0f)]
        [TestCase(false, false, 0f)]
        public void DesktopのAとDからハンドルを決めDで右(bool a, bool d, float steering)
        {
            Assert.That(YakimaTankDesktopInput.Steering(a, d), Is.EqualTo(steering));
        }

        [TestCase(0f, 0f, 1f, 0f)]
        [TestCase(1f, 0f, 0f, 0.5f)]
        [TestCase(-1f, 0f, 0f, -0.5f)]
        [TestCase(0f, 0f, -1f, 1f)]
        public void 車体から見た視点の左右を180度で1にして右を正にする(float x, float y, float z, float expected)
        {
            Assert.That(YakimaTankDesktopInput.ViewYawInput(new Vector3(x, y, z)), Is.EqualTo(expected).Within(Tolerance));
        }

        [TestCase(0f, 1f, 1f, 0.5f)]
        [TestCase(0f, 2f, 2f, 0.5f)]
        [TestCase(0f, 1f, 0f, 1f)]
        [TestCase(0f, -1f, 1f, -0.5f)]
        [TestCase(0f, 0f, 1f, 0f)]
        public void 車体から見た視点の上下を90度で1にして上を正にする(float x, float y, float z, float expected)
        {
            Assert.That(YakimaTankDesktopInput.ViewPitchInput(new Vector3(x, y, z)), Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void Desktopのキーと視点を共通操作値に書く()
        {
            var input = _holder.AddComponent<YakimaTankDesktopInput>();
            input.control = _control;

            input._ApplyKeys(false, true, false, true, true, new Vector3(1f, 0f, 0f));

            Assert.That(_control.driveAxis, Is.EqualTo(-1f));
            Assert.That(_control.gear, Is.EqualTo(YakimaTankControlState.GearReverse));
            Assert.That(_control.throttle, Is.EqualTo(1f));
            Assert.That(_control.steering, Is.EqualTo(1f));
            Assert.That(_control.turretYawInput, Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(_control.gunPitchInput, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(_control.fire, Is.True);
        }

        [TestCase(0.05f, 0.1f, 0f)]
        [TestCase(0.1f, 0.1f, 0f)]
        [TestCase(0.55f, 0.1f, 0.5f)]
        [TestCase(-0.55f, 0.1f, -0.5f)]
        [TestCase(1f, 0.1f, 1f)]
        [TestCase(0.3f, 0f, 0.3f)]
        public void 不感帯より小さい値は0にし残りを0から1に引き伸ばす(float value, float deadzone, float expected)
        {
            Assert.That(YakimaTankVRInput.ApplyDeadzone(value, deadzone), Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void ねじりは基準から車体の前の軸まわりに回った角度()
        {
            Quaternion origin = Quaternion.Euler(0f, 90f, 0f);

            Assert.That(YakimaTankVRInput.TwistAngle(origin, Quaternion.Euler(0f, 0f, 30f) * origin), Is.EqualTo(30f).Within(0.01f));
            Assert.That(YakimaTankVRInput.TwistAngle(origin, Quaternion.Euler(0f, 0f, -30f) * origin), Is.EqualTo(-30f).Within(0.01f));
            Assert.That(YakimaTankVRInput.TwistAngle(origin, Quaternion.Euler(30f, 0f, 0f) * origin), Is.EqualTo(0f).Within(0.01f), "前後に傾けただけではねじりにならない");
        }

        [TestCase(22.5f, 45f, 0f, -0.5f)]
        [TestCase(-45f, 45f, 0f, 1f)]
        [TestCase(-90f, 45f, 0f, 1f)]
        [TestCase(90f, 45f, 0f, -1f)]
        [TestCase(2f, 45f, 0.1f, 0f)]
        public void ねじりを最大角で1にして左へひねると負にする(float twist, float max, float deadzone, float expected)
        {
            Assert.That(YakimaTankVRInput.TwistInput(twist, max, deadzone), Is.EqualTo(expected).Within(Tolerance));
        }

        [TestCase(0.15f, 0.15f, 0f, 1f)]
        [TestCase(-0.075f, 0.15f, 0f, -0.5f)]
        [TestCase(0.3f, 0.15f, 0f, 1f)]
        [TestCase(-0.3f, 0.15f, 0f, -1f)]
        [TestCase(0.01f, 0.15f, 0.1f, 0f)]
        public void 前後の距離を最大距離で1にして前へ押すと正にする(float push, float max, float deadzone, float expected)
        {
            Assert.That(YakimaTankVRInput.PushInput(push, max, deadzone), Is.EqualTo(expected).Within(Tolerance));
        }

        [TestCase(0.05f, 0.1f, 0f)]
        [TestCase(1f, 0.1f, 1f)]
        [TestCase(1.5f, 0.1f, 1f)]
        [TestCase(-0.5f, 0.1f, 0f)]
        public void トリガーを0から1のアクセルにする(float trigger, float deadzone, float expected)
        {
            Assert.That(YakimaTankVRInput.TriggerInput(trigger, deadzone), Is.EqualTo(expected).Within(Tolerance));
        }

        [TestCase(-0.6f, 0.5f, -1)]
        [TestCase(-0.5f, 0.5f, 1)]
        [TestCase(0.2f, 0.5f, 1)]
        public void 前進後退入力が後退のしきい値より後ろにある間だけ後退のギア(float driveAxis, float threshold, int gear)
        {
            Assert.That(YakimaTankVRInput.GearFromDriveAxis(driveAxis, threshold), Is.EqualTo(gear));
        }

        [TestCase(0.5f, 0.5f, true)]
        [TestCase(0.49f, 0.5f, false)]
        public void しきい値以上で握ったとみなす(float value, float threshold, bool expected)
        {
            Assert.That(YakimaTankVRInput.IsPressed(value, threshold), Is.EqualTo(expected));
        }

        [Test]
        public void 左手はグリップ中のねじりと前後でハンドルと後退を決めトリガーでアクセル()
        {
            YakimaTankVRInput input = VRInput();
            input.deadzone = 0f;

            input._ApplyLeftHand(true, 45f, -0.15f, 1f);

            Assert.That(_control.steering, Is.EqualTo(-1f).Within(Tolerance));
            Assert.That(_control.driveAxis, Is.EqualTo(-1f).Within(Tolerance));
            Assert.That(_control.gear, Is.EqualTo(YakimaTankControlState.GearReverse));
            Assert.That(_control.throttle, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void 左のグリップを離すとハンドルと前進後退入力が0でギアは前進でアクセルはトリガーのまま()
        {
            YakimaTankVRInput input = VRInput();
            input._ApplyLeftHand(true, 45f, -0.15f, 1f);

            input._ApplyLeftHand(false, 45f, -0.15f, 0.8f);

            Assert.That(_control.steering, Is.EqualTo(0f));
            Assert.That(_control.driveAxis, Is.EqualTo(0f));
            Assert.That(_control.gear, Is.EqualTo(YakimaTankControlState.GearForward));
            Assert.That(_control.throttle, Is.EqualTo(YakimaTankVRInput.TriggerInput(0.8f, input.deadzone)).Within(Tolerance));
        }

        [Test]
        public void 右手はグリップ中のねじりと前後で砲の入力を決めトリガーで射撃()
        {
            YakimaTankVRInput input = VRInput();
            input.deadzone = 0f;

            input._ApplyRightHand(true, -45f, 0.15f, 0.8f);
            Assert.That(_control.turretYawInput, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(_control.gunPitchInput, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(_control.fire, Is.True);

            input._ApplyRightHand(false, -45f, 0.15f, 0f);
            Assert.That(_control.turretYawInput, Is.EqualTo(0f));
            Assert.That(_control.gunPitchInput, Is.EqualTo(0f));
            Assert.That(_control.fire, Is.False);
        }

        [TestCase(0.5f, 1, 0.5f)]
        [TestCase(0.5f, -1, -0.5f)]
        [TestCase(2f, 1, 1f)]
        [TestCase(-1f, 1, 0f)]
        [TestCase(0f, -1, 0f)]
        public void 走行の前後の入力はアクセルにギアの向きを掛ける(float throttle, int gear, float expected)
        {
            Assert.That(YakimaTankVehicle.ControlThrottle(throttle, gear), Is.EqualTo(expected).Within(Tolerance));
        }

        [Test]
        public void 降りると車両の入力を0にする()
        {
            var vehicleObject = new GameObject("Vehicle");
            try
            {
                var vehicle = vehicleObject.AddComponent<YakimaTankVehicle>();
                vehicle._SetLocalDriver(true);
                vehicle.SetDriveInput(1f, 1f, 1f);

                vehicle._SetLocalDriver(false);

                Assert.That(vehicle.localDriverSeated, Is.False);
                Assert.That(new[] { vehicle.throttleInput, vehicle.steerInput, vehicle.brakeInput }, Is.All.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(vehicleObject);
            }
        }

        [TestCase(0.5f, 0.125f, 0.25f)]
        [TestCase(-1f, -0.25f, 0.5f)]
        [TestCase(0f, 0f, 0f)]
        [TestCase(3f, 0.25f, 0.5f)]
        [TestCase(-3f, -0.25f, 0.5f)]
        public void ゲージの箱は0から値の分だけ伸ばし範囲外は端で止める(float value, float center, float width)
        {
            Assert.That(YakimaTankDebugPanel.GaugeCenter(value, 0.5f), Is.EqualTo(center).Within(Tolerance));
            Assert.That(YakimaTankDebugPanel.GaugeWidth(value, 0.5f), Is.EqualTo(width).Within(Tolerance));
        }
    }
}
