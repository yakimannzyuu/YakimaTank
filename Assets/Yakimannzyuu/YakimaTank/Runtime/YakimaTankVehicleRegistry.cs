using UdonSharp;
using VRC.SDKBase;

namespace Yakimannzyuu.YakimaTank
{
    /// <summary>
    /// シーンの車両の一覧。Editor が作り直し、順番をシーンの中の車両の番号にする（どのクライアントでも同じ）。
    /// 実行中は、始まるときに各車両へ自分を知らせ、車両は一覧から相手を知る（毎フレーム探さない）。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class YakimaTankVehicleRegistry : UdonSharpBehaviour
    {
        public YakimaTankVehicleSync[] vehicles = new YakimaTankVehicleSync[0];

        private void Start()
        {
            for (int index = 0; index < vehicles.Length; index++)
            {
                if (Utilities.IsValid(vehicles[index]))
                {
                    vehicles[index]._SetRegistry(this, index);
                }
            }
        }
    }
}
