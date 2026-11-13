using UnityEngine;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// セットアップの正本（ADR 0008）。モデルの対応は元のモデルのアセットの中のオブジェクトを指し、
    /// シーンのインスタンスは指さない。生成物から逆算しない。
    /// </summary>
    [CreateAssetMenu(fileName = "YakimaTankSetup", menuName = "YakimaTank/Setup Settings")]
    public sealed class YakimaTankSetupSettings : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;

        [Header("モデル")]
        public GameObject modelPrefab;

        [Tooltip("モデルの前が +Z、上が +Y になるように、モデルのルートの回転の上から掛ける回転（度）")]
        public Vector3 modelRotation;

        [Header("対応（元のモデルの中の Transform）")]
        public Transform body;
        public Transform[] leftWheels = new Transform[0];
        public Transform[] rightWheels = new Transform[0];

        [Tooltip("記録だけ。砲塔の操作はまだない")]
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
    }
}
