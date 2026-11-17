using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 生存の状態（仕様書 0.6.0）。ルートに置き、車体のオーナーが書く（所有権の移動が車体と同時に済む）。
    /// HP・復活の世代・撃った車両ごとに処理した命中を、変わったときだけ送る。車体の同期に混ぜないのは、送る頻度が違うため。
    /// 撃った人のクライアントが命中の申告をオーナーへ送り、オーナーが確かめて HP を引く。0 で破壊、待ち時間の後に最初の位置で復活する。
    /// HP のバーは、各クライアントが同期変数の HP から長さを決める（同期しない見た目）。計算は静的な関数に分けて EditMode で確かめる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class YakimaTankLife : UdonSharpBehaviour
    {
        public const byte SyncFormat = 1;
        public const int NoPlayer = -1;

        /// <summary>シーンの車両の一覧をまだ知らない（一覧が始まる前）。受けた配列の長さを、シーンの車両の数と比べない。</summary>
        public const int UnknownCount = -1;

        /// <summary>
        /// 命中の申告の 1 秒あたりの回数の上限（送る側ごと。超えた分は VRChat が後へ回す）。既定は 5、最大は 100（SDK 3.10.6 の UdonSharp の検証）。
        /// 装填の時間は送る間隔（既定 0.1 秒）より長いので、1 台が撃てるのは 1 秒に 10 発まで。その全部が当たっても上限に届かない値にする。
        /// </summary>
        public const int MaxHitsPerSecond = 10;

        // 申告を確かめた結果。番号は仕様書 0.6.0 の 1.2 の項目とは別（確かめる順番が違う）。
        public const int HitUse = 0;
        public const int HitNotOwner = 1;
        public const int HitWrongSender = 2;
        public const int HitBadShooter = 3;
        public const int HitOldGeneration = 4;
        public const int HitDestroyed = 5;
        public const int HitDuplicate = 6;
        public const int HitBadNumber = 7;

        [Header("HP・復活（Play 中に再適用できる）")]
        [Tooltip("最大 HP。整数に四捨五入して使う。Play 中に変えると、次の復活で最大になる")]
        public float maxHealth = 100f;

        [Tooltip("破壊から復活までの待ち時間（秒）")]
        public float respawnDelay = 5f;

        [Header("生成で決まる値")]
        public YakimaTankVehicle vehicle;
        public YakimaTankVehicleSync vehicleSync;
        public YakimaTankSeat seat;
        public YakimaTankWeaponSync weapon;

        /// <summary>HP のバー。各クライアントが自分の頭の方へ向ける。</summary>
        public Transform healthBar;

        /// <summary>HP のバーの中身の左端。左右の拡大で長さを表す。</summary>
        public Transform healthFill;

        [Header("同期変数")]
        [UdonSynced] public byte syncFormat = SyncFormat;

        /// <summary>負の値で始める。オーナーが一度も送っていない状態は、受ける側の確かめで弾かれる。</summary>
        [UdonSynced] public int syncHealth = -1;
        [UdonSynced] public int syncRespawnGeneration;
        [UdonSynced] public int[] syncHitPlayers = new int[0];
        [UdonSynced] public int[] syncHitNumbers = new int[0];

        /// <summary>受けた状態の送った時刻（受ける側の Time.realtimeSinceStartup の軸）。OnDeserialization が入れる。</summary>
        public float incomingSendTime;

        /// <summary>受けた命中の申告。ReceiveHit が入れる。PlayMode のテストは、ここへ入れて _AcceptHit を呼ぶ。</summary>
        public int incomingHitPlayer = NoPlayer;
        public int incomingHitShooter;
        public int incomingHitNumber;
        public int incomingHitGeneration;

        [Header("状態（実行中に変わる）")]
        public bool isOwnerMode;
        public int health = 1;
        public int respawnGeneration;

        /// <summary>撃った車両（シーンの車両の番号）ごとに、最後に処理した命中の撃った人のプレイヤー ID と発射の番号。</summary>
        public int[] hitPlayers = new int[0];
        public int[] hitNumbers = new int[0];

        public float respawnTimer;
        public int acceptedHits;
        public int rejectedHits;
        public int lastHitResult = HitUse;
        public int requestedCount;
        public int rejectedCount;

        private bool _started;
        private bool _hasState;
        private float _lastUsedTime = float.NegativeInfinity;

        private void Start()
        {
            _started = true;
            if (!_hasState)
            {
                health = MaxHealthPoints(maxHealth);
            }

            if (_IsLocalOwner())
            {
                isOwnerMode = true;
                _RequestSend();
            }

            _UpdateBar();
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
                    // 同期変数の HP と処理した命中を引き継ぐ。復活の待ち時間は最初から数え直す。
                    isOwnerMode = true;
                    respawnTimer = 0f;
                }
            }
            else
            {
                isOwnerMode = false;
            }
        }

        /// <summary>破壊されている（HP が 0 以下）。</summary>
        public bool _IsDestroyed()
        {
            return health <= 0;
        }

        private void Update()
        {
            if (!_started || !isOwnerMode || !_IsDestroyed())
            {
                return;
            }

            respawnTimer += Time.deltaTime;
            if (IsRespawnDue(respawnTimer, respawnDelay))
            {
                _Respawn();
            }
        }

        private void LateUpdate()
        {
            _UpdateBar();
        }

        /// <summary>HP のバーの長さと向き。車両が動いた後（LateUpdate）に、自分の頭の方へ、傾けずに向ける。</summary>
        private void _UpdateBar()
        {
            if (Utilities.IsValid(healthFill))
            {
                healthFill.localScale = new Vector3(FillRatio(health, MaxHealthPoints(maxHealth)), 1f, 1f);
            }

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(healthBar) || !Utilities.IsValid(local))
            {
                return;
            }

            Vector3 away = healthBar.position - local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.0001f)
            {
                healthBar.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }

        /// <summary>
        /// 命中の申告の入口（オーナー宛ての引数つきのネットワークのイベント）。撃った車両の番号・発射の番号・撃った側が見ていた相手の復活の世代。
        /// 送った人は NetworkCalling.CallingPlayer で知る（申告に入れない）。確かめは _AcceptHit。
        /// </summary>
        [NetworkCallable(MaxHitsPerSecond)]
        public void ReceiveHit(int shooterIndex, int fireNumber, int targetGeneration)
        {
            VRCPlayerApi caller = NetworkCalling.CallingPlayer;
            incomingHitPlayer = Utilities.IsValid(caller) ? caller.playerId : NoPlayer;
            incomingHitShooter = shooterIndex;
            incomingHitNumber = fireNumber;
            incomingHitGeneration = targetGeneration;
            _AcceptHit();
        }

        /// <summary>
        /// 受けた申告（incomingHit…）を確かめて、通れば HP を引き、処理した命中を覚えて送る。外れたら使わずに数える。
        /// ClientSim はオーナー宛てのイベントをいつも自分で実行し、送った人も自分になるので、PlayMode のテストはここへ直接入れる。
        /// </summary>
        public void _AcceptHit()
        {
            _EnsureHitArrays();
            int shooter = incomingHitShooter;
            YakimaTankWeaponSync shooterWeapon = _ShooterWeapon(shooter);
            bool sender = Utilities.IsValid(shooterWeapon) && incomingHitPlayer != NoPlayer && _OwnerId(shooterWeapon.gameObject) == incomingHitPlayer;
            bool known = shooter >= 0 && shooter < hitPlayers.Length;
            int lastPlayer = known ? hitPlayers[shooter] : NoPlayer;
            int lastNumber = known ? hitNumbers[shooter] : -1;
            int ownIndex = Utilities.IsValid(vehicleSync) ? vehicleSync.vehicleIndex : -1;
            int result = ClassifyHit(_IsLocalOwner(), sender, shooter, ownIndex, _VehicleCount(), incomingHitGeneration, respawnGeneration, health,
                incomingHitPlayer, incomingHitNumber, lastPlayer, lastNumber);
            lastHitResult = result;
            if (result != HitUse || !known)
            {
                rejectedHits++;
                return;
            }

            // ダメージは、届いた数値でなく、撃った車両の武器の設定から求める。
            hitPlayers[shooter] = incomingHitPlayer;
            hitNumbers[shooter] = incomingHitNumber;
            health = ApplyDamage(health, shooterWeapon.damage);
            acceptedHits++;
            _RequestSend();
            _NotifyChanged(false);
        }

        /// <summary>復活。車体のオーナーが、破壊から待ち時間の後に行う。処理した命中はそのまま残す。</summary>
        private void _Respawn()
        {
            respawnGeneration++;
            health = MaxHealthPoints(maxHealth);
            respawnTimer = 0f;
            if (Utilities.IsValid(vehicle))
            {
                vehicle._Respawn();
            }

            _RequestSend();
            _NotifyChanged(true);
        }

        /// <summary>状態が変わったことを、座席（降ろす・乗車の判定）と武器（復活で自分の領域を戻す）へ知らせる。</summary>
        private void _NotifyChanged(bool respawned)
        {
            if (respawned && Utilities.IsValid(weapon))
            {
                weapon._OnRespawned();
            }

            if (Utilities.IsValid(seat))
            {
                seat._OnLifeChanged();
            }

            _UpdateBar();
        }

        private void _RequestSend()
        {
            if (!isOwnerMode)
            {
                return;
            }

            requestedCount++;
            RequestSerialization();
        }

        /// <summary>シーンの車両の数。一覧をまだ知らなければ UnknownCount。</summary>
        private int _VehicleCount()
        {
            if (!Utilities.IsValid(vehicleSync) || !Utilities.IsValid(vehicleSync.registry) || vehicleSync.registry.vehicles == null)
            {
                return UnknownCount;
            }

            return vehicleSync.registry.vehicles.Length;
        }

        /// <summary>撃った車両の武器。番号が範囲の外なら null。</summary>
        private YakimaTankWeaponSync _ShooterWeapon(int index)
        {
            int count = _VehicleCount();
            if (index < 0 || index >= count)
            {
                return null;
            }

            YakimaTankVehicleSync shooter = vehicleSync.registry.vehicles[index];
            return Utilities.IsValid(shooter) ? shooter.weapon : null;
        }

        private int _OwnerId(GameObject target)
        {
            if (!Utilities.IsValid(Networking.LocalPlayer))
            {
                return NoPlayer;
            }

            VRCPlayerApi owner = Networking.GetOwner(target);
            return Utilities.IsValid(owner) ? owner.playerId : NoPlayer;
        }

        /// <summary>処理した命中の配列を、シーンの車両の数まで伸ばす（覚えた命中は残す）。一覧をまだ知らなければ何もしない。</summary>
        private void _EnsureHitArrays()
        {
            int count = _VehicleCount();
            if (count <= hitPlayers.Length)
            {
                return;
            }

            int[] players = new int[count];
            int[] numbers = new int[count];
            for (int index = 0; index < count; index++)
            {
                bool kept = index < hitPlayers.Length;
                players[index] = kept ? hitPlayers[index] : NoPlayer;
                numbers[index] = kept ? hitNumbers[index] : -1;
            }

            hitPlayers = players;
            hitNumbers = numbers;
        }

        public override void OnPreSerialization()
        {
            if (!isOwnerMode)
            {
                return;
            }

            _EnsureHitArrays();
            syncFormat = SyncFormat;
            syncHealth = health;
            syncRespawnGeneration = respawnGeneration;
            if (syncHitPlayers == null || syncHitPlayers.Length != hitPlayers.Length)
            {
                syncHitPlayers = new int[hitPlayers.Length];
                syncHitNumbers = new int[hitNumbers.Length];
            }

            for (int index = 0; index < hitPlayers.Length; index++)
            {
                syncHitPlayers[index] = hitPlayers[index];
                syncHitNumbers[index] = hitNumbers[index];
            }
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            incomingSendTime = result.sendTime;
            _AcceptState();
        }

        /// <summary>
        /// 受けた状態（同期変数と incomingSendTime）を確かめて使う。オーナーでは何もしない。
        /// Start の前に届いても使う（途中参加の最初の状態を落とさないため）。PlayMode のテストはここへ直接入れる。
        /// </summary>
        public void _AcceptState()
        {
            if (_IsLocalOwner())
            {
                return;
            }

            isOwnerMode = false;
            int players = syncHitPlayers == null ? -1 : syncHitPlayers.Length;
            int numbers = syncHitNumbers == null ? -1 : syncHitNumbers.Length;
            if (!IsValidState(syncFormat, syncHealth, MaxHealthPoints(maxHealth), syncRespawnGeneration, players, numbers, _VehicleCount(), incomingSendTime, _lastUsedTime))
            {
                rejectedCount++;
                return;
            }

            _hasState = true;
            _lastUsedTime = incomingSendTime;
            bool respawned = syncRespawnGeneration != respawnGeneration;
            health = syncHealth;
            respawnGeneration = syncRespawnGeneration;
            hitPlayers = new int[players];
            hitNumbers = new int[numbers];
            for (int index = 0; index < players; index++)
            {
                hitPlayers[index] = syncHitPlayers[index];
                hitNumbers[index] = syncHitNumbers[index];
            }

            _NotifyChanged(respawned);
        }

        // ---- 計算（EditMode で確かめる） ----

        /// <summary>
        /// 命中の申告を使うか（仕様書 0.6.0 の 1.2 の 1〜7）。1 つでも外れたら使わない。返すのは HitUse か、外れた理由。
        /// 重複は、撃った車両ごとに覚えた命中と比べ、同じ人で番号が以下なら重複。別の人（所有権が移った）は番号が小さくても重複でない。
        /// </summary>
        public static int ClassifyHit(bool owner, bool senderIsShooterOwner, int shooterIndex, int ownIndex, int vehicleCount, int hitGeneration, int generation,
            int health, int senderId, int fireNumber, int lastPlayer, int lastNumber)
        {
            if (!owner)
            {
                return HitNotOwner;
            }

            if (shooterIndex < 0 || shooterIndex >= vehicleCount || shooterIndex == ownIndex)
            {
                return HitBadShooter;
            }

            if (!senderIsShooterOwner)
            {
                return HitWrongSender;
            }

            if (hitGeneration != generation)
            {
                return HitOldGeneration;
            }

            if (health <= 0)
            {
                return HitDestroyed;
            }

            if (fireNumber < 0)
            {
                return HitBadNumber;
            }

            if (senderId == lastPlayer && fireNumber <= lastNumber)
            {
                return HitDuplicate;
            }

            return HitUse;
        }

        /// <summary>HP からダメージ（四捨五入。負は 0）を引く。0 で止まる。</summary>
        public static int ApplyDamage(int health, float damage)
        {
            return Mathf.Max(0, health - Mathf.Max(0, Mathf.RoundToInt(damage)));
        }

        /// <summary>最大 HP の設定（小数）を、送る整数にする。1 より小さければ 1。</summary>
        public static int MaxHealthPoints(float maxHealth)
        {
            return Mathf.Max(1, Mathf.RoundToInt(maxHealth));
        }

        public static bool IsRespawnDue(float timer, float delay)
        {
            return timer >= delay;
        }

        /// <summary>HP のバーの長さ（最大に対する割合、0〜1）。</summary>
        public static float FillRatio(int health, int maxPoints)
        {
            return maxPoints > 0 ? Mathf.Clamp01((float)health / maxPoints) : 0f;
        }

        /// <summary>
        /// 受けた生存の状態を使うか。形式・HP の範囲（0〜最大）・世代が負でない・2 つの配列の長さがそろってシーンの車両の数と同じ・送った時刻が新しい。
        /// expectedLength が UnknownCount なら、シーンの車両の数とは比べない。長さが負は配列がない。
        /// </summary>
        public static bool IsValidState(byte format, int health, int maxHealth, int generation, int playersLength, int numbersLength, int expectedLength,
            float sendTime, float lastUsedTime)
        {
            return format == SyncFormat
                   && health >= 0 && health <= maxHealth
                   && generation >= 0
                   && playersLength >= 0 && playersLength == numbersLength
                   && (expectedLength == UnknownCount || playersLength == expectedLength)
                   && sendTime > lastUsedTime;
        }
    }
}
