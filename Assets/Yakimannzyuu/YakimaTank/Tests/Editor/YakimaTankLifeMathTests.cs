using NUnit.Framework;
using Life = Yakimannzyuu.YakimaTank.YakimaTankLife;

namespace Yakimannzyuu.YakimaTank.Editor.Tests
{
    /// <summary>HP・破壊・復活の計算（仕様書 0.6.0 の 1.1〜1.3）。</summary>
    public class YakimaTankLifeMathTests
    {
        private const int Shooter = 1;
        private const int Own = 0;
        private const int Count = 4;
        private const int Generation = 3;
        private const int Player = 7;

        private static int Classify(bool owner = true, bool sender = true, int shooter = Shooter, int generation = Generation, int health = 100,
            int number = 5, int lastPlayer = Life.NoPlayer, int lastNumber = -1)
        {
            return Life.ClassifyHit(owner, sender, shooter, Own, Count, generation, Generation, health, Player, number, lastPlayer, lastNumber);
        }

        [Test]
        public void すべて通る申告は使う()
        {
            Assert.That(Classify(), Is.EqualTo(Life.HitUse));
        }

        [Test]
        public void オーナーでなければ使わない()
        {
            Assert.That(Classify(owner: false), Is.EqualTo(Life.HitNotOwner));
        }

        [Test]
        public void 送った人が撃った車両の武器のオーナーでなければ使わない()
        {
            Assert.That(Classify(sender: false), Is.EqualTo(Life.HitWrongSender));
        }

        [Test]
        public void 撃った車両の番号が範囲の外か自分自身なら使わない()
        {
            Assert.That(Classify(shooter: -1), Is.EqualTo(Life.HitBadShooter));
            Assert.That(Classify(shooter: Count), Is.EqualTo(Life.HitBadShooter));
            Assert.That(Classify(shooter: Own), Is.EqualTo(Life.HitBadShooter), "自分自身");
        }

        [Test]
        public void 復活の前の世代に対する申告は使わない()
        {
            Assert.That(Classify(generation: Generation - 1), Is.EqualTo(Life.HitOldGeneration));
            Assert.That(Classify(generation: Generation + 1), Is.EqualTo(Life.HitOldGeneration), "まだ来ていない世代");
        }

        [Test]
        public void 破壊された後の申告は使わない()
        {
            Assert.That(Classify(health: 0), Is.EqualTo(Life.HitDestroyed));
        }

        [Test]
        public void 同じ人の処理した番号以下の申告は重複として使わず別の人の申告は使う()
        {
            Assert.That(Classify(number: 5, lastPlayer: Player, lastNumber: 5), Is.EqualTo(Life.HitDuplicate), "同じ命中");
            Assert.That(Classify(number: 4, lastPlayer: Player, lastNumber: 5), Is.EqualTo(Life.HitDuplicate), "前の命中");
            Assert.That(Classify(number: 6, lastPlayer: Player, lastNumber: 5), Is.EqualTo(Life.HitUse), "次の命中");
            Assert.That(Classify(number: 1, lastPlayer: Player + 1, lastNumber: 5), Is.EqualTo(Life.HitUse), "別の人（所有権が移った）は番号が小さくても使う");
        }

        [Test]
        public void 負の発射の番号は使わない()
        {
            Assert.That(Classify(number: -1), Is.EqualTo(Life.HitBadNumber));
        }

        [Test]
        public void ダメージはHPから引き0で止まり負のダメージは0にする()
        {
            Assert.That(Life.ApplyDamage(100, 25f), Is.EqualTo(75));
            Assert.That(Life.ApplyDamage(20, 25f), Is.EqualTo(0), "0 で止まる");
            Assert.That(Life.ApplyDamage(100, 24.6f), Is.EqualTo(75), "四捨五入");
            Assert.That(Life.ApplyDamage(100, -5f), Is.EqualTo(100), "回復しない");
        }

        [Test]
        public void 最大HPは設定を四捨五入して1以上()
        {
            Assert.That(Life.MaxHealthPoints(100f), Is.EqualTo(100));
            Assert.That(Life.MaxHealthPoints(99.6f), Is.EqualTo(100));
            Assert.That(Life.MaxHealthPoints(0f), Is.EqualTo(1));
        }

        [Test]
        public void 復活は待ち時間がたつと行う()
        {
            Assert.That(Life.IsRespawnDue(4.9f, 5f), Is.False);
            Assert.That(Life.IsRespawnDue(5f, 5f), Is.True);
        }

        [Test]
        public void HPのバーの長さは最大に対する割合で0から1()
        {
            Assert.That(Life.FillRatio(75, 100), Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(Life.FillRatio(0, 100), Is.EqualTo(0f), "破壊");
            Assert.That(Life.FillRatio(150, 100), Is.EqualTo(1f), "Play 中に最大を下げた後");
            Assert.That(Life.FillRatio(50, 0), Is.EqualTo(0f));
        }

        [Test]
        public void 受けた生存の状態は形式とHPの範囲と世代と配列の長さと時刻を確かめる()
        {
            Assert.That(Life.IsValidState(Life.SyncFormat, 50, 100, 2, 4, 4, 4, 10f, 9f), Is.True);
            Assert.That(Life.IsValidState(2, 50, 100, 2, 4, 4, 4, 10f, 9f), Is.False, "形式");
            Assert.That(Life.IsValidState(Life.SyncFormat, -1, 100, 2, 4, 4, 4, 10f, 9f), Is.False, "負の HP");
            Assert.That(Life.IsValidState(Life.SyncFormat, 0, 100, 2, 4, 4, 4, 10f, 9f), Is.True, "破壊");
            Assert.That(Life.IsValidState(Life.SyncFormat, 101, 100, 2, 4, 4, 4, 10f, 9f), Is.False, "最大より大きい HP");
            Assert.That(Life.IsValidState(Life.SyncFormat, 50, 100, -1, 4, 4, 4, 10f, 9f), Is.False, "負の世代");
            Assert.That(Life.IsValidState(Life.SyncFormat, 50, 100, 2, 4, 3, 4, 10f, 9f), Is.False, "2 つの配列の長さが違う");
            Assert.That(Life.IsValidState(Life.SyncFormat, 50, 100, 2, 3, 3, 4, 10f, 9f), Is.False, "シーンの車両の数と違う");
            Assert.That(Life.IsValidState(Life.SyncFormat, 50, 100, 2, 3, 3, Life.UnknownCount, 10f, 9f), Is.True, "一覧をまだ知らない");
            Assert.That(Life.IsValidState(Life.SyncFormat, 50, 100, 2, -1, -1, Life.UnknownCount, 10f, 9f), Is.False, "配列がない");
            Assert.That(Life.IsValidState(Life.SyncFormat, 50, 100, 2, 4, 4, 4, 9f, 9f), Is.False, "古い時刻");
            Assert.That(Life.IsValidState(Life.SyncFormat, 50, 100, 2, 4, 4, 4, float.NaN, 9f), Is.False, "時刻の NaN");
        }
    }
}
