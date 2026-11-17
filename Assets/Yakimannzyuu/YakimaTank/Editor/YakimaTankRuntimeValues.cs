using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharpEditor;
using UnityEngine;
using VRC.Udon;

namespace Yakimannzyuu.YakimaTank.Editor
{
    /// <summary>
    /// Play 中に再適用できる値。設定アセットと、値を受ける Udon（車両・座席・VR の入力）で同じ名前のフィールドにし、
    /// 生成でも Play 中の再適用でも、この一覧だけを写す。名前がずれると片方にだけ値が入るので、テストで両方にあることを確かめている。
    /// </summary>
    public static class YakimaTankRuntimeValues
    {
        /// <summary>走行。車両の Udon が受ける。</summary>
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

        /// <summary>座席。座席の Udon が受ける。</summary>
        public static readonly IReadOnlyList<string> SeatNames = new[]
        {
            nameof(YakimaTankSeat.seatOffset)
        };

        /// <summary>操作（VR）。VR の入力の Udon が受ける。</summary>
        public static readonly IReadOnlyList<string> VRInputNames = new[]
        {
            nameof(YakimaTankVRInput.gripThreshold),
            nameof(YakimaTankVRInput.maxTwistAngle),
            nameof(YakimaTankVRInput.maxPushDistance),
            nameof(YakimaTankVRInput.reverseThreshold),
            nameof(YakimaTankVRInput.deadzone)
        };

        /// <summary>値を受ける Udon の型と、その型が受ける名前の組。</summary>
        public static readonly IReadOnlyList<KeyValuePair<Type, IReadOnlyList<string>>> Groups = new[]
        {
            new KeyValuePair<Type, IReadOnlyList<string>>(typeof(YakimaTankVehicle), Names),
            new KeyValuePair<Type, IReadOnlyList<string>>(typeof(YakimaTankSeat), SeatNames),
            new KeyValuePair<Type, IReadOnlyList<string>>(typeof(YakimaTankVRInput), VRInputNames)
        };

        public static IEnumerable<string> AllNames => Groups.SelectMany(group => group.Value);

        public static FieldInfo SettingsField(string name)
        {
            return typeof(YakimaTankSetupSettings).GetField(name, BindingFlags.Public | BindingFlags.Instance);
        }

        public static FieldInfo VehicleField(string name)
        {
            return BehaviourField(typeof(YakimaTankVehicle), name);
        }

        public static FieldInfo BehaviourField(Type behaviour, string name)
        {
            return behaviour.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        }

        /// <summary>編集モードで、Udon の proxy に書く（生成のとき）。型に合う名前の組だけを書く。</summary>
        public static void WriteToProxy(YakimaTankSetupSettings settings, UdonSharpBehaviour proxy)
        {
            foreach (string name in NamesFor(proxy.GetType()))
            {
                BehaviourField(proxy.GetType(), name).SetValue(proxy, SettingsField(name).GetValue(settings));
            }
        }

        /// <summary>
        /// Play 中に、実行中の Udon に書く（W2）。登録の前に書くと黙って捨てられるので、登録済みの UdonBehaviour に使う。
        /// </summary>
        public static void WriteToUdon(YakimaTankSetupSettings settings, UdonBehaviour behaviour)
        {
            foreach (string name in NamesFor(UdonSharpEditorUtility.GetUdonSharpBehaviourType(behaviour)))
            {
                behaviour.SetProgramVariable(name, SettingsField(name).GetValue(settings));
            }
        }

        public static IReadOnlyList<string> NamesFor(Type behaviour)
        {
            foreach (KeyValuePair<Type, IReadOnlyList<string>> group in Groups)
            {
                if (group.Key == behaviour)
                {
                    return group.Value;
                }
            }

            return Array.Empty<string>();
        }

        public static void Copy(YakimaTankSetupSettings from, YakimaTankSetupSettings to)
        {
            foreach (string name in AllNames)
            {
                FieldInfo field = SettingsField(name);
                field.SetValue(to, field.GetValue(from));
            }
        }

        /// <summary>シーンの GameObject から、車両の UdonBehaviour を探す（子も含む）。</summary>
        public static UdonBehaviour FindVehicle(GameObject root)
        {
            return FindBehaviour(root, typeof(YakimaTankVehicle));
        }

        /// <summary>シーンの GameObject から、指定した U# の型の UdonBehaviour を探す（子も含む）。</summary>
        public static UdonBehaviour FindBehaviour(GameObject root, Type type)
        {
            if (root == null)
            {
                return null;
            }

            foreach (UdonBehaviour behaviour in root.GetComponentsInChildren<UdonBehaviour>(true))
            {
                if (UdonSharpEditorUtility.GetUdonSharpBehaviourType(behaviour) == type)
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
