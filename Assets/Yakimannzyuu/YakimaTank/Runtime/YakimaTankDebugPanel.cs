using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// デバッグ UI。指定した車両の共通操作値を、箱（Cube）の大きさで見せ、同期の状態を文字で見せる。各自の画面だけの表示で、同期しない。
    /// 文字は英字だけ（日本語のフォントを同梱しないため）。見た目の部品は Editor のビルダーが作る。
    /// 同期の文字は UI の Text に書く。Udon は TextMesh の文字を書き換えられない（公開されていない）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankDebugPanel : UdonSharpBehaviour
    {
        /// <summary>ゲージの枠の半分の幅（パネルの座標）。-1〜1 の値をこの幅に割り当てる。</summary>
        public const float GaugeHalfWidth = 0.5f;

        [Header("表示する車両")]
        public YakimaTankVehicle vehicle;

        [Header("置き方")]
        [Tooltip("車両に付いて行く。外すと置いた場所に留まる")]
        public bool followVehicle = true;

        [Tooltip("付いて行くときの、車体から見た位置（m）")]
        public Vector3 followPosition = new Vector3(-2f, 2.5f, 3f);

        [Tooltip("付いて行くときの、車体から見た向き（度）")]
        public Vector3 followRotation = new Vector3(0f, -30f, 0f);

        [Header("見た目の部品（ビルダーが作る）")]
        public Transform steeringBar;
        public Transform leftLeverBar;
        public Transform rightLeverBar;
        public Transform throttleBar;
        public Transform driveAxisBar;
        public Transform turretYawBar;
        public Transform gunPitchBar;
        public GameObject gearForward;
        public GameObject gearReverse;
        public GameObject fireIndicator;
        public Text syncText;

        /// <summary>同期の文字を書き直す間隔（秒）。毎フレーム文字列を作らない。</summary>
        private const float TextInterval = 0.25f;

        private float _nextTextTime;
        private float _rateStart;
        private int _rateSent;
        private int _rateRequested;
        private float _sentRate;
        private float _requestedRate;

        private void LateUpdate()
        {
            if (!Utilities.IsValid(vehicle))
            {
                return;
            }

            if (followVehicle)
            {
                Transform body = vehicle.transform;
                transform.SetPositionAndRotation(body.TransformPoint(followPosition), body.rotation * Quaternion.Euler(followRotation));
            }

            _UpdateSyncText();
            YakimaTankControlState control = vehicle.control;
            if (!Utilities.IsValid(control))
            {
                return;
            }

            _SetBar(steeringBar, control.steering);
            _SetBar(leftLeverBar, control.leftLever);
            _SetBar(rightLeverBar, control.rightLever);
            _SetBar(throttleBar, control.throttle);
            _SetBar(driveAxisBar, control.driveAxis);
            _SetBar(turretYawBar, control.turretYawInput);
            _SetBar(gunPitchBar, control.gunPitchInput);
            bool reverse = control.gear < 0;
            _SetActive(gearForward, !reverse);
            _SetActive(gearReverse, reverse);
            _SetActive(fireIndicator, control.fire);
        }

        private void _UpdateSyncText()
        {
            YakimaTankVehicleSync sync = vehicle.sync;
            if (!Utilities.IsValid(syncText) || !Utilities.IsValid(sync))
            {
                return;
            }

            float now = Time.time;
            if (now - _rateStart >= 1f)
            {
                float span = now - _rateStart;
                _sentRate = (sync.sentCount - _rateSent) / span;
                _requestedRate = (sync.requestedCount - _rateRequested) / span;
                _rateStart = now;
                _rateSent = sync.sentCount;
                _rateRequested = sync.requestedCount;
            }

            if (now < _nextTextTime)
            {
                return;
            }

            _nextTextTime = now + TextInterval;
            VRCPlayerApi local = Networking.LocalPlayer;
            bool ownerIsYou = Utilities.IsValid(local) && local.playerId == sync.ownerId;
            int driver = sync._HasDriver() ? sync.driverId : YakimaTankVehicleSync.NoPlayer;
            syncText.text = SyncSummary(sync.isOwnerMode, sync.ownerId, ownerIsYou, driver, sync.epoch, sync._GetAge(), _sentRate, _requestedRate, sync.rejectedCount);
        }

        /// <summary>同期の状態の文字（仕様書 0.3.0 の 1.7）。オーナーは送った回数と要求した回数、オーナー以外は最新の状態の古さを出す。</summary>
        public static string SyncSummary(bool owner, int ownerId, bool ownerIsYou, int driverId, int epoch, float age, float sentRate, float requestedRate, int rejected)
        {
            string text = (owner ? "OWNER" : "REMOTE")
                          + "\nOwner " + ownerId + (ownerIsYou ? " (you)" : "")
                          + "\nDriver " + (driverId < 0 ? "-" : driverId.ToString())
                          + "\nEpoch " + epoch;
            text += owner
                ? "\nSent/s " + sentRate.ToString("F1") + "  Req/s " + requestedRate.ToString("F1")
                : "\nAge " + (age < 0f ? "-" : age.ToString("F2"));
            return text + "\nRejected " + rejected;
        }

        private void _SetBar(Transform bar, float value)
        {
            if (!Utilities.IsValid(bar))
            {
                return;
            }

            Vector3 position = bar.localPosition;
            position.x = GaugeCenter(value, GaugeHalfWidth);
            bar.localPosition = position;
            Vector3 scale = bar.localScale;
            scale.x = GaugeWidth(value, GaugeHalfWidth);
            bar.localScale = scale;
        }

        private void _SetActive(GameObject target, bool active)
        {
            if (Utilities.IsValid(target) && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        /// <summary>ゲージの箱の中心。0 の目盛りから値の分だけ伸ばした箱の真ん中。範囲外は端で止める。</summary>
        public static float GaugeCenter(float value, float halfWidth)
        {
            return Mathf.Clamp(value, -1f, 1f) * halfWidth * 0.5f;
        }

        /// <summary>ゲージの箱の幅。範囲外は端で止める。</summary>
        public static float GaugeWidth(float value, float halfWidth)
        {
            return Mathf.Abs(Mathf.Clamp(value, -1f, 1f)) * halfWidth;
        }
    }
}
