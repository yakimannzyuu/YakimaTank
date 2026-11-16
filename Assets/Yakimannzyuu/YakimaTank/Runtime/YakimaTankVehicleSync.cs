using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 車体の同期（ADR 0009）。オーナーだけが物理を計算し、送る直前に Rigidbody の状態を同期変数に入れる。
    /// オーナー以外は、受けた状態を送った時刻つきの履歴にため、表示する時刻の前後から補間して車体を置く（物理の形は無効）。
    /// 計算は静的な関数に分けて EditMode で確かめる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class YakimaTankVehicleSync : UdonSharpBehaviour
    {
        public const byte SyncFormat = 1;
        public const int NoPlayer = -1;
        public const int HistorySize = 8;
        public const int HoldSize = 4;

        /// <summary>動いているとみなす速さ（m/s）と旋回の速さ（度/秒）。見た目でほとんど分からない動きを「止まっている」に入れる。</summary>
        public const float MovingSpeed = 0.05f;
        public const float MovingTurnRate = 2f;

        /// <summary>動いていない状態がこれだけ続いたら止まったとみなす（秒）。止まりかけの揺れで送り始めと停止を繰り返さない。</summary>
        public const float StopDelay = 0.5f;

        /// <summary>止まった後に、最後に送れた位置・向きからこれだけずれたら送り直す（m・度）。</summary>
        public const float DriftDistance = 0.05f;
        public const float DriftAngle = 1f;

        /// <summary>受けてからこれを過ぎた保留は使わない（秒）。</summary>
        public const float HoldLifetime = 5f;

        /// <summary>壊れた値だけを弾く上限。ワールドの大きさと戦車の動きより十分に大きい。</summary>
        public const float MaxPosition = 100000f;
        public const float MaxSpeed = 100f;
        public const float MaxAngularSpeed = 20f;
        public const float RotationTolerance = 0.01f;

        /// <summary>SimulationTime から求めた遅れがこの範囲の外なら、時間軸が違うとみなす（秒）。</summary>
        public const float MaxSimulationDelay = 2f;

        public const int StateUse = 0;
        public const int StateHold = 1;
        public const int StateReject = 2;

        public const int SendNone = 0;
        public const int SendMoving = 1;
        public const int SendStop = 2;

        private const float InputDeadZone = 0.01f;

        [Header("同期（Play 中に再適用できる）")]
        [Tooltip("動いている間に送る間隔（秒）")]
        public float sendInterval = 0.1f;

        [Tooltip("新しい状態が届かないときに、速度で先へ進めて見せる上限（秒）")]
        public float maxExtrapolation = 0.25f;

        [Header("生成で決まる値")]
        public YakimaTankVehicle vehicle;
        public YakimaTankSeat seat;
        public Rigidbody body;

        /// <summary>オーナー以外の画面で無効にする物理の形（車体のコライダーと WheelCollider）。乗車の判定は含めない。</summary>
        public Collider[] physicsColliders = new Collider[0];

        [Header("同期変数")]
        [UdonSynced] public byte syncFormat = SyncFormat;
        [UdonSynced] public int syncEpoch;
        [UdonSynced] public Vector3 syncPosition;
        [UdonSynced] public Quaternion syncRotation = Quaternion.identity;
        [UdonSynced] public Vector3 syncVelocity;
        [UdonSynced] public Vector3 syncAngularVelocity;
        [UdonSynced] public bool syncStopped;
        [UdonSynced] public int syncDriver = NoPlayer;
        [UdonSynced] public int syncSender = NoPlayer;

        /// <summary>受けた状態の送った時刻（受ける側の Time.realtimeSinceStartup の軸）。OnDeserialization が入れる。</summary>
        public float incomingSendTime;

        [Header("状態（実行中に変わる）")]
        public bool isOwnerMode;
        public int ownerId = NoPlayer;
        public int driverId = NoPlayer;
        public int epoch;
        public int requestedCount;
        public int sentCount;
        public int rejectedCount;
        public YakimaTankVehicleRegistry registry;

        private bool _started;
        private RigidbodyInterpolation _ownerInterpolation = RigidbodyInterpolation.None;

        // 送る側
        private float _stillTime;
        private bool _stopSent;
        private bool _sendingStop;
        private bool _pendingSend;
        private bool _awaitingSerialization;
        private float _lastRequestTime = float.NegativeInfinity;
        private bool _hasSent;
        private Vector3 _sentPosition;
        private Quaternion _sentRotation = Quaternion.identity;

        // 受ける側
        private float _lastUsedTime = float.NegativeInfinity;
        private int _historyEpoch;
        private bool _snapNext = true;
        private int _historyCount;
        private float[] _historyTimes = new float[HistorySize];
        private Vector3[] _historyPositions = new Vector3[HistorySize];
        private Quaternion[] _historyRotations = new Quaternion[HistorySize];
        private Vector3[] _historyVelocities = new Vector3[HistorySize];
        private Vector3[] _historyAngulars = new Vector3[HistorySize];
        private bool[] _historyStops = new bool[HistorySize];

        private int _holdCount;
        private int[] _holdSenders = new int[HoldSize];
        private float[] _holdReceived = new float[HoldSize];
        private float[] _holdTimes = new float[HoldSize];
        private int[] _holdEpochs = new int[HoldSize];
        private Vector3[] _holdPositions = new Vector3[HoldSize];
        private Quaternion[] _holdRotations = new Quaternion[HoldSize];
        private Vector3[] _holdVelocities = new Vector3[HoldSize];
        private Vector3[] _holdAngulars = new Vector3[HoldSize];
        private bool[] _holdStops = new bool[HoldSize];
        private int[] _holdDrivers = new int[HoldSize];

        private void Start()
        {
            if (Utilities.IsValid(body))
            {
                _ownerInterpolation = body.interpolation;
            }

            _started = true;
            _RefreshOwner();
            if (_IsLocalOwner())
            {
                _BecomeOwner(false);
            }
            else
            {
                _BecomeRemote();
            }

            SendCustomEventDelayedSeconds(nameof(_WarnIfNoRegistry), 2f);
        }

        public void _WarnIfNoRegistry()
        {
            if (!Utilities.IsValid(registry))
            {
                Debug.LogWarning("[YakimaTank] シーンの車両の一覧がありません。同じ人が持つ車両同士が当たります。セットアップウィンドウの「シーンの車両一覧を更新」で作ってください。", this);
            }
        }

        /// <summary>シーンの車両の一覧が、始まるときに自分を知らせる。</summary>
        public void _SetRegistry(YakimaTankVehicleRegistry value)
        {
            registry = value;
            if (isOwnerMode)
            {
                _ApplyIgnores();
            }
        }

        /// <summary>
        /// 物理の形が有効なほかの車両と、全部のコライダーの当たりを外す。持ち主が違う車両は相手の物理の形が無効なので外さなくても当たらないが、
        /// 持ち主ごとに組み合わせを選び直すと、所有権が移るたびに外し直しが要る。無効なコライダーとは外せない（Unity がエラーにする）ので飛ばし、
        /// 相手が有効にしたときに相手の側で外す。
        /// </summary>
        private void _ApplyIgnores()
        {
            if (!Utilities.IsValid(registry) || registry.vehicles == null)
            {
                return;
            }

            for (int index = 0; index < registry.vehicles.Length; index++)
            {
                YakimaTankVehicleSync other = registry.vehicles[index];
                if (!Utilities.IsValid(other) || other == this || !other.isOwnerMode || other.physicsColliders == null)
                {
                    continue;
                }

                for (int mine = 0; mine < physicsColliders.Length; mine++)
                {
                    Collider own = physicsColliders[mine];
                    if (!Utilities.IsValid(own))
                    {
                        continue;
                    }

                    for (int theirs = 0; theirs < other.physicsColliders.Length; theirs++)
                    {
                        Collider their = other.physicsColliders[theirs];
                        if (Utilities.IsValid(their))
                        {
                            Physics.IgnoreCollision(own, their, true);
                        }
                    }
                }
            }
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

        private void _RefreshOwner()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer))
            {
                ownerId = NoPlayer;
                return;
            }

            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            ownerId = Utilities.IsValid(owner) ? owner.playerId : NoPlayer;
        }

        private int _LocalPlayerId()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            return Utilities.IsValid(local) ? local.playerId : NoPlayer;
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!_started)
            {
                return;
            }

            ownerId = Utilities.IsValid(player) ? player.playerId : NoPlayer;
            if (_IsLocalOwner())
            {
                if (!isOwnerMode)
                {
                    _BecomeOwner(true);
                }
            }
            else
            {
                if (isOwnerMode)
                {
                    _BecomeRemote();
                }
                else
                {
                    _ResetHistory();
                }

                _UseHoldFor(ownerId);
                _DropExpiredHolds();
            }

            if (Utilities.IsValid(seat))
            {
                seat._OnOwnerChanged();
            }
        }

        /// <summary>自分の画面で物理を計算する。所有権を受け取ったときは、最新の有効な状態から物理を始め、世代を進めてすぐに送る。</summary>
        private void _BecomeOwner(bool takeOver)
        {
            isOwnerMode = true;
            rejectedCount += _holdCount;
            _holdCount = 0;
            _SetPhysicsShape(true);
            if (Utilities.IsValid(body))
            {
                body.isKinematic = false;
                body.interpolation = _ownerInterpolation;
                if (takeOver && _historyCount > 0)
                {
                    int latest = _historyCount - 1;
                    body.transform.SetPositionAndRotation(_historyPositions[latest], _historyRotations[latest]);
                    body.position = _historyPositions[latest];
                    body.rotation = _historyRotations[latest];
                    body.velocity = _historyVelocities[latest];
                    body.angularVelocity = _historyAngulars[latest];
                }
            }

            if (Utilities.IsValid(vehicle))
            {
                vehicle._SetAuthority(true);
            }

            if (takeOver)
            {
                epoch++;
                if (driverId != NoPlayer && !Utilities.IsValid(VRCPlayerApi.GetPlayerById(driverId)))
                {
                    driverId = NoPlayer;
                }
            }

            _stillTime = 0f;
            _stopSent = false;
            _pendingSend = true;
            _ApplyIgnores();
        }

        private void _BecomeRemote()
        {
            isOwnerMode = false;
            _pendingSend = false;
            _awaitingSerialization = false;
            if (Utilities.IsValid(vehicle))
            {
                vehicle._SetAuthority(false);
            }

            if (Utilities.IsValid(body))
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }

            _SetPhysicsShape(false);
            _ResetHistory();
        }

        private void _SetPhysicsShape(bool enabled)
        {
            for (int index = 0; index < physicsColliders.Length; index++)
            {
                Collider shape = physicsColliders[index];
                if (Utilities.IsValid(shape))
                {
                    shape.enabled = enabled;
                }
            }
        }

        private void _ResetHistory()
        {
            _historyCount = 0;
            _snapNext = true;
        }

        /// <summary>座席から呼ぶ。運転手を変えたら、すぐに送る。</summary>
        public void _SetDriver(int playerId)
        {
            driverId = playerId;
            if (isOwnerMode)
            {
                _pendingSend = true;
            }

            _NotifyBoarding();
        }

        /// <summary>同期した運転手がいるか。インスタンスにいない人を指していれば、いないとみなす。</summary>
        public bool _HasDriver()
        {
            return driverId != NoPlayer && Utilities.IsValid(VRCPlayerApi.GetPlayerById(driverId));
        }

        /// <summary>車両が最初の位置に戻したときに呼ぶ。オーナーなら世代を進めてすぐに送る。</summary>
        public void _OnVehicleReset()
        {
            if (!isOwnerMode)
            {
                return;
            }

            epoch++;
            _stillTime = 0f;
            _stopSent = false;
            _pendingSend = true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.playerId == driverId)
            {
                _SetDriver(NoPlayer);
            }
        }

        private void _NotifyBoarding()
        {
            if (Utilities.IsValid(seat))
            {
                seat._UpdateBoarding();
            }
        }

        private void Update()
        {
            if (!_started)
            {
                return;
            }

            if (isOwnerMode)
            {
                _UpdateOwner();
            }
            else
            {
                _UpdateRemote();
            }
        }

        private void _UpdateOwner()
        {
            if (!Utilities.IsValid(body) || !Utilities.IsValid(vehicle))
            {
                return;
            }

            bool hasInput = Mathf.Abs(vehicle.throttleInput) > InputDeadZone || Mathf.Abs(vehicle.steerInput) > InputDeadZone;
            bool moving = IsMoving(body.velocity.magnitude, body.angularVelocity.magnitude * Mathf.Rad2Deg, hasInput);
            _stillTime = moving ? 0f : _stillTime + Time.deltaTime;
            bool drifted = _stopSent && _hasSent && !_awaitingSerialization && HasDrifted(_sentPosition, _sentRotation, body.position, body.rotation);
            float now = Time.realtimeSinceStartup;
            float sinceRequest = now - _lastRequestTime;
            bool clogged = Networking.IsClogged;
            if (clogged && DecideSend(moving, _stillTime, _stopSent, sinceRequest, sendInterval, drifted, false, _pendingSend) != SendNone)
            {
                _pendingSend = true;
            }

            int action = DecideSend(moving, _stillTime, _stopSent, sinceRequest, sendInterval, drifted, clogged, _pendingSend);
            if (action == SendNone)
            {
                return;
            }

            _sendingStop = action == SendStop;
            _stopSent = _sendingStop;
            _pendingSend = false;
            _awaitingSerialization = true;
            _lastRequestTime = now;
            requestedCount++;
            RequestSerialization();
        }

        public override void OnPreSerialization()
        {
            if (!isOwnerMode || !Utilities.IsValid(body))
            {
                return;
            }

            syncFormat = SyncFormat;
            syncEpoch = epoch;
            syncPosition = body.position;
            syncRotation = body.rotation;
            syncVelocity = body.velocity;
            syncAngularVelocity = body.angularVelocity;
            syncStopped = _sendingStop;
            syncDriver = driverId;
            syncSender = _LocalPlayerId();
        }

        /// <summary>送れなかった状態は、次に送り直す（停止の印の状態を落とさないため）。</summary>
        public override void OnPostSerialization(SerializationResult result)
        {
            _awaitingSerialization = false;
            if (!result.success)
            {
                _pendingSend = true;
                return;
            }

            sentCount++;
            _hasSent = true;
            _sentPosition = syncPosition;
            _sentRotation = syncRotation;
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            incomingSendTime = result.sendTime;
            _AcceptState();
        }

        /// <summary>
        /// 受けた状態（同期変数と incomingSendTime）を確かめて履歴に入れる。オーナーでは何もしない。
        /// OnDeserialization から呼ぶ。ClientSim は別のクライアントからの受信を再現できないので、PlayMode のテストはここへ直接入れる。
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

            _RefreshOwner();
            _DropExpiredHolds();
            int kind = ClassifyState(syncFormat, syncPosition, syncRotation, syncVelocity, syncAngularVelocity,
                incomingSendTime, _lastUsedTime, syncSender, ownerId);
            if (kind == StateReject)
            {
                rejectedCount++;
                return;
            }

            Quaternion rotation = syncRotation.normalized;
            if (kind == StateHold)
            {
                _Hold(syncSender, incomingSendTime, syncEpoch, syncPosition, rotation, syncVelocity, syncAngularVelocity, syncStopped, syncDriver);
                return;
            }

            _UseState(syncEpoch, incomingSendTime, syncPosition, rotation, syncVelocity, syncAngularVelocity, syncStopped, syncDriver);
        }

        private void _UseState(int stateEpoch, float sendTime, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity, bool stopped, int driver)
        {
            int kept = KeepHistory(stateEpoch, _historyEpoch, _historyCount);
            if (kept != _historyCount)
            {
                _snapNext = true;
            }

            _historyEpoch = stateEpoch;
            epoch = stateEpoch;
            _historyCount = PushHistory(_historyTimes, _historyPositions, _historyRotations, _historyVelocities, _historyAngulars, _historyStops,
                kept, sendTime, position, rotation, velocity, angularVelocity, stopped);
            _lastUsedTime = sendTime;
            driverId = Utilities.IsValid(VRCPlayerApi.GetPlayerById(driver)) ? driver : NoPlayer;
            _NotifyBoarding();
        }

        private void _Hold(int sender, float sendTime, int stateEpoch, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity, bool stopped, int driver)
        {
            float now = Time.realtimeSinceStartup;
            int index = StoreHold(_holdSenders, _holdReceived, _holdTimes, _holdCount, sender, sendTime, now);
            if (index < 0)
            {
                rejectedCount++;
                return;
            }

            if (index < _holdCount && _holdSenders[index] != sender)
            {
                // あふれて、別の人の保留を捨てた。
                rejectedCount++;
            }

            _holdSenders[index] = sender;
            _holdReceived[index] = now;
            _holdTimes[index] = sendTime;
            _holdEpochs[index] = stateEpoch;
            _holdPositions[index] = position;
            _holdRotations[index] = rotation;
            _holdVelocities[index] = velocity;
            _holdAngulars[index] = angularVelocity;
            _holdStops[index] = stopped;
            _holdDrivers[index] = driver;
            _holdCount = Mathf.Max(_holdCount, index + 1);
        }

        /// <summary>所有権が移った相手の保留があれば、使えるときは履歴を捨ててすぐに置き、使っても使わなくても消す。ほかの人の保留は残す。</summary>
        private void _UseHoldFor(int owner)
        {
            int index = FindHold(_holdSenders, _holdCount, owner);
            if (index < 0)
            {
                return;
            }

            if (HoldUsable(_holdTimes[index], _holdReceived[index], Time.realtimeSinceStartup, _lastUsedTime))
            {
                _ResetHistory();
                _UseState(_holdEpochs[index], _holdTimes[index], _holdPositions[index], _holdRotations[index],
                    _holdVelocities[index], _holdAngulars[index], _holdStops[index], _holdDrivers[index]);
            }
            else
            {
                rejectedCount++;
            }

            _RemoveHold(index);
        }

        private void _DropExpiredHolds()
        {
            float now = Time.realtimeSinceStartup;
            for (int index = _holdCount - 1; index >= 0; index--)
            {
                if (now - _holdReceived[index] > HoldLifetime)
                {
                    rejectedCount++;
                    _RemoveHold(index);
                }
            }
        }

        /// <summary>最後の保留を空いた所へ移して詰める（保留の順番は意味を持たない。古さは受けた時刻で見る）。</summary>
        private void _RemoveHold(int index)
        {
            int last = _holdCount - 1;
            if (index != last)
            {
                _holdSenders[index] = _holdSenders[last];
                _holdReceived[index] = _holdReceived[last];
                _holdTimes[index] = _holdTimes[last];
                _holdEpochs[index] = _holdEpochs[last];
                _holdPositions[index] = _holdPositions[last];
                _holdRotations[index] = _holdRotations[last];
                _holdVelocities[index] = _holdVelocities[last];
                _holdAngulars[index] = _holdAngulars[last];
                _holdStops[index] = _holdStops[last];
                _holdDrivers[index] = _holdDrivers[last];
            }

            _holdCount = last;
        }

        private void _UpdateRemote()
        {
            if (_historyCount == 0 || !Utilities.IsValid(body))
            {
                return;
            }

            int latest = _historyCount - 1;
            Vector3 position;
            Quaternion rotation;
            if (_snapNext)
            {
                position = _historyPositions[latest];
                rotation = _historyRotations[latest];
                _snapNext = false;
            }
            else
            {
                float display = DisplayTime(Networking.SimulationTime(gameObject), Time.realtimeSinceStartup, sendInterval);
                position = SamplePosition(_historyTimes, _historyPositions, _historyVelocities, _historyStops, _historyCount, display, maxExtrapolation);
                rotation = SampleRotation(_historyTimes, _historyRotations, _historyAngulars, _historyStops, _historyCount, display, maxExtrapolation);
            }

            body.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>デバッグ UI：オーナー以外で、最新の状態の古さ（秒）。状態がなければ負。</summary>
        public float _GetAge()
        {
            return _historyCount == 0 ? -1f : Time.realtimeSinceStartup - _historyTimes[_historyCount - 1];
        }

        // ---- 計算（EditMode で確かめる） ----

        public static int ClassifyState(byte format, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity,
            float sendTime, float lastUsedTime, int sender, int owner)
        {
            // 比べ方を「範囲の内なら通す」にして、NaN（どの比べ方も false）と無限大を同じ所で弾く。
            bool valid = format == SyncFormat
                         && Mathf.Abs(position.x) <= MaxPosition && Mathf.Abs(position.y) <= MaxPosition && Mathf.Abs(position.z) <= MaxPosition
                         && Mathf.Abs(Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w) - 1f) <= RotationTolerance
                         && velocity.magnitude <= MaxSpeed
                         && angularVelocity.magnitude <= MaxAngularSpeed
                         && sendTime > lastUsedTime;
            if (!valid)
            {
                return StateReject;
            }

            return sender == owner ? StateUse : StateHold;
        }

        /// <summary>times[i] ≦ time ＜ times[i + 1] の i。最初より前は -1、最新より後は最後の番号。</summary>
        public static int FindSegment(float[] times, int count, float time)
        {
            if (count == 0 || time < times[0])
            {
                return -1;
            }

            for (int index = 0; index < count - 1; index++)
            {
                if (time < times[index + 1])
                {
                    return index;
                }
            }

            return count - 1;
        }

        public static Vector3 Hermite(Vector3 p0, Vector3 v0, float t0, Vector3 p1, Vector3 v1, float t1, float time)
        {
            float span = t1 - t0;
            if (span <= 0f)
            {
                return p1;
            }

            float s = Mathf.Clamp01((time - t0) / span);
            float s2 = s * s;
            float s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * p0
                   + (s3 - 2f * s2 + s) * span * v0
                   + (-2f * s3 + 3f * s2) * p1
                   + (s3 - s2) * span * v1;
        }

        public static Quaternion SlerpBetween(Quaternion r0, float t0, Quaternion r1, float t1, float time)
        {
            float span = t1 - t0;
            return span <= 0f ? r1 : Quaternion.Slerp(r0, r1, Mathf.Clamp01((time - t0) / span));
        }

        public static Vector3 ExtrapolatePosition(Vector3 position, Vector3 velocity, float elapsed, float maxExtrapolation)
        {
            return position + velocity * Mathf.Clamp(elapsed, 0f, maxExtrapolation);
        }

        public static Quaternion ExtrapolateRotation(Quaternion rotation, Vector3 angularVelocity, float elapsed, float maxExtrapolation)
        {
            float speed = angularVelocity.magnitude;
            if (speed <= 0f)
            {
                return rotation;
            }

            float angle = speed * Mathf.Clamp(elapsed, 0f, maxExtrapolation) * Mathf.Rad2Deg;
            return Quaternion.AngleAxis(angle, angularVelocity / speed) * rotation;
        }

        /// <summary>履歴の最後に足し、新しい数を返す。いっぱいなら一番古いものを捨てて詰める。</summary>
        public static int PushHistory(float[] times, Vector3[] positions, Quaternion[] rotations, Vector3[] velocities, Vector3[] angularVelocities,
            bool[] stops, int count, float time, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity, bool stopped)
        {
            int capacity = times.Length;
            if (count >= capacity)
            {
                for (int index = 1; index < capacity; index++)
                {
                    times[index - 1] = times[index];
                    positions[index - 1] = positions[index];
                    rotations[index - 1] = rotations[index];
                    velocities[index - 1] = velocities[index];
                    angularVelocities[index - 1] = angularVelocities[index];
                    stops[index - 1] = stops[index];
                }

                count = capacity - 1;
            }

            times[count] = time;
            positions[count] = position;
            rotations[count] = rotation;
            velocities[count] = velocity;
            angularVelocities[count] = angularVelocity;
            stops[count] = stopped;
            return count + 1;
        }

        /// <summary>世代が変わったら履歴を捨てる（前のオーナー・前の位置の状態と混ぜない）。</summary>
        public static int KeepHistory(int epoch, int historyEpoch, int count)
        {
            return epoch == historyEpoch ? count : 0;
        }

        public static Vector3 SamplePosition(float[] times, Vector3[] positions, Vector3[] velocities, bool[] stops, int count, float time, float maxExtrapolation)
        {
            int segment = FindSegment(times, count, time);
            if (segment < 0)
            {
                return positions[0];
            }

            int latest = count - 1;
            if (segment == latest)
            {
                return stops[latest]
                    ? positions[latest]
                    : ExtrapolatePosition(positions[latest], velocities[latest], time - times[latest], maxExtrapolation);
            }

            return Hermite(positions[segment], velocities[segment], times[segment],
                positions[segment + 1], velocities[segment + 1], times[segment + 1], time);
        }

        public static Quaternion SampleRotation(float[] times, Quaternion[] rotations, Vector3[] angularVelocities, bool[] stops, int count, float time, float maxExtrapolation)
        {
            int segment = FindSegment(times, count, time);
            if (segment < 0)
            {
                return rotations[0];
            }

            int latest = count - 1;
            if (segment == latest)
            {
                return stops[latest]
                    ? rotations[latest]
                    : ExtrapolateRotation(rotations[latest], angularVelocities[latest], time - times[latest], maxExtrapolation);
            }

            return SlerpBetween(rotations[segment], times[segment], rotations[segment + 1], times[segment + 1], time);
        }

        /// <summary>
        /// 表示する時刻。SimulationTime から求めた遅れが範囲の内ならそれを使い、外なら時間軸が違うとみなして、
        /// 今の時刻から送る間隔の 2 倍を引く。最新の送った時刻を基準にする案は採らない。届かない間に時刻が進まず、最後の状態まで再生されないため。
        /// </summary>
        public static float DisplayTime(float simulationTime, float now, float sendInterval)
        {
            float delay = now - simulationTime;
            return delay >= 0f && delay <= MaxSimulationDelay ? simulationTime : now - 2f * sendInterval;
        }

        /// <summary>保留を書く番号。同じ人の保留は新しい時刻のときだけ置き換え（古ければ -1）、なければ空きか、一番前に受けたもの。</summary>
        public static int StoreHold(int[] senders, float[] received, float[] times, int count, int sender, float sendTime, float now)
        {
            int existing = FindHold(senders, count, sender);
            if (existing >= 0)
            {
                return sendTime > times[existing] ? existing : -1;
            }

            if (count < senders.Length)
            {
                return count;
            }

            int oldest = 0;
            for (int index = 1; index < count; index++)
            {
                if (received[index] < received[oldest])
                {
                    oldest = index;
                }
            }

            return oldest;
        }

        public static int FindHold(int[] senders, int count, int sender)
        {
            for (int index = 0; index < count; index++)
            {
                if (senders[index] == sender)
                {
                    return index;
                }
            }

            return -1;
        }

        public static bool HoldUsable(float holdSendTime, float holdReceived, float now, float lastUsedTime)
        {
            return now - holdReceived <= HoldLifetime && holdSendTime > lastUsedTime;
        }

        public static bool IsMoving(float speed, float turnRate, bool hasInput)
        {
            return hasInput || speed >= MovingSpeed || turnRate >= MovingTurnRate;
        }

        public static bool HasDrifted(Vector3 sentPosition, Quaternion sentRotation, Vector3 position, Quaternion rotation)
        {
            return Vector3.Distance(sentPosition, position) >= DriftDistance || Quaternion.Angle(sentRotation, rotation) >= DriftAngle;
        }

        /// <summary>
        /// 送るか。pending は「すぐに送る」（乗った・降りた・所有権・最初の位置）と「詰まりが解けたら最新を 1 回」の印。
        /// 止まっている間は停止の印で送り、印を落とさない。
        /// </summary>
        public static int DecideSend(bool moving, float stillTime, bool stopSent, float sinceRequest, float interval, bool drifted, bool clogged, bool pending)
        {
            if (clogged)
            {
                return SendNone;
            }

            bool stopped = !moving && stillTime >= StopDelay;
            if (pending)
            {
                return stopped ? SendStop : SendMoving;
            }

            if (moving)
            {
                return stopSent || sinceRequest >= interval ? SendMoving : SendNone;
            }

            if (!stopped)
            {
                return sinceRequest >= interval ? SendMoving : SendNone;
            }

            return !stopSent || drifted ? SendStop : SendNone;
        }
    }
}
