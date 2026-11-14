using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// デバッグ UI。指定した車両の共通操作値を、箱（Cube）の大きさで見せる。各自の画面だけの表示で、同期しない。
    /// 文字は英字だけ（日本語のフォントを同梱しないため）。見た目の部品は Editor のビルダーが作る。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankDebugPanel : UdonSharpBehaviour
    {
        /// <summary>ゲージの枠の半分の幅（パネルの座標）。-1〜1 の値をこの幅に割り当てる。</summary>
        public const float GaugeHalfWidth = 0.5f;

        [Header("表示する車両")]
        public YakimaTankVehicle vehicle;

        [Header("置き方")]
        [Tooltip("車両に付いて行く。外すと置いた場所に留まる")]
        public bool followVehicle = true;

        [Tooltip("付いて行くときの、車体から見た位置（m）")]
        public Vector3 followPosition = new Vector3(-2f, 2.5f, 3f);

        [Tooltip("付いて行くときの、車体から見た向き（度）")]
        public Vector3 followRotation = new Vector3(0f, -30f, 0f);

        [Header("見た目の部品（ビルダーが作る）")]
        public Transform steeringBar;
        public Transform leftLeverBar;
        public Transform rightLeverBar;
        public Transform throttleBar;
        public Transform driveAxisBar;
        public Transform turretYawBar;
        public Transform gunPitchBar;
        public GameObject gearForward;
        public GameObject gearReverse;
        public GameObject fireIndicator;

        private void LateUpdate()
        {
            if (!Utilities.IsValid(vehicle))
            {
                return;
            }

            if (followVehicle)
            {
                Transform body = vehicle.transform;
                transform.SetPositionAndRotation(body.TransformPoint(followPosition), body.rotation * Quaternion.Euler(followRotation));
            }

            YakimaTankControlState control = vehicle.control;
            if (!Utilities.IsValid(control))
            {
                return;
            }

            _SetBar(steeringBar, control.steering);
            _SetBar(leftLeverBar, control.leftLever);
            _SetBar(rightLeverBar, control.rightLever);
            _SetBar(throttleBar, control.throttle);
            _SetBar(driveAxisBar, control.driveAxis);
            _SetBar(turretYawBar, control.turretYawInput);
            _SetBar(gunPitchBar, control.gunPitchInput);
            bool reverse = control.gear < 0;
            _SetActive(gearForward, !reverse);
            _SetActive(gearReverse, reverse);
            _SetActive(fireIndicator, control.fire);
        }

        private void _SetBar(Transform bar, float value)
        {
            if (!Utilities.IsValid(bar))
            {
                return;
            }

            Vector3 position = bar.localPosition;
            position.x = GaugeCenter(value, GaugeHalfWidth);
            bar.localPosition = position;
            Vector3 scale = bar.localScale;
            scale.x = GaugeWidth(value, GaugeHalfWidth);
            bar.localScale = scale;
        }

        private void _SetActive(GameObject target, bool active)
        {
            if (Utilities.IsValid(target) && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        /// <summary>ゲージの箱の中心。0 の目盛りから値の分だけ伸ばした箱の真ん中。範囲外は端で止める。</summary>
        public static float GaugeCenter(float value, float halfWidth)
        {
            return Mathf.Clamp(value, -1f, 1f) * halfWidth * 0.5f;
        }

        /// <summary>ゲージの箱の幅。範囲外は端で止める。</summary>
        public static float GaugeWidth(float value, float halfWidth)
        {
            return Mathf.Abs(Mathf.Clamp(value, -1f, 1f)) * halfWidth;
        }
    }
}
