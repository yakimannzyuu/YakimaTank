using NUnit.Framework;
using UnityEngine;
using Sync = Yakimannzyuu.YakimaTank.YakimaTankVehicleSync;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    /// <summary>車体の同期の計算（仕様書 0.3.0 の 1.4・1.5）。</summary>
    public class YakimaTankSyncMathTests
    {
        private const int Owner = 1;
        private const int Other = 2;
        private const float Interval = 0.1f;

        private static int Classify(float sendTime = 10f, float lastUsed = 9f, int sender = Owner, byte format = Sync.SyncFormat,
            Vector3? position = null, Quaternion? rotation = null, Vector3? velocity = null, Vector3? angular = null)
        {
            return Sync.ClassifyState(format, position ?? new Vector3(1f, 2f, 3f), rotation ?? Quaternion.Euler(0f, 30f, 0f),
                velocity ?? new Vector3(0f, 0f, 2f), angular ?? new Vector3(0f, 0.5f, 0f), sendTime, lastUsed, sender, Owner);
        }

        [Test]
        public void 今のオーナーが送った正しい状態は使う()
        {
            Assert.That(Classify(), Is.EqualTo(Sync.StateUse));
        }

        [Test]
        public void ほかが正しく送った人だけが今のオーナーと違う状態は保留にする()
        {
            Assert.That(Classify(sender: Other), Is.EqualTo(Sync.StateHold));
        }

        [Test]
        public void 送った人がいない状態はオーナーが分からなくても使わない()
        {
            int kind = Sync.ClassifyState(Sync.SyncFormat, new Vector3(1f, 2f, 3f), Quaternion.identity, Vector3.zero, Vector3.zero,
                10f, 9f, Sync.NoPlayer, Sync.NoPlayer);
            Assert.That(kind, Is.EqualTo(Sync.StateReject));
        }

        [Test]
        public void 形式が違う状態は使わない()
        {
            Assert.That(Classify(format: 2), Is.EqualTo(Sync.StateReject));
        }

        [Test]
        public void 有限でない値の状態は使わない()
        {
            Assert.That(Classify(position: new Vector3(float.NaN, 0f, 0f)), Is.EqualTo(Sync.StateReject), "位置の NaN");
            Assert.That(Classify(velocity: new Vector3(0f, float.PositiveInfinity, 0f)), Is.EqualTo(Sync.StateReject), "速度の無限大");
            Assert.That(Classify(angular: new Vector3(0f, 0f, float.NaN)), Is.EqualTo(Sync.StateReject), "角速度の NaN");
            Assert.That(Classify(rotation: new Quaternion(float.NaN, 0f, 0f, 1f)), Is.EqualTo(Sync.StateReject), "回転の NaN");
            Assert.That(Classify(sendTime: float.NaN), Is.EqualTo(Sync.StateReject), "時刻の NaN");
        }

        [Test]
        public void 回転の大きさが1から離れた状態は使わない()
        {
            Assert.That(Classify(rotation: new Quaternion(0f, 0f, 0f, 1.5f)), Is.EqualTo(Sync.StateReject));
            Assert.That(Classify(rotation: new Quaternion(0f, 0f, 0f, 1.005f)), Is.EqualTo(Sync.StateUse), "丸めの範囲");
        }

        [Test]
        public void 位置と速さと角速度が上限を超えた状態は使わない()
        {
            Assert.That(Classify(position: new Vector3(0f, 0f, 100001f)), Is.EqualTo(Sync.StateReject), "位置");
            Assert.That(Classify(position: new Vector3(-100000f, 0f, 0f)), Is.EqualTo(Sync.StateUse), "位置の上限ちょうど");
            Assert.That(Classify(velocity: new Vector3(0f, 0f, 100.1f)), Is.EqualTo(Sync.StateReject), "速さ");
            Assert.That(Classify(angular: new Vector3(0f, 20.1f, 0f)), Is.EqualTo(Sync.StateReject), "角速度");
        }

        [Test]
        public void 前に使った状態より新しくない状態は使わない()
        {
            Assert.That(Classify(sendTime: 9f, lastUsed: 9f), Is.EqualTo(Sync.StateReject), "同じ時刻");
            Assert.That(Classify(sendTime: 8f, lastUsed: 9f), Is.EqualTo(Sync.StateReject), "古い時刻");
            Assert.That(Classify(sendTime: 8f, lastUsed: 9f, sender: Other), Is.EqualTo(Sync.StateReject), "保留にもしない");
            Assert.That(Classify(sendTime: -5f, lastUsed: float.NegativeInfinity), Is.EqualTo(Sync.StateUse), "まだ使っていなければ負の時刻も使う");
        }

        [Test]
        public void 表示する時刻の前後の状態を選ぶ()
        {
            float[] times = { 1f, 1.1f, 1.2f, 0f };
            Assert.That(Sync.FindSegment(times, 3, 1.05f), Is.EqualTo(0));
            Assert.That(Sync.FindSegment(times, 3, 1.1f), Is.EqualTo(1));
            Assert.That(Sync.FindSegment(times, 3, 0.5f), Is.EqualTo(-1), "最初より前");
            Assert.That(Sync.FindSegment(times, 3, 1.3f), Is.EqualTo(2), "最新より後");
        }

        [Test]
        public void エルミート補間は両端で状態と一致し間で速度に沿う()
        {
            var p0 = new Vector3(0f, 0f, 0f);
            var p1 = new Vector3(0f, 0f, 1f);
            var v = new Vector3(0f, 0f, 10f);
            Assert.That(Vector3.Distance(Sync.Hermite(p0, v, 0f, p1, v, 0.1f, 0f), p0), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(Sync.Hermite(p0, v, 0f, p1, v, 0.1f, 0.1f), p1), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(Sync.Hermite(p0, v, 0f, p1, v, 0.1f, 0.05f), new Vector3(0f, 0f, 0.5f)), Is.LessThan(1e-5f), "等速なら直線の上");

            // 両端の速度が 0 なら、真ん中で半分、4 分の 1 では半分より手前（速度に沿って滑らかに動き出す）。
            Vector3 quarter = Sync.Hermite(p0, Vector3.zero, 0f, p1, Vector3.zero, 1f, 0.25f);
            Assert.That(quarter.z, Is.LessThan(0.25f));
            Assert.That(Sync.Hermite(p0, Vector3.zero, 0f, p1, Vector3.zero, 1f, 0.5f).z, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void 姿勢は球面線形補間する()
        {
            Quaternion r0 = Quaternion.Euler(0f, 0f, 0f);
            Quaternion r1 = Quaternion.Euler(0f, 90f, 0f);
            Assert.That(Quaternion.Angle(Sync.SlerpBetween(r0, 0f, r1, 1f, 0.5f), Quaternion.Euler(0f, 45f, 0f)), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(Sync.SlerpBetween(r0, 0f, r1, 1f, 2f), r1), Is.LessThan(0.01f), "範囲の外は端で止める");
        }

        [Test]
        public void 先読みは上限まで速度で進み上限を超えるとそれ以上動かない()
        {
            var position = new Vector3(0f, 0f, 0f);
            var velocity = new Vector3(0f, 0f, 4f);
            Assert.That(Sync.ExtrapolatePosition(position, velocity, 0.1f, 0.25f).z, Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(Sync.ExtrapolatePosition(position, velocity, 0.25f, 0.25f).z, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Sync.ExtrapolatePosition(position, velocity, 3f, 0.25f).z, Is.EqualTo(1f).Within(1e-5f), "上限の先");

            var angular = new Vector3(0f, 90f * Mathf.Deg2Rad, 0f);
            Assert.That(Quaternion.Angle(Sync.ExtrapolateRotation(Quaternion.identity, angular, 0.1f, 0.25f), Quaternion.Euler(0f, 9f, 0f)), Is.LessThan(0.01f));
            Assert.That(Quaternion.Angle(Sync.ExtrapolateRotation(Quaternion.identity, angular, 3f, 0.25f), Quaternion.Euler(0f, 22.5f, 0f)), Is.LessThan(0.01f), "上限の先");
        }

        private static float[] _times;
        private static Vector3[] _positions;
        private static Quaternion[] _rotations;
        private static Vector3[] _velocities;
        private static Vector3[] _angulars;
        private static bool[] _stops;

        private static int History(params (float time, float z, bool stop)[] states)
        {
            _times = new float[Sync.HistorySize];
            _positions = new Vector3[Sync.HistorySize];
            _rotations = new Quaternion[Sync.HistorySize];
            _velocities = new Vector3[Sync.HistorySize];
            _angulars = new Vector3[Sync.HistorySize];
            _stops = new bool[Sync.HistorySize];
            int count = 0;
            foreach (var state in states)
            {
                count = Sync.PushHistory(_times, _positions, _rotations, _velocities, _angulars, _stops, count,
                    state.time, new Vector3(0f, 0f, state.z), Quaternion.identity, new Vector3(0f, 0f, 1f), Vector3.zero, state.stop);
            }

            return count;
        }

        [Test]
        public void 表示する位置は前後の状態の補間で最新より後は先読みし停止の印があればぴったり置く()
        {
            int count = History((1f, 0f, false), (2f, 1f, false));
            Assert.That(Sync.SamplePosition(_times, _positions, _velocities, _stops, count, 1.5f, 0.25f).z, Is.EqualTo(0.5f).Within(1e-4f), "間");
            Assert.That(Sync.SamplePosition(_times, _positions, _velocities, _stops, count, 0.5f, 0.25f).z, Is.EqualTo(0f).Within(1e-5f), "最初より前は最初の状態");
            Assert.That(Sync.SamplePosition(_times, _positions, _velocities, _stops, count, 2.1f, 0.25f).z, Is.EqualTo(1.1f).Within(1e-4f), "先読み");
            Assert.That(Sync.SamplePosition(_times, _positions, _velocities, _stops, count, 5f, 0.25f).z, Is.EqualTo(1.25f).Within(1e-4f), "先読みの上限");

            count = History((1f, 0f, false), (2f, 1f, true));
            Assert.That(Sync.SamplePosition(_times, _positions, _velocities, _stops, count, 2.1f, 0.25f).z, Is.EqualTo(1f).Within(1e-5f), "停止の印");
            Assert.That(Quaternion.Angle(Sync.SampleRotation(_times, _rotations, _angulars, _stops, count, 2.1f, 0.25f), Quaternion.identity), Is.LessThan(0.01f));
        }

        [Test]
        public void 履歴は8個を超えると古いものから捨てる()
        {
            var states = new (float, float, bool)[10];
            for (int index = 0; index < states.Length; index++)
            {
                states[index] = (index, index, false);
            }

            int count = History(states);
            Assert.That(count, Is.EqualTo(Sync.HistorySize));
            Assert.That(_times[0], Is.EqualTo(2f), "古い 2 つを捨てた");
            Assert.That(_times[Sync.HistorySize - 1], Is.EqualTo(9f));
            Assert.That(_positions[Sync.HistorySize - 1].z, Is.EqualTo(9f), "並んだ配列も同じだけずれる");
        }

        [Test]
        public void 世代が変わると履歴を捨てる()
        {
            Assert.That(Sync.KeepHistory(3, 3, 5), Is.EqualTo(5));
            Assert.That(Sync.KeepHistory(4, 3, 5), Is.EqualTo(0));
        }

        [Test]
        public void 代わりの表示時刻は新しい状態が届かなくても今の時刻とともに進む()
        {
            // ClientSim のように SimulationTime の時間軸が違う（遅れが範囲の外）と、今の時刻から送る間隔の 2 倍を引く。
            Assert.That(Sync.DisplayTime(0.5f, 10f, Interval), Is.EqualTo(9.8f).Within(1e-5f));
            Assert.That(Sync.DisplayTime(0.5f, 11f, Interval), Is.EqualTo(10.8f).Within(1e-5f), "今の時刻とともに進む");
            Assert.That(Sync.DisplayTime(10.5f, 10f, Interval), Is.EqualTo(9.8f).Within(1e-5f), "未来の SimulationTime も使わない");
            Assert.That(Sync.DisplayTime(9.7f, 10f, Interval), Is.EqualTo(9.7f).Within(1e-5f), "遅れが範囲の内なら SimulationTime");
        }

        [Test]
        public void 代わりの表示時刻で最後の停止の状態まで再生し届かないまま上限を超えると先読みを止める()
        {
            int count = History((10f, 0f, false), (10.1f, 0.1f, true));
            float display = Sync.DisplayTime(0f, 10.4f, Interval);
            Assert.That(display, Is.GreaterThan(10.1f), "最後の停止の状態の時刻に届く");
            Assert.That(Sync.SamplePosition(_times, _positions, _velocities, _stops, count, display, 0.25f).z, Is.EqualTo(0.1f).Within(1e-5f));

            count = History((10f, 0f, false), (10.1f, 0.1f, false));
            float late = Sync.DisplayTime(0f, 20f, Interval);
            Assert.That(Sync.SamplePosition(_times, _positions, _velocities, _stops, count, late, 0.25f).z, Is.EqualTo(0.35f).Within(1e-4f), "先読みの上限で止まる");
        }

        [Test]
        public void 保留は送った人ごとに最新の1つで別の人の状態では消えずあふれたら一番前に受けたものを捨てる()
        {
            var senders = new int[Sync.HoldSize];
            var received = new float[Sync.HoldSize];
            var times = new float[Sync.HoldSize];
            int count = 0;

            int Store(int sender, float time, float now)
            {
                int index = Sync.StoreHold(senders, received, times, count, sender, time, now);
                if (index >= 0)
                {
                    senders[index] = sender;
                    received[index] = now;
                    times[index] = time;
                    count = Mathf.Max(count, index + 1);
                }

                return index;
            }

            int first = Store(Other, 5f, 100f);
            Assert.That(Store(Other, 4f, 100.1f), Is.EqualTo(-1), "同じ人の古い状態では置き換えない");
            Assert.That(Store(Other, 6f, 100.2f), Is.EqualTo(first), "同じ人の新しい状態で置き換える");
            Assert.That(Store(3, 7f, 100.3f), Is.Not.EqualTo(first), "別の人の状態は別に持つ");
            Store(4, 8f, 100.4f);
            Store(5, 9f, 100.5f);
            Assert.That(count, Is.EqualTo(Sync.HoldSize));
            Assert.That(Store(6, 10f, 100.6f), Is.EqualTo(first), "あふれたら一番前に受けた人（2 は 100.2 に受け直した後も一番前）");
        }

        [Test]
        public void 所有権が移ったら新しいオーナーの保留を選びほかの人の保留は残す()
        {
            // A → B → C と続けて移り、C の状態・B への通知・C への通知の順に届く。
            const int b = 2;
            const int c = 3;
            var senders = new[] { c, 0, 0, 0 };
            var received = new[] { 100f, 0f, 0f, 0f };
            var times = new[] { 50f, 0f, 0f, 0f };

            Assert.That(Sync.FindHold(senders, 1, b), Is.EqualTo(-1), "B への通知では C の状態を使わない");
            int index = Sync.FindHold(senders, 1, c);
            Assert.That(index, Is.EqualTo(0), "C の保留は残っている");
            Assert.That(Sync.HoldUsable(times[index], received[index], 100.5f, 40f), Is.True, "C への通知で使う");
        }

        [Test]
        public void 使った最新の状態より新しくない保留と期限を過ぎた保留は使わない()
        {
            Assert.That(Sync.HoldUsable(50f, 100f, 100.5f, 50f), Is.False, "使った状態と同じ時刻");
            Assert.That(Sync.HoldUsable(49f, 100f, 100.5f, 50f), Is.False, "使った状態より古い");
            Assert.That(Sync.HoldUsable(51f, 100f, 100f + Sync.HoldLifetime + 0.01f, 50f), Is.False, "受けてから期限を過ぎた");
            Assert.That(Sync.HoldUsable(51f, 100f, 100f + Sync.HoldLifetime, 50f), Is.True, "期限ちょうど");
        }

        [Test]
        public void 動いているとみなすのは速さか旋回の速さか運転の入力があるとき()
        {
            Assert.That(Sync.IsMoving(0.04f, 1.9f, false), Is.False);
            Assert.That(Sync.IsMoving(0.05f, 0f, false), Is.True, "速さ");
            Assert.That(Sync.IsMoving(0f, 2f, false), Is.True, "旋回の速さ");
            Assert.That(Sync.IsMoving(0f, 0f, true), Is.True, "入力");
        }

        [Test]
        public void 動いている間は送る間隔ごとに送る()
        {
            Assert.That(Sync.DecideSend(true, 0f, false, 0.05f, Interval, false, false, false), Is.EqualTo(Sync.SendNone));
            Assert.That(Sync.DecideSend(true, 0f, false, 0.1f, Interval, false, false, false), Is.EqualTo(Sync.SendMoving));
        }

        [Test]
        public void 止まって一定の時間で停止の印を1回送りその後は送らない()
        {
            Assert.That(Sync.DecideSend(false, 0.3f, false, 0.1f, Interval, false, false, false), Is.EqualTo(Sync.SendMoving), "止まりかけの間は間隔ごと");
            Assert.That(Sync.DecideSend(false, Sync.StopDelay, false, 0.05f, Interval, false, false, false), Is.EqualTo(Sync.SendStop));
            Assert.That(Sync.DecideSend(false, 5f, true, 3f, Interval, false, false, false), Is.EqualTo(Sync.SendNone), "停止を送った後");
        }

        [Test]
        public void 止まった状態から動き出したらすぐに送る()
        {
            Assert.That(Sync.DecideSend(true, 0f, true, 0.01f, Interval, false, false, false), Is.EqualTo(Sync.SendMoving));
        }

        [Test]
        public void 詰まっている間は送らず解けたら最新を1回送り停止の印を落とさない()
        {
            Assert.That(Sync.DecideSend(true, 0f, false, 1f, Interval, false, true, true), Is.EqualTo(Sync.SendNone), "詰まっている");
            Assert.That(Sync.DecideSend(true, 0f, false, 0.01f, Interval, false, false, true), Is.EqualTo(Sync.SendMoving), "解けたら間隔を待たない");
            Assert.That(Sync.DecideSend(false, 5f, true, 0.01f, Interval, false, false, true), Is.EqualTo(Sync.SendStop), "止まっていれば停止の印のまま");
        }

        [Test]
        public void デバッグUIの同期の文字はオーナーなら送った回数と要求した回数と接触と送った大きさを出しオーナー以外なら最新の状態の古さを出す()
        {
            Assert.That(YakimaTankDebugPanel.SyncSummary(true, 3, true, 3, 2, -1f, 9.96f, 10f, 0, 2, 1, 70, 45, 30),
                Is.EqualTo("OWNER\nOwner 3 (you)\nDriver 3\nEpoch 2\nSent/s 10.0  Req/s 10.0\nContact 2  Stale 1\nBytes 70/45/30\nRejected 0"));
            Assert.That(YakimaTankDebugPanel.SyncSummary(false, 3, false, Sync.NoPlayer, 2, 0.123f, 0f, 0f, 4, 2, 1, 70, 45, 30),
                Is.EqualTo("REMOTE\nOwner 3\nDriver -\nEpoch 2\nAge 0.12\nRejected 4"), "オーナー以外は接触を計算せず送らないので出さない");
            Assert.That(YakimaTankDebugPanel.SyncSummary(false, 3, false, Sync.NoPlayer, 0, -1f, 0f, 0f, 0, 0, 0, 0, 0, 0), Does.Contain("Age -"), "まだ状態を受けていない");
            Assert.That(YakimaTankDebugPanel.SyncSummary(true, 3, true, 3, 2, -1f, 0f, 0f, 0, 0, 0, 70, -1, -1), Does.Contain("\nBytes 70/-/-\n"), "武器や生存の状態の部品がない");
        }

        [Test]
        public void 表示用の速度はオーナー以外では最新の受けた状態の値を返す()
        {
            var velocity = new Vector3(1f, 0f, 3f);
            Assert.That(Sync.DisplayVelocity(true, velocity, false, 9.8f, 10f, 0.25f), Is.EqualTo(velocity), "表示が最新の状態より前（ふつう）でも最新の値");
            Assert.That(Sync.DisplayVelocity(true, velocity, false, 10.25f, 10f, 0.25f), Is.EqualTo(velocity), "先読みの上限ちょうど");
        }

        [Test]
        public void 表示用の速度は止まった印と先読みの上限を過ぎた後と状態がないときは0()
        {
            var velocity = new Vector3(1f, 0f, 3f);
            Assert.That(Sync.DisplayVelocity(true, velocity, true, 9.8f, 10f, 0.25f), Is.EqualTo(Vector3.zero), "止まった印");
            Assert.That(Sync.DisplayVelocity(true, velocity, false, 10.3f, 10f, 0.25f), Is.EqualTo(Vector3.zero), "表示も止まっている");
            Assert.That(Sync.DisplayVelocity(false, velocity, false, 9.8f, 10f, 0.25f), Is.EqualTo(Vector3.zero), "状態をまだ受けていない");
        }

        [Test]
        public void 止まった後に送った位置か向きから許容値以上ずれたら停止の印を付けて1回送る()
        {
            var position = new Vector3(1f, 0f, 1f);
            Quaternion rotation = Quaternion.Euler(0f, 10f, 0f);
            Assert.That(Sync.HasDrifted(position, rotation, position + new Vector3(0f, 0f, 0.049f), rotation), Is.False);
            // 境い目ちょうどは float の丸めでどちらにも転ぶので、少し越えた所で確かめる。
            Assert.That(Sync.HasDrifted(position, rotation, position + new Vector3(0f, 0f, Sync.DriftDistance + 1e-4f), rotation), Is.True, "位置");
            Assert.That(Sync.HasDrifted(position, rotation, position, Quaternion.Euler(0f, 10.9f, 0f)), Is.False);
            Assert.That(Sync.HasDrifted(position, rotation, position, Quaternion.Euler(0f, 10f + Sync.DriftAngle + 0.01f, 0f)), Is.True, "向き");

            Assert.That(Sync.DecideSend(false, 5f, true, 3f, Interval, true, false, false), Is.EqualTo(Sync.SendStop), "ずれたら停止の印で送る");
        }
    }
}
