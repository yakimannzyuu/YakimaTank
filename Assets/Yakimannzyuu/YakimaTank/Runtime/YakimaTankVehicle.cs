using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 戦車の走行。値はセットアップの生成と、Play 中の再適用（SetProgramVariable）で Editor から書く。
    /// 実行中に設定アセットは読まない（Udon から ScriptableObject は読めない）。
    /// 物理を計算するのはオーナーの画面だけ（同期の Udon が hasAuthority を切り替える）。車両同士の接触の速度の変化は、同期の Udon が求める。
    /// Manual にするのは、同じ GameObject の同期の Udon と同期の方式をそろえるため。同期変数は持たない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class YakimaTankVehicle : UdonSharpBehaviour
    {
        private const float InputDeadZone = 0.01f;

        /// <summary>これより速く逆へ動いている間は、入力の向きに駆動せずに止める。</summary>
        private const float OpposingSpeed = 0.5f;

        /// <summary>
        /// 押されている間、車輪の張り付きを外すために掛ける、転輪 1 つあたりの駆動のトルク（N·m）。
        /// 車体を動かす力にはならない大きさ。張り付きは、駆動のトルクが 0 でなければ外れる（0.4.0 の実装の最初の確かめ）。
        /// </summary>
        public const float ReleaseTorque = 1f;

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

        /// <summary>自分が座っている間、物理の更新ごとに読む共通操作値。</summary>
        public YakimaTankControlState control;

        /// <summary>最初の位置に戻したことを伝える同期の Udon。</summary>
        public YakimaTankVehicleSync sync;

        /// <summary>生存の状態。破壊の間は入力を受けず、停止中のブレーキで止める。</summary>
        public YakimaTankLife life;

        [Header("状態（実行中に変わる）")]
        public bool isReady;

        /// <summary>自分の画面で物理を計算してよい（オーナー）。外すと、入力を受けず、走行の物理の更新もしない。</summary>
        public bool hasAuthority = true;
        public int generation;

        /// <summary>自分（ローカルのプレイヤー）が座席に座っている。座席が書く。</summary>
        public bool localDriverSeated;

        public float throttleInput;
        public float steerInput;
        public float brakeInput;

        /// <summary>車体に与えている旋回の速さ（度/秒、右が正）。旋回の加速度で入力の目標へ近づける。</summary>
        public float turnRate;

        public bool hasSpawnPose;
        public Vector3 spawnPosition;
        public Quaternion spawnRotation;

        /// <summary>転輪の半径の平均（m）。押されている間のブレーキの減速を求める。</summary>
        public float wheelRadius;

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
            if (!hasAuthority)
            {
                return;
            }

            throttleInput = Mathf.Clamp(throttle, -1f, 1f);
            steerInput = Mathf.Clamp(steer, -1f, 1f);
            brakeInput = Mathf.Clamp01(brake);
        }

        /// <summary>
        /// 座席から呼ぶ。座っている間は共通操作値を読み、降りたら入力を 0 にして停止中のブレーキで止める。
        /// 座っていない間は共通操作値を読まないので、SetDriveInput を直接使える。
        /// </summary>
        public void _SetLocalDriver(bool seated)
        {
            localDriverSeated = seated;
            if (!seated)
            {
                SetDriveInput(0f, 0f, 0f);
            }
        }

        /// <summary>同期の Udon から呼ぶ。外したら入力と旋回の速さを 0 にし、転輪の駆動を外す。</summary>
        public void _SetAuthority(bool value)
        {
            hasAuthority = value;
            if (!value)
            {
                throttleInput = 0f;
                steerInput = 0f;
                brakeInput = 0f;
                turnRate = 0f;
                _ReleaseWheels(leftWheels);
                _ReleaseWheels(rightWheels);
            }
        }

        /// <summary>
        /// 設定を入れ直し、最初の位置に戻す。Start に頼らずに呼べる（Play 中の再適用・出し直しから呼ぶ）。
        /// オーナー以外では値だけを入れ、位置は同期に従う。
        /// </summary>
        public void _ApplySettingsAndReset()
        {
            _Initialize();
        }

        /// <summary>
        /// ゲームの中の復活（仕様書 0.6.0 の 1.3）。生存の状態のオーナー（車体のオーナー）が呼ぶ。
        /// Editor の「最初の位置に戻す」と入口を分け、中の初期化だけを共用する（車体の世代が進み、ほかの人の画面で補間せずに置かれる）。
        /// </summary>
        public void _Respawn()
        {
            _Initialize();
        }

        private void _Initialize()
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
                _ReleaseWheels(leftWheels);
                _ReleaseWheels(rightWheels);
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
            wheelRadius = (_SumRadius(leftWheels) + _SumRadius(rightWheels)) / (leftWheels.Length + rightWheels.Length);
            _ReleaseWheels(leftWheels);
            _ReleaseWheels(rightWheels);
            isReady = true;
            if (!hasAuthority)
            {
                return;
            }

            _StopBody();
            body.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
            body.position = spawnPosition;
            body.rotation = spawnRotation;
            if (Utilities.IsValid(sync))
            {
                sync._OnVehicleReset();
            }
        }

        private void FixedUpdate()
        {
            if (!isReady || !hasAuthority)
            {
                return;
            }

            // 車両同士の接触は速度だけを直す。走行は角速度と転輪のトルクを書くので上書きし合わない。
            // 接地していないと下で抜けるので、その前に足す。
            Vector3 push = Vector3.zero;
            if (Utilities.IsValid(sync))
            {
                body.velocity = body.velocity + sync._ContactVelocityChange();
                push = sync.contactPush;
            }

            if (Utilities.IsValid(life) && life._IsDestroyed())
            {
                // 破壊の間は入力を止め、停止中のブレーキにする（直接の SetDriveInput も上書きする）。
                SetDriveInput(0f, 0f, 0f);
            }
            else if (localDriverSeated && Utilities.IsValid(control))
            {
                SetDriveInput(ControlThrottle(control.throttle, control.gear), control.steering, control.brake);
            }

            Transform bodyTransform = body.transform;
            Vector3 forward = bodyTransform.forward;
            float forwardSpeed = Vector3.Dot(body.velocity, forward);
            int wheelCount = leftWheels.Length + rightWheels.Length;
            float motor = CalculateDriveTorque(throttleInput, forwardSpeed, driveTorque, maxForwardSpeed, maxReverseSpeed) / wheelCount;
            float brakeTotal = CalculateBrakeTorque(throttleInput, steerInput, brakeInput, forwardSpeed, brakeTorque, idleBrakeTorque);
            float brake = brakeTotal / wheelCount;
            float pushAlong = Vector3.Dot(push, forward);
            float deceleration = BrakeDeceleration(brakeTotal, wheelRadius, body.mass);
            if (ReleasesForPush(pushAlong, deceleration, motor != 0f))
            {
                // 押されている間は、ブレーキを外し、わずかな駆動のトルクで車輪の張り付きを外して、ブレーキの減速を速度で掛ける。
                // 押す加速の上限を上げてはがす案は採らない。張り付きは 1 ステップの速度の変化が大きいときだけはがれ、はがれると抵抗がなく飛ぶ。
                motor = pushAlong > 0f ? ReleaseTorque : -ReleaseTorque;
                brake = 0f;
                body.velocity = body.velocity + forward * (ApplyBrake(forwardSpeed, deceleration, Time.fixedDeltaTime) - forwardSpeed);
            }

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

        /// <summary>共通操作値から走行の前後の入力へ。アクセルの量を、ギアの向きにする。</summary>
        public static float ControlThrottle(float throttle, int gear)
        {
            float amount = Mathf.Clamp01(throttle);
            return gear < 0 ? -amount : amount;
        }

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

        public static float CalculateYawRate(float currentYawRate, float steer, float maxTurnRate, float turnAcceleration, float deltaTime)
        {
            float target = Mathf.Clamp(steer, -1f, 1f) * maxTurnRate;
            return Mathf.MoveTowards(currentYawRate, target, turnAcceleration * deltaTime);
        }

        /// <summary>ブレーキのトルク（全部の転輪の合計）で止める減速（m/s²）。</summary>
        public static float BrakeDeceleration(float brakeTorque, float wheelRadius, float mass)
        {
            return wheelRadius > 0f && mass > 0f ? brakeTorque / wheelRadius / mass : 0f;
        }

        /// <summary>
        /// 車両同士の接触で押されている間、車輪の張り付きを外すか（仕様書 0.4.0 の 1.5）。
        /// 駆動していない車輪は、低速でブレーキのトルクに関係なくその場に張り付く（WheelCollider）。押す加速の前後の分が、
        /// ブレーキの減速を超えている間だけ外す。横はタイヤの横の摩擦で受けるので見ない。
        /// </summary>
        public static bool ReleasesForPush(float pushAlong, float brakeDeceleration, bool driven)
        {
            return !driven && Mathf.Abs(pushAlong) > brakeDeceleration;
        }

        /// <summary>張り付きを外している間の、ブレーキの代わりの減速。</summary>
        public static float ApplyBrake(float speedAlong, float deceleration, float deltaTime)
        {
            return Mathf.MoveTowards(speedAlong, 0f, deceleration * deltaTime);
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

        private float _SumRadius(WheelCollider[] wheels)
        {
            float sum = 0f;
            for (int index = 0; index < wheels.Length; index++)
            {
                sum += wheels[index].radius;
            }

            return sum;
        }

        /// <summary>設定の検証に失敗したときも呼ぶので、足りない参照は飛ばして、外せる転輪だけ外す。</summary>
        private void _ReleaseWheels(WheelCollider[] wheels)
        {
            if (wheels == null)
            {
                return;
            }

            for (int index = 0; index < wheels.Length; index++)
            {
                WheelCollider wheel = wheels[index];
                if (Utilities.IsValid(wheel))
                {
                    wheel.motorTorque = 0f;
                    wheel.brakeTorque = 0f;
                }
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
