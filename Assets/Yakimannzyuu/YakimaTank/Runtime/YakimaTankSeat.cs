using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 座席。VRCStation の出入りで、乗車の判定と入力を切り替え、降りたら共通操作値と車両の入力を初期値に戻す。
    /// 自分が座ったら車両の所有権を取り、オーナーになってから入力を有効にする。
    /// 同期変数は持たない。NoVariableSync にするのは、座席の出入りを VRChat がほかの人へ伝えるために、
    /// VRCStation のある GameObject をネットワークの対象にしておくため。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class YakimaTankSeat : UdonSharpBehaviour
    {
        public const int NoPlayer = -1;

        /// <summary>座ってから所有権が来るのを待つ時間（秒）。所有権の移動を待つには十分で、乗れないまま長く待たせない長さ。</summary>
        public const float OwnershipTimeout = 3f;

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
        public YakimaTankVehicleSync sync;

        /// <summary>自動の位置（砲塔の範囲の上面の中央）。親（砲塔）の座標。</summary>
        public Vector3 basePosition;

        [Header("状態（実行中に変わる）")]
        public int occupantId = NoPlayer;
        public bool localSeated;

        /// <summary>自分が座って、所有権が来るのを待っている。</summary>
        public bool waitingOwnership;

        /// <summary>
        /// 所有権を待ち始めた回数と、待ちの時間切れを確かめた回数。どちらも同じ長さで予約するので、n 回目の確かめは n 回目の待ちのもの。
        /// 待ち始めた時刻と比べないのは、予約の時計と realtimeSinceStartup がずれて、ちょうど 3 秒の確かめが通らないことがあるため。
        /// </summary>
        public int ownershipWaits;
        public int ownershipChecks;

        private void Start()
        {
            _ApplySeatOffset();
        }

        /// <summary>
        /// ずらしは localPosition に足さない。親（砲塔）に拡大縮小があると、ずらしが m にならないため。
        /// 座席の向きは車体にそろえてあるので、向きを掛けたずらしは車体から見た向きになる。
        /// </summary>
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
            if (!Utilities.IsValid(sync) || sync.isOwnerMode)
            {
                _ActivateDriver();
                return;
            }

            waitingOwnership = true;
            ownershipWaits++;
            Networking.SetOwner(player, sync.gameObject);
            SendCustomEventDelayedSeconds(nameof(_CheckOwnershipTimeout), OwnershipTimeout);
        }

        private void _ActivateDriver()
        {
            waitingOwnership = false;
            bool vr = Networking.LocalPlayer.IsUserInVR();
            desktopInput._SetActive(!vr);
            vrInput._SetActive(vr);
            vehicle._SetLocalDriver(true);
            if (Utilities.IsValid(sync))
            {
                sync._SetDriver(Networking.LocalPlayer.playerId);
            }
        }

        /// <summary>同期の Udon が、所有権が移ったときに呼ぶ。待っていた所有権が来たら入力を有効にし、ほかの人へ移ったら降ろす。</summary>
        public void _OnOwnerChanged()
        {
            if (!localSeated)
            {
                return;
            }

            if (sync.isOwnerMode)
            {
                if (waitingOwnership)
                {
                    _ActivateDriver();
                }
            }
            else
            {
                _ForceExit();
            }
        }

        public void _CheckOwnershipTimeout()
        {
            ownershipChecks++;
            if (ownershipChecks == ownershipWaits && localSeated && waitingOwnership)
            {
                _ForceExit();
            }
        }

        private void _ForceExit()
        {
            waitingOwnership = false;
            _ExitLocal();
        }

        /// <summary>降りる入力。VRCStation の標準の降り方は移動の入力でも降りるので止めてあり、ここで降ろす。</summary>
        public override void InputJump(bool value, UdonInputEventArgs args)
        {
            if (value)
            {
                _RequestExit();
            }
        }

        /// <summary>自分が座っていれば降りる。降りる入力から呼ぶ。</summary>
        public void _RequestExit()
        {
            if (localSeated)
            {
                _ExitLocal();
            }
        }

        private void _ExitLocal()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (Utilities.IsValid(local) && Utilities.IsValid(station))
            {
                station.ExitStation(local);
            }
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
            bool wasDriving = localSeated && !waitingOwnership;
            localSeated = false;
            waitingOwnership = false;
            desktopInput._SetActive(false);
            vrInput._SetActive(false);
            control._ResetToDefaults();
            vehicle._SetLocalDriver(false);
            if (wasDriving && Utilities.IsValid(sync))
            {
                sync._SetDriver(NoPlayer);
            }
        }

        /// <summary>座席の出入りの通知で人が座っている間と、同期した運転手がいる間は、乗車の判定を無効にする（途中で入った人の画面も同期した運転手で無効になる）。</summary>
        public void _UpdateBoarding()
        {
            if (Utilities.IsValid(boarding))
            {
                bool driven = Utilities.IsValid(sync) && sync._HasDriver();
                boarding._SetAvailable(occupantId == NoPlayer && !driven);
            }
        }
    }
}
