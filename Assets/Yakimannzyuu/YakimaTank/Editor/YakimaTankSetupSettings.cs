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
        public const int CurrentSchemaVersion = 2;

        public int schemaVersion = CurrentSchemaVersion;

        [Header("モデル")]
        public GameObject modelPrefab;

        [Tooltip("モデルの前が +Z、上が +Y になるように、モデルのルートの回転の上から掛ける回転（度）")]
        public Vector3 modelRotation;

        [Header("対応（元のモデルの中の Transform）")]
        public Transform body;
        public Transform[] leftWheels = new Transform[0];
        public Transform[] rightWheels = new Transform[0];

        [Tooltip("座席を置く。なければ車体に置く。砲塔の操作はまだない")]
        public Transform turret;

        [Tooltip("記録だけ。砲身の操作はまだない")]
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
        [Tooltip("砲塔の範囲の上面の中央（自動の位置）から、車体から見た向きでずらす量（m）")]
        public Vector3 seatOffset;

        [Header("操作（VR。Play 中に再適用できる）")]
        [Tooltip("グリップ・トリガーを握った（引いた）とみなす値（0〜1）")]
        public float gripThreshold = 0.5f;

        [Tooltip("ねじりの最大角（度）。これだけねじると入力が端になる")]
        public float maxTwistAngle = 45f;

        [Tooltip("前後の最大距離（m）。これだけ動かすと入力が端になる")]
        public float maxPushDistance = 0.15f;

        [Tooltip("前進後退入力（-1〜1）がこれより後ろにある間は後退のギア")]
        public float reverseThreshold = 0.5f;

        [Tooltip("ねじり・前後・トリガーを -1〜1・0〜1 にした後に掛ける不感帯")]
        public float deadzone = 0.1f;

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
        /// 版 1 → 2：足した座席と VR の項目は、古いアセットに値がないので、読み込みで初期化子の既定値のまま残る。
        /// 走行・足回りの値と参照は名前を変えていないので、そのまま読める。版だけを上げればよい。
        /// </summary>
        public static int Migrate(int version)
        {
            return version == 1 ? 2 : version;
        }
    }
}
