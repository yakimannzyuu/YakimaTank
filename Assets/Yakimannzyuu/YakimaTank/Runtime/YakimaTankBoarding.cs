using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 乗車の判定。戦車と同じ大きさのトリガーを Interact すると、座席に座らせる。
    /// 誰かが座っている間は判定を無効にして、戦車全体が Interact の対象として光り続けないようにする。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankBoarding : UdonSharpBehaviour
    {
        [Header("生成で決まる値")]
        public YakimaTankSeat seat;

        /// <summary>Interact を受けるトリガー。無効にすると Interact の対象から外れる。</summary>
        public Collider area;

        public override void Interact()
        {
            if (Utilities.IsValid(seat))
            {
                seat._RequestEnter();
            }
        }

        public void _SetAvailable(bool available)
        {
            if (Utilities.IsValid(area))
            {
                area.enabled = available;
            }
        }
    }
}
