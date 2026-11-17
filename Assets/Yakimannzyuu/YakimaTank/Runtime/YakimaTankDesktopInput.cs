using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// Desktop の入力アダプター。座席が有効にしている間だけ、キーと視点を共通操作値に書く。
    /// 降車（Space）は VRCStation の標準に任せる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankDesktopInput : UdonSharpBehaviour
    {
        /// <summary>視点の左右の最大（度）。後ろを向くと端になる。</summary>
        public const float MaxViewYaw = 180f;

        /// <summary>視点の上下の最大（度）。真上・真下で端になる。</summary>
        public const float MaxViewPitch = 90f;

        [Header("生成で決まる値")]
        public YakimaTankControlState control;

        /// <summary>視点の向きを、この Transform（車体）から見た向きにする。</summary>
        public Transform vehicleBody;

        [Header("状態（実行中に変わる）")]
        public bool isActive;

        public void _SetActive(bool active)
        {
            isActive = active;
        }

        private void Update()
        {
            if (!isActive || !Utilities.IsValid(control) || !Utilities.IsValid(vehicleBody))
            {
                return;
            }

            VRCPlayerApi player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player))
            {
                return;
            }

            Quaternion head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
            Vector3 viewForward = Quaternion.Inverse(vehicleBody.rotation) * (head * Vector3.forward);
            _ApplyKeys(
                Input.GetKey(KeyCode.W),
                Input.GetKey(KeyCode.S),
                Input.GetKey(KeyCode.A),
                Input.GetKey(KeyCode.D),
                Input.GetMouseButton(0),
                viewForward);
        }

        /// <summary>キーと、車体から見た視点の前の向きから、共通操作値を書く。</summary>
        public void _ApplyKeys(bool forward, bool back, bool left, bool right, bool fire, Vector3 viewForward)
        {
            control.driveAxis = DriveAxis(forward, back);
            control.gear = Gear(forward, back);
            control.throttle = Throttle(forward, back);
            control.steering = Steering(left, right);
            control.turretYawInput = ViewYawInput(viewForward);
            control.gunPitchInput = ViewPitchInput(viewForward);
            control.fire = fire;
        }

        /// <summary>W で 1、S で -1。両方・どちらもなしは 0。</summary>
        public static float DriveAxis(bool forward, bool back)
        {
            if (forward == back)
            {
                return 0f;
            }

            return forward ? 1f : -1f;
        }

        /// <summary>S だけを押している間だけ後退。ほかは前進。</summary>
        public static int Gear(bool forward, bool back)
        {
            return back && !forward ? YakimaTankControlState.GearReverse : YakimaTankControlState.GearForward;
        }

        /// <summary>W か S の片方だけを押している間は踏み切る。</summary>
        public static float Throttle(bool forward, bool back)
        {
            return forward != back ? 1f : 0f;
        }

        /// <summary>D で 1（右）、A で -1。両方・どちらもなしは 0。</summary>
        public static float Steering(bool left, bool right)
        {
            if (left == right)
            {
                return 0f;
            }

            return right ? 1f : -1f;
        }

        /// <summary>車体から見た視点の左右。右が正、±180 度を ±1 にする。</summary>
        public static float ViewYawInput(Vector3 viewForward)
        {
            float yaw = Mathf.Atan2(viewForward.x, viewForward.z) * Mathf.Rad2Deg;
            return Mathf.Clamp(yaw / MaxViewYaw, -1f, 1f);
        }

        /// <summary>車体から見た視点の上下。上が正、±90 度を ±1 にする。</summary>
        public static float ViewPitchInput(Vector3 viewForward)
        {
            float length = viewForward.magnitude;
            if (length <= 0f)
            {
                return 0f;
            }

            float pitch = Mathf.Asin(Mathf.Clamp(viewForward.y / length, -1f, 1f)) * Mathf.Rad2Deg;
            return Mathf.Clamp(pitch / MaxViewPitch, -1f, 1f);
        }
    }
}
