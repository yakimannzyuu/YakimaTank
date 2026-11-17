using UdonSharp;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 共通操作値。入力の方法（Desktop・VR・将来の物理レバーなど）と車両の動きを分ける。
    /// 入力アダプターが書き、車両とデバッグ UI が読む。各自の画面だけの値で、同期しない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankControlState : UdonSharpBehaviour
    {
        public const int GearForward = 1;
        public const int GearReverse = -1;

        /// <summary>ハンドル。-1〜1、正で右。</summary>
        public float steering;

        /// <summary>左右の旋回レバー。0〜1。この版では値と表示だけ。</summary>
        public float leftLever;

        public float rightLever;

        /// <summary>アクセル。0〜1。ギアの向きに進む。</summary>
        public float throttle;

        /// <summary>前後の操作そのもの。-1〜1、正で前。表示と、VR で後退のギアを決めるのに使う。</summary>
        public float driveAxis;

        /// <summary>1 前進・-1 後退。</summary>
        public int gear = GearForward;

        /// <summary>ブレーキ。0〜1。この版では値だけ。</summary>
        public float brake;

        /// <summary>砲の左右入力。-1〜1、正で右。この版では表示だけ。</summary>
        public float turretYawInput;

        /// <summary>砲の上下入力。-1〜1、正で上。この版では表示だけ。</summary>
        public float gunPitchInput;

        /// <summary>射撃。押している間 true。この版では表示だけ。</summary>
        public bool fire;

        /// <summary>降りたときの初期値に戻す。</summary>
        public void _ResetToDefaults()
        {
            steering = 0f;
            leftLever = 0f;
            rightLever = 0f;
            throttle = 0f;
            driveAxis = 0f;
            gear = GearForward;
            brake = 0f;
            turretYawInput = 0f;
            gunPitchInput = 0f;
            fire = false;
        }
    }
}
