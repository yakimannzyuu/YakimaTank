using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// セットアップの正本（ADR 0008）。モデルの対応は元のモデルのアセットの中のオブジェクトを指し、
    /// シーンのインスタンスは指さない。生成物から逆算しない。
    /// </summary>
    [CreateAssetMenu(fileName = "YakimaTankSetup", menuName = "YakimaTank/Setup Settings")]
    public sealed class YakimaTankSetupSettings : ScriptableObject, ISerializationCallbackReceiver
    {
        public const int CurrentSchemaVersion = 6;

        public int schemaVersion = CurrentSchemaVersion;

        [Header("モデル")]
        public GameObject modelPrefab;

        [Tooltip("モデルの前が +Z、上が +Y になるように、モデルのルートの回転の上から掛ける回転（度）")]
        public Vector3 modelRotation;

        [Header("対応（元のモデルの中の Transform）")]
        public Transform body;
        public Transform[] leftWheels = new Transform[0];
        public Transform[] rightWheels = new Transform[0];

        [Tooltip("左右に回す。座席はこの上に付いて行く")]
        public Transform turret;

        [Tooltip("上下に回す。砲塔の下（子孫）にあること。前の端が砲口")]
        public Transform barrel;

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

        [Header("座席（Play 中に再適用できる）")]
        [Tooltip("砲塔の範囲の上面の中央（自動の位置）から、砲塔から見た向きでずらす量（m）。砲塔が回ると、ずらした位置も一緒に回る")]
        public Vector3 seatOffset;

        [Header("操作（VR。Play 中に再適用できる）")]
        [Tooltip("グリップ・トリガーを握った（引いた）とみなす値（0〜1）")]
        public float gripThreshold = 0.5f;

        [Tooltip("ねじりの最大角（度）。手の先をこれだけ左右・上下に向けると入力が端になる")]
        public float maxTwistAngle = 45f;

        [Tooltip("前後の最大距離（m。左手だけ）。これだけ動かすと入力が端になる")]
        public float maxPushDistance = 0.15f;

        [Tooltip("前進後退入力（-1〜1）がこれより後ろにある間は後退のギア")]
        public float reverseThreshold = 0.5f;

        [Tooltip("向き・前後・トリガーを -1〜1・0〜1 にした後に掛ける不感帯")]
        public float deadzone = 0.1f;

        [Header("同期（Play 中に再適用できる）")]
        [Tooltip("動いている間に送る間隔（秒、0.05〜1）")]
        public float sendInterval = 0.1f;

        [Tooltip("新しい状態が届かないときに、速度で先へ進めて見せる上限（秒、0〜1）")]
        public float maxExtrapolation = 0.25f;

        [Header("接触（Play 中に再適用できる）")]
        [Tooltip("相手へ寄る速さを消す速さ（1/秒、0 以上）。10 なら 0.1 秒ほどで消す")]
        public float contactStopRate = 10f;

        [Tooltip("重なりの深さあたりの押し返す加速（1/秒²、0 以上）")]
        public float contactPushStrength = 20f;

        [Tooltip("押し返す加速の上限（m/s²、0 以上）。停止中のブレーキの減速より大きいと、無人の車両を押せる")]
        public float contactMaxPushAcceleration = 5f;

        [Header("砲塔・武器（Play 中に再適用できる）")]
        [Tooltip("砲塔の回る速さ（度/秒、0 より大きい）")]
        public float turretTurnRate = 30f;

        [Tooltip("砲身の回る速さ（度/秒、0 より大きい）")]
        public float gunTurnRate = 15f;

        [Tooltip("砲身の上下の下限（度、上が正。-90〜0）")]
        public float gunPitchMin = -10f;

        [Tooltip("砲身の上下の上限（度、上が正。0〜90）")]
        public float gunPitchMax = 20f;

        [Tooltip("弾速（m/s、0 より大きい）")]
        public float shellSpeed = 200f;

        [Tooltip("砲弾に掛かる重力（m/s²、0 以上）")]
        public float shellGravity = 9.81f;

        [Tooltip("砲弾の寿命（秒、0 より大きい）")]
        public float shellLifetime = 3f;

        [Tooltip("装填の時間（秒）。同期の送る間隔より長くする")]
        public float reloadTime = 3f;

        [Header("HP・復活（Play 中に再適用できる）")]
        [Tooltip("最大 HP（1 以上）。整数に四捨五入する。Play 中に変えると、次の復活で最大になる")]
        public float maxHealth = 100f;

        [Tooltip("1 発のダメージ（0 以上）。撃った車両のこの値で、当たった車両の HP を引く。整数に四捨五入する")]
        public float damage = 25f;

        [Tooltip("破壊から復活までの待ち時間（秒、0 以上）")]
        public float respawnDelay = 5f;

        [Header("足回り（生成で反映）")]
        [Tooltip("0 なら転輪のメッシュから求める")]
        public float wheelRadius;

        public float wheelMass = 50f;
        public float suspensionDistance = 0.2f;

        [Tooltip("0 なら質量と転輪の数から求める")]
        public float suspensionSpring;

        [Tooltip("0 なら質量と転輪の数から求める")]
        public float suspensionDamper;

        public float forwardFrictionStiffness = 1f;
        public float sidewaysFrictionStiffness = 1f;

        [Header("出力")]
        [Tooltip("利用者の Assets の下の .prefab")]
        public string outputPrefabPath = string.Empty;

        [Header("配置（シーンへの配置で書く）")]
        public string placementId = string.Empty;
        public bool hasPlacement;
        public Vector3 placementPosition;
        public Vector3 placementRotation;

        public void OnBeforeSerialize()
        {
        }

        /// <summary>読み込んだときに今の版へ移す。書き戻しは、利用者が次に保存したとき。</summary>
        public void OnAfterDeserialize()
        {
            schemaVersion = Migrate(schemaVersion);
        }

        /// <summary>
        /// 版 1 → 2（座席と VR の項目）、版 2 → 3（同期の項目）、版 3 → 4（接触の項目）、版 4 → 5（砲塔・武器の項目）、版 5 → 6（HP・復活の項目）：
        /// 足した項目は、古いアセットに値がないので、読み込みで初期化子の既定値のまま残る。
        /// 前からある値と参照は名前を変えていないので、そのまま読める。版だけを上げればよい。今より新しい版は変えない（検証で Error）。
        /// </summary>
        public static int Migrate(int version)
        {
            return version >= 1 && version < CurrentSchemaVersion ? CurrentSchemaVersion : version;
        }
    }
}
