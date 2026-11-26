using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 砲弾 1 つ（仕様書 0.5.0 の 1.6）。同期しない。武器の下にあらかじめ作り、使い回す。
    /// 前の位置から次の位置へ光線で当たりを見る。命中を数えるのは、撃った人のクライアントの砲弾（counting）だけ。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankShell : UdonSharpBehaviour
    {
        /// <summary>1 回の光線で見る当たりの数の上限。</summary>
        public const int MaxHits = 16;

        [Header("生成で決まる値")]
        public YakimaTankWeaponSync weapon;
        public Renderer shellRenderer;

        [Header("状態（実行中に変わる）")]
        public bool active;
        public bool counting;
        public float age;

        /// <summary>撃ったときの発射の番号。命中の申告に付ける。</summary>
        public int fireNumber;

        private Vector3 _position;
        private Vector3 _velocity;
        private float[] _distances = new float[MaxHits];
        private bool[] _solids = new bool[MaxHits];
        private bool[] _targets = new bool[MaxHits];
        private YakimaTankHitbox[] _hitboxes = new YakimaTankHitbox[MaxHits];

        private void Start()
        {
            _SetVisible(active);
        }

        /// <summary>撃つ。counting は、撃った人のクライアントの砲弾か（命中を数える）。number は発射の番号。</summary>
        public void _Launch(Vector3 position, Vector3 velocity, bool count, int number)
        {
            active = true;
            counting = count;
            fireNumber = number;
            age = 0f;
            _position = position;
            _velocity = velocity;
            transform.position = position;
            _SetVisible(true);
        }

        public void _Stop()
        {
            active = false;
            counting = false;
            _SetVisible(false);
        }

        private void _SetVisible(bool visible)
        {
            if (Utilities.IsValid(shellRenderer))
            {
                shellRenderer.enabled = visible;
            }
        }

        private void Update()
        {
            if (!active || !Utilities.IsValid(weapon))
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            Vector3 next = YakimaTankWeaponSync.ShellPosition(_position, _velocity, weapon.shellGravity, deltaTime);
            Vector3 segment = next - _position;
            float length = segment.magnitude;
            if (length > 0f)
            {
                RaycastHit[] hits = Physics.RaycastAll(_position, segment / length, length, weapon.shellMask, QueryTriggerInteraction.Collide);
                int count = Mathf.Min(hits.Length, MaxHits);
                for (int index = 0; index < count; index++)
                {
                    Collider hitCollider = hits[index].collider;
                    bool trigger = hitCollider.isTrigger;
                    YakimaTankHitbox hitbox = trigger ? hitCollider.GetComponent<YakimaTankHitbox>() : null;
                    _distances[index] = hits[index].distance;
                    _solids[index] = !trigger;
                    _targets[index] = Utilities.IsValid(hitbox) && Utilities.IsValid(hitbox.vehicle) && hitbox.vehicle != weapon.vehicleSync;
                    _hitboxes[index] = hitbox;
                }

                int chosen = YakimaTankWeaponSync.ChooseHit(_distances, _solids, _targets, count);
                if (chosen >= 0)
                {
                    if (counting && _targets[chosen])
                    {
                        weapon._OnShellHit(_hitboxes[chosen], fireNumber);
                    }

                    transform.position = hits[chosen].point;
                    _Stop();
                    return;
                }
            }

            _velocity = YakimaTankWeaponSync.ShellVelocity(_velocity, weapon.shellGravity, deltaTime);
            _position = next;
            transform.position = next;
            age += deltaTime;
            if (age >= weapon.shellLifetime)
            {
                _Stop();
            }
        }
    }
}
