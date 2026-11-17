using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 武器の同期（仕様書 0.5.0）。ルートの子の `Weapon` に置き、車体とは別の所有権の領域にする（将来、武器だけを砲手に渡せる）。
    /// オーナーは入力から砲塔と砲身を回し、装填して撃ち、角度・装填・発射の番号と砲口を送る。オーナー以外は最新の角度へ寄せ、
    /// 発射の番号が変わったら見た目の砲弾を出す。命中を数えるのは撃った人のクライアントの砲弾だけ（0.5.0 は当たった車両の番号を記録するまで）。
    /// 計算は静的な関数に分けて EditMode で確かめる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class YakimaTankWeaponSync : UdonSharpBehaviour
    {
        public const byte SyncFormat = 1;
        public const int NoVehicle = -1;

        /// <summary>受けた上下の角度の範囲の確かめで許す幅（度）。設定を Play 中に直した直後の丸めを弾かないため。</summary>
        public const float AngleTolerance = 0.5f;

        /// <summary>角度がこれより変わったら、送る間隔ごとに送る（度）。</summary>
        public const float SendAngle = 0.05f;

        [Header("砲塔・武器（Play 中に再適用できる）")]
        [Tooltip("砲塔の回る速さ（度/秒）")]
        public float turretTurnRate = 30f;

        [Tooltip("砲身の回る速さ（度/秒）")]
        public float gunTurnRate = 15f;

        [Tooltip("砲身の上下の下限（度、上が正）")]
        public float gunPitchMin = -10f;

        [Tooltip("砲身の上下の上限（度、上が正）")]
        public float gunPitchMax = 20f;

        [Tooltip("弾速（m/s）")]
        public float shellSpeed = 200f;

        [Tooltip("砲弾に掛かる重力（m/s²）")]
        public float shellGravity = 9.81f;

        [Tooltip("砲弾の寿命（秒）")]
        public float shellLifetime = 3f;

        [Tooltip("装填の時間（秒）")]
        public float reloadTime = 3f;

        [Header("生成で決まる値")]
        public YakimaTankVehicleSync vehicleSync;
        public YakimaTankSeat seat;
        public YakimaTankControlState control;
        public Transform turret;
        public Transform barrel;

        /// <summary>砲口。前が撃つ向き。</summary>
        public Transform muzzle;

        /// <summary>砲塔の左右の軸（砲塔の親から見た、車体の上の向き）と最初の向き。モデルの軸を生成で解く。</summary>
        public Vector3 turretAxis = Vector3.up;
        public Quaternion turretBaseRotation = Quaternion.identity;

        /// <summary>砲身の上下の軸（砲身の親から見た、車体の右の向き）と最初の向き。</summary>
        public Vector3 barrelAxis = Vector3.right;
        public Quaternion barrelBaseRotation = Quaternion.identity;

        public YakimaTankShell[] shells = new YakimaTankShell[0];

        /// <summary>砲弾の光線のレイヤー。車両の物理の形（Walkthrough）とプレイヤーを除く。</summary>
        public int shellMask = -1;

        [Header("同期変数")]
        [UdonSynced] public byte syncFormat = SyncFormat;
        [UdonSynced] public float syncYaw;
        [UdonSynced] public float syncPitch;
        [UdonSynced] public bool syncLoaded = true;
        [UdonSynced] public int syncFireNumber;
        [UdonSynced] public Vector3 syncFirePosition;
        [UdonSynced] public Vector3 syncFireDirection;

        /// <summary>受けた状態の送った時刻（受ける側の Time.realtimeSinceStartup の軸）。OnDeserialization が入れる。</summary>
        public float incomingSendTime;

        [Header("状態（実行中に変わる）")]
        public bool isOwnerMode;
        public float yaw;
        public float pitch;
        public bool loaded = true;
        public float reloadTimer;
        public int fireNumber;
        public Vector3 firePosition;
        public Vector3 fireDirection;

        /// <summary>自分がこの武器で前に撃った最後の番号。所有権が移っても覚えておく（1.2 の命中の見分け方）。</summary>
        public int lastOwnFire;

        /// <summary>VR の入力（量で回す）か、Desktop の入力（目標の角度へ向く）か。座席が乗ったときに決める。</summary>
        public bool aimByRate;

        public int requestedCount;
        public int rejectedCount;

        /// <summary>受けた状態から出した見た目の砲弾の数（テストと確かめ用）。</summary>
        public int shownFires;

        /// <summary>撃った人のクライアントで、自分の砲弾が当たった車両の番号と、当たった数（0.5.0 は記録だけ）。</summary>
        public int lastHitVehicle = NoVehicle;
        public int hitCount;

        private bool _started;
        private bool _hasState;
        private float _lastUsedTime = float.NegativeInfinity;
        private float _targetYaw;
        private float _targetPitch;
        private bool _pendingSend;
        private float _lastRequestTime = float.NegativeInfinity;
        private float _sentYaw;
        private float _sentPitch;
        private int _nextShell;

        private void Start()
        {
            _started = true;
            if (_IsLocalOwner())
            {
                isOwnerMode = true;
                _pendingSend = true;
            }

            _ApplyAngles();
        }

        private bool _IsLocalOwner()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local))
            {
                // ClientSim を始めない走行のテスト（Udon VM だけ）。プレイヤーがいないので、自分が計算する。
                return true;
            }

            return Networking.IsOwner(local, gameObject);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!_started)
            {
                return;
            }

            if (_IsLocalOwner())
            {
                if (!isOwnerMode)
                {
                    _BecomeOwner();
                }
            }
            else if (isOwnerMode)
            {
                _BecomeRemote();
            }

            if (Utilities.IsValid(seat))
            {
                seat._OnOwnerChanged();
            }
        }

        /// <summary>
        /// 所有権を受け取ったら、受けた状態から続ける。装填中なら最初から数え直し、装填済みならそのまま（残り時間は同期しない）。
        /// 発射の番号は、撃つときに受けた番号と自分の最後の番号の大きい方から続ける。
        /// </summary>
        private void _BecomeOwner()
        {
            isOwnerMode = true;
            reloadTimer = 0f;
            _pendingSend = true;
        }

        /// <summary>所有権を失ったら、飛んでいる自分の砲弾は命中を数えずに消す。角度は今の見た目から寄せ始める。</summary>
        private void _BecomeRemote()
        {
            isOwnerMode = false;
            _pendingSend = false;
            _targetYaw = yaw;
            _targetPitch = pitch;
            for (int index = 0; index < shells.Length; index++)
            {
                YakimaTankShell shell = shells[index];
                if (Utilities.IsValid(shell) && shell.counting)
                {
                    shell._Stop();
                }
            }
        }

        /// <summary>座席から呼ぶ。VR は量で回し、Desktop は目標の角度へ向く。</summary>
        public void _SetAimByRate(bool value)
        {
            aimByRate = value;
        }

        /// <summary>撃てるか：車体と武器の両方の所有権を持ち、自分が座って入力が有効になっている。</summary>
        private bool _IsArmed()
        {
            return isOwnerMode
                   && Utilities.IsValid(seat) && seat.localSeated && !seat.waitingOwnership
                   && Utilities.IsValid(vehicleSync) && vehicleSync.isOwnerMode;
        }

        private void Update()
        {
            if (!_started)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            if (isOwnerMode)
            {
                _UpdateOwner(deltaTime);
            }
            else
            {
                yaw = StepYawToTarget(yaw, _targetYaw, turretTurnRate, deltaTime);
                pitch = StepPitchToTarget(pitch, _targetPitch, gunTurnRate, deltaTime, gunPitchMin, gunPitchMax);
            }

            _ApplyAngles();
        }

        private void _UpdateOwner(float deltaTime)
        {
            bool armed = _IsArmed();
            bool hasControl = Utilities.IsValid(control);
            if (armed && hasControl)
            {
                if (aimByRate)
                {
                    yaw = StepYawByRate(yaw, control.turretYawInput, turretTurnRate, deltaTime);
                    pitch = StepPitchByRate(pitch, control.gunPitchInput, gunTurnRate, deltaTime, gunPitchMin, gunPitchMax);
                }
                else
                {
                    yaw = StepYawToTarget(yaw, control.turretYawInput * YakimaTankDesktopInput.MaxViewYaw, turretTurnRate, deltaTime);
                    pitch = StepPitchToTarget(pitch, control.gunPitchInput * YakimaTankDesktopInput.MaxViewPitch, gunTurnRate, deltaTime, gunPitchMin, gunPitchMax);
                }
            }

            if (!loaded)
            {
                reloadTimer += deltaTime;
                if (IsReloaded(reloadTimer, reloadTime))
                {
                    loaded = true;
                    _pendingSend = true;
                }
            }

            if (hasControl && ShouldFire(loaded, control.fire, armed))
            {
                _Fire();
            }

            float now = Time.realtimeSinceStartup;
            float interval = Utilities.IsValid(vehicleSync) ? vehicleSync.sendInterval : 0.1f;
            bool turned = Mathf.Abs(Mathf.DeltaAngle(yaw, _sentYaw)) > SendAngle || Mathf.Abs(pitch - _sentPitch) > SendAngle;
            if (_pendingSend || (turned && now - _lastRequestTime >= interval))
            {
                _pendingSend = false;
                _lastRequestTime = now;
                _sentYaw = yaw;
                _sentPitch = pitch;
                requestedCount++;
                RequestSerialization();
            }
        }

        private void _Fire()
        {
            fireNumber = NextFireNumber(fireNumber, lastOwnFire);
            lastOwnFire = fireNumber;
            loaded = false;
            reloadTimer = 0f;
            Transform from = Utilities.IsValid(muzzle) ? muzzle : transform;
            firePosition = from.position;
            fireDirection = from.forward;
            _LaunchShell(firePosition, fireDirection, true);
            _pendingSend = true;
        }

        private void _LaunchShell(Vector3 position, Vector3 direction, bool counting)
        {
            if (shells.Length == 0)
            {
                return;
            }

            YakimaTankShell shell = shells[_nextShell];
            _nextShell = (_nextShell + 1) % shells.Length;
            if (Utilities.IsValid(shell))
            {
                shell._Launch(position, direction * shellSpeed, counting);
            }
        }

        /// <summary>撃った人のクライアントの砲弾が、ほかの車両の命中用のトリガーに当たったときに呼ぶ。0.5.0 は記録だけ（申告と HP は 0.6.0）。</summary>
        public void _OnShellHit(int vehicleIndex)
        {
            lastHitVehicle = vehicleIndex;
            hitCount++;
        }

        private void _ApplyAngles()
        {
            if (Utilities.IsValid(turret))
            {
                turret.localRotation = Quaternion.AngleAxis(yaw, turretAxis) * turretBaseRotation;
            }

            if (Utilities.IsValid(barrel))
            {
                // 右の軸まわりの正の回転は前を下げるので、上を正にするために負にする。
                barrel.localRotation = Quaternion.AngleAxis(-pitch, barrelAxis) * barrelBaseRotation;
            }
        }

        public override void OnPreSerialization()
        {
            if (!isOwnerMode)
            {
                return;
            }

            syncFormat = SyncFormat;
            syncYaw = yaw;
            syncPitch = pitch;
            syncLoaded = loaded;
            syncFireNumber = fireNumber;
            syncFirePosition = firePosition;
            syncFireDirection = fireDirection;
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            incomingSendTime = result.sendTime;
            _AcceptState();
        }

        /// <summary>
        /// 受けた状態（同期変数と incomingSendTime）を確かめて使う。オーナーでは何もしない。
        /// OnDeserialization から呼ぶ。ClientSim は別のクライアントからの受信を再現できないので、PlayMode のテストはここへ直接入れる。
        /// 送った人の保留は作らない。所有権が移る前後の表示のずれは許す（仕様書 0.5.0 の 1.2）。
        /// </summary>
        public void _AcceptState()
        {
            if (!_started || _IsLocalOwner())
            {
                return;
            }

            if (isOwnerMode)
            {
                _BecomeRemote();
            }

            if (!IsValidState(syncFormat, syncYaw, syncPitch, syncFirePosition, syncFireDirection, incomingSendTime, _lastUsedTime, gunPitchMin, gunPitchMax))
            {
                rejectedCount++;
                return;
            }

            bool first = !_hasState;
            _hasState = true;
            _lastUsedTime = incomingSendTime;
            _targetYaw = syncYaw;
            _targetPitch = syncPitch;
            loaded = syncLoaded;
            if (ShouldShowFire(first, syncFireNumber, fireNumber) && syncFireDirection.sqrMagnitude > 0.25f)
            {
                _LaunchShell(syncFirePosition, syncFireDirection.normalized, false);
                shownFires++;
            }

            fireNumber = syncFireNumber;
            if (first)
            {
                // 途中参加は、最初に受けた角度にすぐ置く。
                yaw = _targetYaw;
                pitch = Mathf.Clamp(_targetPitch, gunPitchMin, gunPitchMax);
            }
        }

        // ---- 計算（EditMode で確かめる） ----

        /// <summary>左右の角度を -180〜180 にする。</summary>
        public static float WrapAngle(float angle)
        {
            angle = Mathf.Repeat(angle + 180f, 360f) - 180f;
            return angle <= -180f ? angle + 360f : angle;
        }

        /// <summary>Desktop・オーナー以外：目標の左右の角度へ、近い向きに回る速さで向く（全周）。</summary>
        public static float StepYawToTarget(float yaw, float targetYaw, float turnRate, float deltaTime)
        {
            return WrapAngle(Mathf.MoveTowardsAngle(yaw, targetYaw, turnRate * deltaTime));
        }

        /// <summary>Desktop・オーナー以外：目標の上下の角度へ回る速さで向き、動ける範囲で止まる。</summary>
        public static float StepPitchToTarget(float pitch, float targetPitch, float turnRate, float deltaTime, float pitchMin, float pitchMax)
        {
            return Mathf.Clamp(Mathf.MoveTowards(pitch, Mathf.Clamp(targetPitch, pitchMin, pitchMax), turnRate * deltaTime), pitchMin, pitchMax);
        }

        /// <summary>VR：量（-1〜1）に回る速さを掛けて回す（全周）。</summary>
        public static float StepYawByRate(float yaw, float input, float turnRate, float deltaTime)
        {
            return WrapAngle(yaw + Mathf.Clamp(input, -1f, 1f) * turnRate * deltaTime);
        }

        /// <summary>VR：量（-1〜1）に回る速さを掛けて回し、動ける範囲で止まる。</summary>
        public static float StepPitchByRate(float pitch, float input, float turnRate, float deltaTime, float pitchMin, float pitchMax)
        {
            return Mathf.Clamp(pitch + Mathf.Clamp(input, -1f, 1f) * turnRate * deltaTime, pitchMin, pitchMax);
        }

        /// <summary>撃つか。armed は、車体と武器の両方の所有権を持ち、自分が座っていること。押し続けても、装填ごとに 1 発。</summary>
        public static bool ShouldFire(bool loaded, bool fireInput, bool armed)
        {
            return loaded && fireInput && armed;
        }

        public static bool IsReloaded(float reloadTimer, float reloadTime)
        {
            return reloadTimer >= reloadTime;
        }

        /// <summary>
        /// 次の発射の番号。受けた番号と、自分がこの武器で前に撃った最後の番号の大きい方から続ける（仕様書 0.5.0 の 1.2）。
        /// 同じ人の番号は必ず増えるので、「撃った人のプレイヤー ID と番号」の組は重ならない。古い番号から引き継いでも、前のオーナーとはプレイヤー ID が違う。
        /// </summary>
        public static int NextFireNumber(int received, int ownLast)
        {
            return Mathf.Max(received, ownLast) + 1;
        }

        /// <summary>
        /// 受けた発射を表示するか。最初に受けた状態（途中参加）では、番号を覚えるだけ。
        /// 「大きいとき」ではなく「違うとき」にする。所有権が移ると、新しいオーナーの番号が覚えている番号より小さいことがあるため（時刻の新しさは受けるときに確かめる）。
        /// </summary>
        public static bool ShouldShowFire(bool first, int received, int shown)
        {
            return !first && received != shown;
        }

        /// <summary>受けた状態を使うか（オーナー以外から届いた値は確かめてから使う）。比べ方を「範囲の内なら通す」にして、NaN と無限大を同じ所で弾く。</summary>
        public static bool IsValidState(byte format, float yaw, float pitch, Vector3 firePosition, Vector3 fireDirection, float sendTime, float lastUsedTime,
            float pitchMin, float pitchMax)
        {
            return format == SyncFormat
                   && Mathf.Abs(yaw) <= 180f
                   && pitch >= pitchMin - AngleTolerance && pitch <= pitchMax + AngleTolerance
                   && Mathf.Abs(firePosition.x) <= YakimaTankVehicleSync.MaxPosition
                   && Mathf.Abs(firePosition.y) <= YakimaTankVehicleSync.MaxPosition
                   && Mathf.Abs(firePosition.z) <= YakimaTankVehicleSync.MaxPosition
                   && fireDirection.magnitude <= 1f + YakimaTankVehicleSync.RotationTolerance
                   && sendTime > lastUsedTime;
        }

        public static Vector3 ShellPosition(Vector3 position, Vector3 velocity, float gravity, float deltaTime)
        {
            return position + velocity * deltaTime + Vector3.down * (0.5f * gravity * deltaTime * deltaTime);
        }

        public static Vector3 ShellVelocity(Vector3 velocity, float gravity, float deltaTime)
        {
            return velocity + Vector3.down * (gravity * deltaTime);
        }

        /// <summary>
        /// 光線が拾った物から、止まる物を選ぶ。solids はワールドの物（トリガーでないコライダー）、targets はほかの車両の命中用のトリガー。
        /// どちらでもない物（自分の車両の中・乗車の判定・ほかのトリガー）は通り抜ける。近い方を返し、なければ -1。
        /// </summary>
        public static int ChooseHit(float[] distances, bool[] solids, bool[] targets, int count)
        {
            int best = -1;
            for (int index = 0; index < count; index++)
            {
                if ((solids[index] || targets[index]) && (best < 0 || distances[index] < distances[best]))
                {
                    best = index;
                }
            }

            return best;
        }
    }
}
