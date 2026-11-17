using UdonSharp;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 命中用のトリガー（仕様書 0.5.0 の 1.7）。車体と砲塔に 1 つずつ。当たった車両を知らせる。
    /// 車体の同期の Udon（車両の番号はそこから分かる）と、命中の申告の送り先の生存の状態（仕様書 0.6.0）を指す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankHitbox : UdonSharpBehaviour
    {
        [UnityEngine.Header("生成で決まる値")]
        public YakimaTankVehicleSync vehicle;
        public YakimaTankLife life;
    }
}
