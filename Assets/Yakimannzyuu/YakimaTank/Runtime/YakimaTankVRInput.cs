using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// VR の入力アダプター。左手で走行、右手で砲と射撃。座席が有効にしている間だけ書く。
    /// 手の位置と向きは車体から見た値で扱い、グリップを握った瞬間の手を基準にする。降車（ジャンプ）は VRCStation の標準に任せる。
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

        [Tooltip("ねじりの最大角（度）。これだけねじると入力が端になる")]
        public float maxTwistAngle = 45f;

        [Tooltip("前後の最大距離（m）。これだけ動かすと入力が端になる")]
        public float maxPushDistance = 0.15f;

        [Tooltip("前進後退入力がこれより後ろ（-側）にある間は後退のギア")]
        public float reverseThreshold = 0.5f;

        [Tooltip("ねじり・前後・トリガーを -1〜1・0〜1 にした後に掛ける不感帯")]
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
        private Vector3 _rightOrigin;
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
                leftGripped ? TwistAngle(_leftOriginRotation, leftRotation) : 0f,
                leftGripped ? leftPosition.z - _leftOrigin.z : 0f,
                Input.GetAxisRaw(LeftTriggerAxis));

            VRCPlayerApi.TrackingData right = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand);
            Vector3 rightPosition = toBody * (right.position - vehicleBody.position);
            Quaternion rightRotation = toBody * right.rotation;
            bool rightGripped = IsPressed(Input.GetAxisRaw(RightGripAxis), gripThreshold);
            if (rightGripped && !_rightGripped)
            {
                _rightOrigin = rightPosition;
                _rightOriginRotation = rightRotation;
            }

            _rightGripped = rightGripped;
            _ApplyRightHand(
                rightGripped,
                rightGripped ? TwistAngle(_rightOriginRotation, rightRotation) : 0f,
                rightGripped ? rightPosition.z - _rightOrigin.z : 0f,
                Input.GetAxisRaw(RightTriggerAxis));
        }

        /// <summary>
        /// 左手：グリップ＋ねじりでハンドル、グリップ＋前後で前進後退入力とギア、トリガーでアクセル。
        /// twistAngle は基準から車体の前の軸まわりに回った角度（度）、push は基準から前へ動いた距離（m）。
        /// </summary>
        public void _ApplyLeftHand(bool gripped, float twistAngle, float push, float trigger)
        {
            control.throttle = TriggerInput(trigger, deadzone);
            if (!gripped)
            {
                control.steering = 0f;
                control.driveAxis = 0f;
                control.gear = YakimaTankControlState.GearForward;
                return;
            }

            control.steering = TwistInput(twistAngle, maxTwistAngle, deadzone);
            control.driveAxis = PushInput(push, maxPushDistance, deadzone);
            control.gear = GearFromDriveAxis(control.driveAxis, reverseThreshold);
        }

        /// <summary>右手：グリップ＋ねじりで砲の左右、グリップ＋前後で砲の上下（前で上）、トリガーで射撃。</summary>
        public void _ApplyRightHand(bool gripped, float twistAngle, float push, float trigger)
        {
            control.fire = IsPressed(trigger, gripThreshold);
            if (!gripped)
            {
                control.turretYawInput = 0f;
                control.gunPitchInput = 0f;
                return;
            }

            control.turretYawInput = TwistInput(twistAngle, maxTwistAngle, deadzone);
            control.gunPitchInput = PushInput(push, maxPushDistance, deadzone);
        }

        public static bool IsPressed(float value, float threshold)
        {
            return value >= threshold;
        }

        /// <summary>
        /// 基準の向きから今の向きまでに、車体の前の軸（Z）まわりに回った角度（度、-180〜180）。
        /// 正は、後ろから見て反時計回り（手首を左にひねる向き）。
        /// 手のコントローラーごとの軸の違いに左右されないよう、手の軸ではなく車体の前の軸で測る。
        /// </summary>
        public static float TwistAngle(Quaternion origin, Quaternion current)
        {
            Quaternion delta = current * Quaternion.Inverse(origin);
            float angle = 2f * Mathf.Atan2(delta.z, delta.w) * Mathf.Rad2Deg;
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

        /// <summary>ねじりを -1〜1 にする。左にひねると負（左）。</summary>
        public static float TwistInput(float twistAngle, float maxTwistAngle, float deadzone)
        {
            if (maxTwistAngle <= 0f)
            {
                return 0f;
            }

            return ApplyDeadzone(Mathf.Clamp(-twistAngle / maxTwistAngle, -1f, 1f), deadzone);
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
