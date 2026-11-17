using UdonSharp;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// 命中用のトリガー（仕様書 0.5.0 の 1.7）。車体と砲塔に 1 つずつ。当たった車両を知らせる。
    /// 0.5.0 は車体の同期の Udon を指す（車両の番号はそこから分かる）。HP への接続は 0.6.0 で足す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankHitbox : UdonSharpBehaviour
    {
        [UnityEngine.Header("生成で決まる値")]
        public YakimaTankVehicleSync vehicle;
    }
}
