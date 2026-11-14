using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 座席。VRCStation の出入りで、乗車の判定と入力を切り替え、降りたら共通操作値と車両の入力を初期値に戻す。
    /// 同期変数は持たない。NoVariableSync にするのは、座席の出入りを VRChat がほかの人へ伝えるために、
    /// VRCStation のある GameObject をネットワークの対象にしておくため。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class YakimaTankSeat : UdonSharpBehaviour
    {
        public const int NoPlayer = -1;

        [Header("座席（Play 中に再適用できる）")]
        [Tooltip("自動の位置から、車体から見た向きでずらす量（m）")]
        public Vector3 seatOffset;

        [Header("生成で決まる値")]
        public VRCStation station;
        public YakimaTankBoarding boarding;
        public YakimaTankVehicle vehicle;
        public YakimaTankControlState control;
        public YakimaTankDesktopInput desktopInput;
        public YakimaTankVRInput vrInput;

        /// <summary>自動の位置（砲塔の範囲の上面の中央）。親（砲塔）の座標。</summary>
        public Vector3 basePosition;

        [Header("状態（実行中に変わる）")]
        public int occupantId = NoPlayer;
        public bool localSeated;

        private void Start()
        {
            _ApplySeatOffset();
        }

        /// <summary>自動の位置にずらしを足した所へ座席を置く。座席の向きは車体にそろえてあるので、ずらしは車体から見た向きになる。</summary>
        public void _ApplySeatOffset()
        {
            Transform parent = transform.parent;
            Vector3 origin = Utilities.IsValid(parent) ? parent.TransformPoint(basePosition) : basePosition;
            transform.position = origin + transform.rotation * seatOffset;
        }

        /// <summary>乗車の判定から呼ぶ。空いていれば自分を座らせる。</summary>
        public void _RequestEnter()
        {
            if (occupantId != NoPlayer || !Utilities.IsValid(station))
            {
                return;
            }

            VRCPlayerApi player = Networking.LocalPlayer;
            if (Utilities.IsValid(player))
            {
                station.UseStation(player);
            }
        }

        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
            {
                return;
            }

            occupantId = player.playerId;
            _UpdateBoarding();
            if (!player.isLocal)
            {
                return;
            }

            localSeated = true;
            bool vr = player.IsUserInVR();
            desktopInput._SetActive(!vr);
            vrInput._SetActive(vr);
            vehicle._SetLocalDriver(true);
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player))
            {
                return;
            }

            if (player.playerId == occupantId)
            {
                occupantId = NoPlayer;
            }

            if (player.isLocal)
            {
                _ReleaseLocal();
            }

            _UpdateBoarding();
        }

        /// <summary>座っていた人が抜けたとき、座席の出入りの通知が来ないことがあるので、ここでも戻す。</summary>
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (Utilities.IsValid(player) && player.playerId == occupantId)
            {
                occupantId = NoPlayer;
                _UpdateBoarding();
            }
        }

        private void _ReleaseLocal()
        {
            localSeated = false;
            desktopInput._SetActive(false);
            vrInput._SetActive(false);
            control._ResetToDefaults();
            vehicle._SetLocalDriver(false);
        }

        private void _UpdateBoarding()
        {
            if (Utilities.IsValid(boarding))
            {
                boarding._SetAvailable(occupantId == NoPlayer);
            }
        }
    }
}
