using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// VR の入力アダプター。左手で走行、右手で砲と射撃。座席が有効にしている間だけ書く。
    /// 手の位置と向きは車体から見た値で扱い、グリップを握った瞬間の手を基準にする。降車（ジャンプ）は座席が受ける（VRCStation の標準の降り方は止めてある）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankVRInput : UdonSharpBehaviour
    {
        private const string LeftGripAxis = "Oculus_CrossPlatform_PrimaryHandTrigger";
        private const string RightGripAxis = "Oculus_CrossPlatform_SecondaryHandTrigger";
        private const string LeftTriggerAxis = "Oculus_CrossPlatform_PrimaryIndexTrigger";
        private const string RightTriggerAxis = "Oculus_CrossPlatform_SecondaryIndexTrigger";

        [Header("操作（Play 中に再適用できる）")]
        [Tooltip("グリップ・トリガーを握った（引いた）とみなす値（0〜1）")]
        public float gripThreshold = 0.5f;

        [Tooltip("ねじりの最大角（度）。手の先をこれだけ左右・上下に向けると入力が端になる")]
        public float maxTwistAngle = 45f;

        [Tooltip("前後の最大距離（m。左手だけ）。これだけ動かすと入力が端になる")]
        public float maxPushDistance = 0.15f;

        [Tooltip("前進後退入力がこれより後ろ（-側）にある間は後退のギア")]
        public float reverseThreshold = 0.5f;

        [Tooltip("向き・前後・トリガーを -1〜1・0〜1 にした後に掛ける不感帯")]
        public float deadzone = 0.1f;

        [Header("生成で決まる値")]
        public YakimaTankControlState control;

        /// <summary>手の位置と向きを、この Transform（車体）から見た値にする。</summary>
        public Transform vehicleBody;

        [Header("状態（実行中に変わる）")]
        public bool isActive;

        private bool _leftGripped;
        private Vector3 _leftOrigin;
        private Quaternion _leftOriginRotation = Quaternion.identity;
        private bool _rightGripped;
        private Quaternion _rightOriginRotation = Quaternion.identity;

        public void _SetActive(bool active)
        {
            isActive = active;
            _leftGripped = false;
            _rightGripped = false;
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

            Quaternion toBody = Quaternion.Inverse(vehicleBody.rotation);

            VRCPlayerApi.TrackingData left = player.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand);
            Vector3 leftPosition = toBody * (left.position - vehicleBody.position);
            Quaternion leftRotation = toBody * left.rotation;
            bool leftGripped = IsPressed(Input.GetAxisRaw(LeftGripAxis), gripThreshold);
            if (leftGripped && !_leftGripped)
            {
                _leftOrigin = leftPosition;
                _leftOriginRotation = leftRotation;
            }

            _leftGripped = leftGripped;
            _ApplyLeftHand(
                leftGripped,
                leftGripped ? YawAngle(_leftOriginRotation, leftRotation) : 0f,
                leftGripped ? leftPosition.z - _leftOrigin.z : 0f,
                Input.GetAxisRaw(LeftTriggerAxis));

            VRCPlayerApi.TrackingData right = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
            Quaternion rightRotation = toBody * right.rotation;
            bool rightGripped = IsPressed(Input.GetAxisRaw(RightGripAxis), gripThreshold);
            if (rightGripped && !_rightGripped)
            {
                _rightOriginRotation = rightRotation;
            }

            _rightGripped = rightGripped;
            _ApplyRightHand(
                rightGripped,
                rightGripped ? YawAngle(_rightOriginRotation, rightRotation) : 0f,
                rightGripped ? PitchAngle(_rightOriginRotation, rightRotation) : 0f,
                Input.GetAxisRaw(RightTriggerAxis));
        }

        /// <summary>
        /// 左手：グリップ＋先の左右の向きでハンドル、グリップ＋前後で前進後退入力とギア、トリガーでアクセル。
        /// yawAngle は基準から右へ向けた角度（度）、push は基準から前へ動いた距離（m）。
        /// </summary>
        public void _ApplyLeftHand(bool gripped, float yawAngle, float push, float trigger)
        {
            control.throttle = TriggerInput(trigger, deadzone);
            if (!gripped)
            {
                control.steering = 0f;
                control.driveAxis = 0f;
                control.gear = YakimaTankControlState.GearForward;
                return;
            }

            control.steering = AngleInput(yawAngle, maxTwistAngle, deadzone);
            control.driveAxis = PushInput(push, maxPushDistance, deadzone);
            control.gear = GearFromDriveAxis(control.driveAxis, reverseThreshold);
        }

        /// <summary>
        /// 右手：グリップ＋先の左右の向きで砲の左右、グリップ＋先の上下の向きで砲の上下、トリガーで射撃。
        /// yawAngle は基準から右へ向けた角度、pitchAngle は基準から先を上げた角度（度）。
        /// </summary>
        public void _ApplyRightHand(bool gripped, float yawAngle, float pitchAngle, float trigger)
        {
            control.fire = IsPressed(trigger, gripThreshold);
            if (!gripped)
            {
                control.turretYawInput = 0f;
                control.gunPitchInput = 0f;
                return;
            }

            control.turretYawInput = AngleInput(yawAngle, maxTwistAngle, deadzone);
            control.gunPitchInput = AngleInput(pitchAngle, maxTwistAngle, deadzone);
        }

        public static bool IsPressed(float value, float threshold)
        {
            return value >= threshold;
        }

        /// <summary>
        /// 基準の向きから今の向きまでに、車体の上の軸（Y）まわりに回った角度（度、-180〜180）。先を右へ向けると正。
        /// 手のコントローラーごとの軸の違いに左右されないよう、手の軸ではなく車体の軸で測る。
        /// </summary>
        public static float YawAngle(Quaternion origin, Quaternion current)
        {
            Quaternion delta = current * Quaternion.Inverse(origin);
            return HalfAngleToDegrees(delta.y, delta.w);
        }

        /// <summary>基準の向きから今の向きまでに、車体の右の軸（X）まわりに回った角度（度、-180〜180）。先を上げると正。</summary>
        public static float PitchAngle(Quaternion origin, Quaternion current)
        {
            Quaternion delta = current * Quaternion.Inverse(origin);
            return -HalfAngleToDegrees(delta.x, delta.w);
        }

        private static float HalfAngleToDegrees(float axisComponent, float w)
        {
            float angle = 2f * Mathf.Atan2(axisComponent, w) * Mathf.Rad2Deg;
            if (angle > 180f)
            {
                angle -= 360f;
            }
            else if (angle < -180f)
            {
                angle += 360f;
            }

            return angle;
        }

        /// <summary>角度を最大角で -1〜1 にする（正の角度が正）。</summary>
        public static float AngleInput(float angle, float maxAngle, float deadzone)
        {
            if (maxAngle <= 0f)
            {
                return 0f;
            }

            return ApplyDeadzone(Mathf.Clamp(angle / maxAngle, -1f, 1f), deadzone);
        }

        /// <summary>前後の距離を -1〜1 にする。前へ押すと正。</summary>
        public static float PushInput(float push, float maxPushDistance, float deadzone)
        {
            if (maxPushDistance <= 0f)
            {
                return 0f;
            }

            return ApplyDeadzone(Mathf.Clamp(push / maxPushDistance, -1f, 1f), deadzone);
        }

        public static float TriggerInput(float trigger, float deadzone)
        {
            return ApplyDeadzone(Mathf.Clamp01(trigger), deadzone);
        }

        /// <summary>前進後退入力が後退のしきい値より後ろにある間だけ後退。</summary>
        public static int GearFromDriveAxis(float driveAxis, float reverseThreshold)
        {
            return driveAxis < -reverseThreshold ? YakimaTankControlState.GearReverse : YakimaTankControlState.GearForward;
        }

        /// <summary>不感帯より小さい値を 0 にし、残りを 0〜1 に引き伸ばす（符号は保つ）。</summary>
        public static float ApplyDeadzone(float value, float deadzone)
        {
            float amount = Mathf.Abs(value);
            if (deadzone >= 1f || amount <= deadzone)
            {
                return 0f;
            }

            float scaled = Mathf.Clamp01((amount - Mathf.Max(deadzone, 0f)) / (1f - Mathf.Max(deadzone, 0f)));
            return value < 0f ? -scaled : scaled;
        }
    }
}
