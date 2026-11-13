using System;
using UnityEditor;
using UnityEngine;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// Play 中の調整（ADR 0008）。値は SetProgramVariable（W2）で書く。
    /// 再適用（C 案）は今の車両に書いて戻す。出し直し（A 案）は非アクティブで複製 → 登録 → 書く → アクティブ → 同じフレームで適用。
    /// 「書く → 登録」の順にすると、登録のときに Prefab の値で上書きされて消える（試作で確かめた）。
    /// </summary>
    public static class YakimaTankPlayTuning
    {
        public static void Reapply(UdonBehaviour vehicle, YakimaTankSetupSettings values)
        {
            RequirePlaying();
            if (vehicle == null)
            {
                throw new ArgumentNullException(nameof(vehicle), "再適用する車両がありません。");
            }

            YakimaTankRuntimeValues.WriteToUdon(values, vehicle);
            vehicle.SendCustomEvent(nameof(YakimaTankVehicle._ApplySettingsAndReset));
        }

        /// <summary>今の車両を消し、同じ構造の車両を、前の車両が最初に置かれた位置に出し直す。新しい車両のルートを返す。</summary>
        public static GameObject Respawn(GameObject current, GameObject prefab, YakimaTankSetupSettings values)
        {
            RequirePlaying();
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab), "生成 Prefab がありません。");
            }

            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            Transform parent = null;
            if (current != null)
            {
                parent = current.transform.parent;
                position = current.transform.position;
                rotation = current.transform.rotation;
                UdonBehaviour old = YakimaTankRuntimeValues.FindVehicle(current);
                if (old != null && old.GetProgramVariable(nameof(YakimaTankVehicle.hasSpawnPose)) is bool has && has)
                {
                    position = (Vector3)old.GetProgramVariable(nameof(YakimaTankVehicle.spawnPosition));
                    rotation = (Quaternion)old.GetProgramVariable(nameof(YakimaTankVehicle.spawnRotation));
                }

                Object.Destroy(current);
            }

            var holder = new GameObject("__YakimaTankRespawn");
            holder.SetActive(false);
            GameObject clone = Object.Instantiate(prefab, position, rotation, holder.transform);
            clone.name = prefab.name;
            RegisterAll(clone);
            UdonBehaviour vehicle = YakimaTankRuntimeValues.FindVehicle(clone);
            YakimaTankRuntimeValues.WriteToUdon(values, vehicle);
            vehicle.SetProgramVariable(nameof(YakimaTankVehicle.applyOnStart), false);
            clone.transform.SetParent(parent, true);
            Object.Destroy(holder);
            vehicle.SendCustomEvent(nameof(YakimaTankVehicle._ApplySettingsAndReset));
            return clone;
        }

        /// <summary>VRCInstantiate と同じく、複製して全部の UdonBehaviour を登録する。複製しただけでは Udon が動かない。</summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            RequirePlaying();
            GameObject clone = Object.Instantiate(prefab, position, rotation);
            clone.name = prefab.name;
            RegisterAll(clone);
            return clone;
        }

        private static void RegisterAll(GameObject root)
        {
            foreach (UdonBehaviour behaviour in root.GetComponentsInChildren<UdonBehaviour>(true))
            {
                UdonManager.Instance.RegisterUdonBehaviour(behaviour);
            }
        }

        /// <summary>作業値を設定アセットに保存する。明示的に呼んだときだけ（Play を抜けても自動では保存しない）。</summary>
        public static void SaveWorkingValues(YakimaTankSetupSettings working, YakimaTankSetupSettings asset)
        {
            Undo.RecordObject(asset, "YakimaTank の作業値を保存");
            YakimaTankRuntimeValues.Copy(working, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }

        private static void RequirePlaying()
        {
            if (!EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("Play 中だけ使えます。");
            }
        }
    }
}
