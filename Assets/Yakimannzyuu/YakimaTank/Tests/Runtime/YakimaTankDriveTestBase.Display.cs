using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using Sync = Yakimannzyuu.YakimaTank.YakimaTankVehicleSync;

namespace Yakimannzyuu.YakimaTank.Tests.PlayMode
{
    /// <summary>
    /// 表示用の速度と送った大きさ（仕様書 0.7.0 の同期の部分、3 節の PlayMode）。
    /// 受ける状態は <see cref="YakimaTankVehicleSync._AcceptState"/> に直接入れる。送った結果は、VRChat と同じく OnPostSerialization をテストが呼ぶ。
    /// </summary>
    public abstract partial class YakimaTankDriveTestBase
    {
        /// <summary>
        /// 値を返す Udon の関数を呼び、返した値を読む。UdonSharp は、引数のない関数の返す値を「__0_関数の名前__ret」の変数に置く
        /// （SDK 3.10.6 の UdonSharp の CompilationContext.GetUniqueID）。名前が変わったら、変数がないことで分かる。
        /// </summary>
        private static T CallReturning<T>(UdonBehaviour udon, string method)
        {
            string symbol = "__0_" + method + "__ret";
            Assert.That(udon.GetProgramVariableType(symbol), Is.Not.Null, method + " の返す値の変数 " + symbol + " がない");
            udon.SendCustomEvent(method);
            return (T)udon.GetProgramVariable(symbol);
        }

        private Vector3 DisplayVelocity => CallReturning<Vector3>(SyncUdon, nameof(Sync._GetDisplayVelocity));

        private Vector3 DisplayAngularVelocity => CallReturning<Vector3>(SyncUdon, nameof(Sync._GetDisplayAngularVelocity));

        [UnityTest]
        public IEnumerator オーナーは表示用の速度と角速度にRigidbodyの値を返す()
        {
            yield return Place();
            SetInput(1f, 1f, 0f);
            yield return PhysicsSeconds(1f);
            Assert.That(Body.velocity.magnitude, Is.GreaterThan(0.5f), "走っていない（判定にならない）");
            Assert.That(Body.angularVelocity.magnitude, Is.GreaterThan(0.1f), "回っていない（判定にならない）");

            Assert.That(Vector3.Distance(DisplayVelocity, Body.velocity), Is.LessThan(1e-4f));
            Assert.That(Vector3.Distance(DisplayAngularVelocity, Body.angularVelocity), Is.LessThan(1e-4f));
        }

        [UnityTest]
        public IEnumerator オーナー以外は表示用の速度と角速度に最新の受けた状態の値を返し止まった印と古い状態と状態がないときは0()
        {
            yield return Place(withPlayer: true);
            VRCPlayerApi owner = Session.SpawnRemotePlayer("B");
            yield return GiveOwnershipTo(owner);
            Assert.That(DisplayVelocity, Is.EqualTo(Vector3.zero), "状態をまだ受けていない");

            int epoch = Epoch;
            var velocity = new Vector3(0f, 0f, 2f);
            var angular = new Vector3(0f, 0.5f, 0f);
            float now = Time.realtimeSinceStartup;

            // 1.5 秒前に送られた状態だけなら、表示の時刻は先読みの上限を過ぎていて、表示も止まっている。
            Inject(owner.playerId, epoch, now - 1.5f, Away(4f), Quaternion.identity, velocity, false, Sync.NoPlayer, angular);
            Assert.That(Read<int>(SyncUdon, nameof(Sync.rejectedCount)), Is.EqualTo(0), "受けた状態を使っていない（判定にならない）");
            Assert.That(DisplayVelocity, Is.EqualTo(Vector3.zero), "先読みの上限を過ぎた古い状態");

            Inject(owner.playerId, epoch, now, Away(5f), Quaternion.identity, velocity, false, Sync.NoPlayer, angular);
            Assert.That(DisplayVelocity, Is.EqualTo(velocity), "最新の受けた速度");
            Assert.That(DisplayAngularVelocity, Is.EqualTo(angular), "最新の受けた角速度");

            Inject(owner.playerId, epoch, now + 0.1f, Away(5.2f), Quaternion.identity, velocity, true, Sync.NoPlayer, angular);
            Assert.That(Read<int>(SyncUdon, nameof(Sync.rejectedCount)), Is.EqualTo(0));
            Assert.That(DisplayVelocity, Is.EqualTo(Vector3.zero), "止まった印");
            Assert.That(DisplayAngularVelocity, Is.EqualTo(Vector3.zero), "止まった印");
        }

        [UnityTest]
        public IEnumerator 車体と武器と生存の状態の同期は送れた大きさを覚え送れなかったときは変えない()
        {
            yield return Place(withPlayer: true);
            foreach (System.Type type in new[] { typeof(Sync), typeof(YakimaTankWeaponSync), typeof(YakimaTankLife) })
            {
                UdonBehaviour udon = Part(type);
                udon.OnPostSerialization(new SerializationResult(true, 77));
                Assert.That(Read<int>(udon, nameof(Sync.lastSentBytes)), Is.EqualTo(77), type.Name);
                udon.OnPostSerialization(new SerializationResult(false, 5));
                Assert.That(Read<int>(udon, nameof(Sync.lastSentBytes)), Is.EqualTo(77), type.Name + " は送れなかった大きさで上書きした");
            }
        }
    }
}
