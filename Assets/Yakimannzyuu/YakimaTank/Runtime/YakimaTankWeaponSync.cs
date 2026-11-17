using UdonSharp;
using UnityEngine;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>武器の同期（仕様書 0.5.0）。</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class YakimaTankWeaponSync : UdonSharpBehaviour
    {
        public const byte SyncFormat = 1;

        /// <summary>受けた上下の角度の範囲の確かめで許す幅（度）。設定を Play 中に直した直後の丸めを弾かないため。</summary>
        public const float AngleTolerance = 0.5f;

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
