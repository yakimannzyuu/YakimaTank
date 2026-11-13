using System;
using System.Collections.Generic;
using System.Reflection;
using UdonSharpEditor;
using UnityEngine;
using VRC.Udon;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// 「走行」の値。設定アセットと車両の Udon で同じ名前のフィールドにし、生成でも Play 中の再適用でも、この一覧だけを写す。
    /// 名前がずれると片方にだけ値が入るので、テストで両方にあることを確かめている。
    /// </summary>
    public static class YakimaTankRuntimeValues
    {
        public static readonly IReadOnlyList<string> Names = new[]
        {
            nameof(YakimaTankVehicle.mass),
            nameof(YakimaTankVehicle.driveTorque),
            nameof(YakimaTankVehicle.brakeTorque),
            nameof(YakimaTankVehicle.idleBrakeTorque),
            nameof(YakimaTankVehicle.maxForwardSpeed),
            nameof(YakimaTankVehicle.maxReverseSpeed),
            nameof(YakimaTankVehicle.maxTurnRate),
            nameof(YakimaTankVehicle.turnAcceleration),
            nameof(YakimaTankVehicle.centerOfMassOffset)
        };

        public static FieldInfo SettingsField(string name)
        {
            return typeof(YakimaTankSetupSettings).GetField(name, BindingFlags.Public | BindingFlags.Instance);
        }

        public static FieldInfo VehicleField(string name)
        {
            return typeof(YakimaTankVehicle).GetField(name, BindingFlags.Public | BindingFlags.Instance);
        }

        /// <summary>編集モードで、車両の proxy に書く（生成のとき）。</summary>
        public static void WriteToProxy(YakimaTankSetupSettings settings, YakimaTankVehicle proxy)
        {
            foreach (string name in Names)
            {
                VehicleField(name).SetValue(proxy, SettingsField(name).GetValue(settings));
            }
        }

        /// <summary>
        /// Play 中に、実行中の Udon に書く（W2）。登録の前に書くと黙って捨てられるので、登録済みの UdonBehaviour に使う。
        /// </summary>
        public static void WriteToUdon(YakimaTankSetupSettings settings, UdonBehaviour vehicle)
        {
            foreach (string name in Names)
            {
                vehicle.SetProgramVariable(name, SettingsField(name).GetValue(settings));
            }
        }

        public static void Copy(YakimaTankSetupSettings from, YakimaTankSetupSettings to)
        {
            foreach (string name in Names)
            {
                FieldInfo field = SettingsField(name);
                field.SetValue(to, field.GetValue(from));
            }
        }

        /// <summary>シーンの GameObject から、車両の UdonBehaviour を探す（子も含む）。</summary>
        public static UdonBehaviour FindVehicle(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            foreach (UdonBehaviour behaviour in root.GetComponentsInChildren<UdonBehaviour>(true))
            {
                if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(behaviour) == typeof(YakimaTankVehicle))
                {
                    return behaviour;
                }
            }

            return null;
        }

        public static T Get<T>(UdonBehaviour vehicle, string name)
        {
            object value = vehicle.GetProgramVariable(name);
            if (value is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException($"車両の Udon の {name} を読めません（{value?.GetType().Name ?? "null"}）。");
        }
    }
}
