using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 戦車の走行。値はセットアップの生成と、Play 中の再適用（SetProgramVariable）で Editor から書く。
    /// 実行中に設定アセットは読まない（Udon から ScriptableObject は読めない）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankVehicle : UdonSharpBehaviour
    {
        private const float InputDeadZone = 0.01f;

        /// <summary>これより速く逆へ動いている間は、入力の向きに駆動せずに止める。</summary>
        private const float OpposingSpeed = 0.5f;

        [Header("走行（Play 中に再適用できる）")]
        public float mass = 20000f;
        public float driveTorque = 12000f;
        public float brakeTorque = 60000f;
        public float idleBrakeTorque = 20000f;
        public float maxForwardSpeed = 10f;
        public float maxReverseSpeed = 4f;
        public float maxTurnRate = 30f;
        public float turnAcceleration = 90f;
        public Vector3 centerOfMassOffset;

        [Header("生成で決まる値")]
        public Vector3 baseCenterOfMass;
        public Rigidbody body;
        public WheelCollider[] leftWheels;
        public WheelCollider[] rightWheels;
        public bool applyOnStart = true;

        [Header("状態（実行中に変わる）")]
        public bool isReady;
        public int generation;
        public float throttleInput;
        public float steerInput;
        public float brakeInput;

        /// <summary>車体に与えている旋回の速さ（度/秒、右が正）。旋回の加速度で入力の目標へ近づける。</summary>
        public float turnRate;

        public bool hasSpawnPose;
        public Vector3 spawnPosition;
        public Quaternion spawnRotation;

        private void Start()
        {
            if (applyOnStart)
            {
                _ApplySettingsAndReset();
            }
        }

        /// <summary>入力。throttle と steer は -1〜1、brake は 0〜1 に丸める。steer が正なら右へ回る。</summary>
        public void SetDriveInput(float throttle, float steer, float brake)
        {
            throttleInput = Mathf.Clamp(throttle, -1f, 1f);
            steerInput = Mathf.Clamp(steer, -1f, 1f);
            brakeInput = Mathf.Clamp01(brake);
        }

        /// <summary>
        /// 設定を入れ直し、最初の位置に戻す。Start に頼らずに呼べる（Play 中の再適用・出し直しから呼ぶ）。
        /// </summary>
        public void _ApplySettingsAndReset()
        {
            isReady = false;
            generation++;
            throttleInput = 0f;
            steerInput = 0f;
            brakeInput = 0f;
            turnRate = 0f;

            if (!_ValidateSettings())
            {
                _StopBody();
                return;
            }

            if (!hasSpawnPose)
            {
                spawnPosition = body.transform.position;
                spawnRotation = body.transform.rotation;
                hasSpawnPose = true;
            }

            body.mass = mass;
            body.centerOfMass = baseCenterOfMass + centerOfMassOffset;
            _StopBody();
            body.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            body.position = spawnPosition;
            body.rotation = spawnRotation;
            _ResetWheels(leftWheels);
            _ResetWheels(rightWheels);
            isReady = true;
        }

        private void FixedUpdate()
        {
            if (!isReady)
            {
                return;
            }

            Transform bodyTransform = body.transform;
            float forwardSpeed = Vector3.Dot(body.velocity, bodyTransform.forward);
            int wheelCount = leftWheels.Length + rightWheels.Length;
            float motor = CalculateDriveTorque(throttleInput, forwardSpeed, driveTorque, maxForwardSpeed, maxReverseSpeed) / wheelCount;
            float brake = CalculateBrakeTorque(throttleInput, steerInput, brakeInput, forwardSpeed, brakeTorque, idleBrakeTorque) / wheelCount;
            bool leftGrounded = _DriveWheels(leftWheels, motor, brake);
            bool rightGrounded = _DriveWheels(rightWheels, motor, brake);
            if (!leftGrounded && !rightGrounded)
            {
                turnRate = 0f;
                return;
            }

            // 左右の転輪の回転差で曲げると、WheelCollider の横の摩擦でその場ではほとんど回らない。
            // カジュアルな操作感を優先し、車体の旋回の速さを毎回与える。測った速さから加速すると、
            // 横の摩擦で毎回ほぼ 0 に戻され、旋回が積み上がらない。
            turnRate = CalculateYawRate(turnRate, steerInput, maxTurnRate, turnAcceleration, Time.fixedDeltaTime);
            if (Mathf.Abs(steerInput) <= InputDeadZone && turnRate == 0f)
            {
                // 旋回の入力がない間は、ぶつかったときなどの物理の回転をそのまま残す。
                return;
            }

            Vector3 up = bodyTransform.up;
            float yawRate = Vector3.Dot(body.angularVelocity, up) * Mathf.Rad2Deg;
            body.angularVelocity = body.angularVelocity + up * ((turnRate - yawRate) * Mathf.Deg2Rad);
        }

        /// <summary>全輪の合計の駆動トルク。進む向きの最高速度に達したら 0、逆へ動いている間は 0（先に止める）。</summary>
        public static float CalculateDriveTorque(float throttle, float forwardSpeed, float driveTorque, float maxForwardSpeed, float maxReverseSpeed)
        {
            if (throttle > InputDeadZone)
            {
                if (forwardSpeed < -OpposingSpeed || forwardSpeed >= maxForwardSpeed)
                {
                    return 0f;
                }

                return throttle * driveTorque;
            }

            if (throttle < -InputDeadZone)
            {
                if (forwardSpeed > OpposingSpeed || -forwardSpeed >= maxReverseSpeed)
                {
                    return 0f;
                }

                return throttle * driveTorque;
            }

            return 0f;
        }

        /// <summary>全輪の合計のブレーキトルク。ブレーキ入力・逆向きの入力・何も入力していない間に掛ける。</summary>
        public static float CalculateBrakeTorque(float throttle, float steer, float brake, float forwardSpeed, float brakeTorque, float idleBrakeTorque)
        {
            float result = Mathf.Clamp01(brake) * brakeTorque;
            bool opposing = throttle > InputDeadZone && forwardSpeed < -OpposingSpeed ||
                            throttle < -InputDeadZone && forwardSpeed > OpposingSpeed;
            if (opposing)
            {
                result = Mathf.Max(result, Mathf.Abs(throttle) * brakeTorque);
            }

            bool idle = Mathf.Abs(throttle) <= InputDeadZone && Mathf.Abs(steer) <= InputDeadZone;
            if (idle)
            {
                result = Mathf.Max(result, idleBrakeTorque);
            }

            return result;
        }

        /// <summary>次の上向きの角速度（度/秒）。steer × 最高角速度へ、角加速度で近づける。</summary>
        public static float CalculateYawRate(float currentYawRate, float steer, float maxTurnRate, float turnAcceleration, float deltaTime)
        {
            float target = Mathf.Clamp(steer, -1f, 1f) * maxTurnRate;
            return Mathf.MoveTowards(currentYawRate, target, turnAcceleration * deltaTime);
        }

        private bool _DriveWheels(WheelCollider[] wheels, float motor, float brake)
        {
            bool grounded = false;
            for (int index = 0; index < wheels.Length; index++)
            {
                WheelCollider wheel = wheels[index];
                wheel.motorTorque = motor;
                wheel.brakeTorque = brake;
                if (wheel.isGrounded)
                {
                    grounded = true;
                }
            }

            return grounded;
        }

        private void _ResetWheels(WheelCollider[] wheels)
        {
            for (int index = 0; index < wheels.Length; index++)
            {
                wheels[index].motorTorque = 0f;
                wheels[index].brakeTorque = 0f;
            }
        }

        private bool _ValidateSettings()
        {
            bool valid = true;
            if (!Utilities.IsValid(body))
            {
                Debug.LogError("[YakimaTank] 参照が足りません: body（Rigidbody）。車両は動き出しません。", this);
                valid = false;
            }

            if (!_WheelsValid(leftWheels) || !_WheelsValid(rightWheels))
            {
                Debug.LogError("[YakimaTank] 参照が足りません: leftWheels / rightWheels（WheelCollider）。車両は動き出しません。", this);
                valid = false;
            }

            valid &= _Positive(mass, "mass（質量）");
            valid &= _Positive(driveTorque, "driveTorque（駆動トルク）");
            valid &= _Positive(maxForwardSpeed, "maxForwardSpeed（前進の最高速度）");
            valid &= _Positive(turnAcceleration, "turnAcceleration（旋回の角加速度）");
            valid &= _NotNegative(brakeTorque, "brakeTorque（ブレーキトルク）");
            valid &= _NotNegative(idleBrakeTorque, "idleBrakeTorque（停止中のブレーキトルク）");
            valid &= _NotNegative(maxReverseSpeed, "maxReverseSpeed（後退の最高速度）");
            valid &= _NotNegative(maxTurnRate, "maxTurnRate（旋回の最高角速度）");
            return valid;
        }

        private bool _WheelsValid(WheelCollider[] wheels)
        {
            if (wheels == null || wheels.Length == 0)
            {
                return false;
            }

            for (int index = 0; index < wheels.Length; index++)
            {
                if (!Utilities.IsValid(wheels[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool _Positive(float value, string label)
        {
            if (value > 0f)
            {
                return true;
            }

            Debug.LogError("[YakimaTank] 設定が足りません: " + label + " が 0 以下です。車両は動き出しません。", this);
            return false;
        }

        private bool _NotNegative(float value, string label)
        {
            if (value >= 0f)
            {
                return true;
            }

            Debug.LogError("[YakimaTank] 設定が範囲外です: " + label + " が負です。車両は動き出しません。", this);
            return false;
        }

        private void _StopBody()
        {
            if (!Utilities.IsValid(body))
            {
                return;
            }

            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
