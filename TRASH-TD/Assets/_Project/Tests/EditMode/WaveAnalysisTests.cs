using NUnit.Framework;
using TrashTD.Data;
using UnityEngine;

namespace TrashTD.Tests
{
    public class WaveAnalysisTests
    {
        private StageData stage;
        private EnemyData grunt;
        private EnemyData flyer;

        [SetUp]
        public void SetUp()
        {
            stage = ScriptableObject.CreateInstance<StageData>();
            stage.spawnPoints = new[] { Vector2Int.zero, Vector2Int.one };

            grunt = ScriptableObject.CreateInstance<EnemyData>();
            grunt.enemyName = "Grunt";
            grunt.baseHP = 100;
            grunt.lifePointCost = 1;

            flyer = ScriptableObject.CreateInstance<EnemyData>();
            flyer.enemyName = "Flyer";
            flyer.baseHP = 40;
            flyer.lifePointCost = 2;
            flyer.movementType = EnemyMovementType.Air;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(stage);
            Object.DestroyImmediate(grunt);
            Object.DestroyImmediate(flyer);
        }

        [Test]
        public void Summarize_TotalsUseBaseHP_OnEveryDifficulty()
        {
            var wave = Wave(
                new WaveEntry { enemyData = grunt, count = 3, spawnInterval = 1f },
                new WaveEntry { enemyData = flyer, count = 2, spawnInterval = 1f, spawnPointIndex = 1 });

            WaveAnalysis.WaveSummary summary = WaveAnalysis.Summarize(wave, stage, StageDifficulty.Hard);

            Assert.AreEqual(5, summary.EnemyCount);
            Assert.AreEqual(2, summary.AirCount);
            Assert.AreEqual(grunt.baseHP * 3 + flyer.baseHP * 2, summary.TotalHP, "Enemy stats don't scale with difficulty.");
            Assert.AreEqual(3 * 1 + 2 * 2, summary.MaxLeakCost);
            Assert.AreEqual(0b11, summary.SpawnPointMask);
            Assert.IsFalse(summary.HasWarnings);
        }

        [Test]
        public void Summarize_SpawnDurationIsLatestEntryFinish()
        {
            var wave = Wave(
                new WaveEntry { enemyData = grunt, count = 4, spawnInterval = 2f, startDelay = 0f },
                new WaveEntry { enemyData = grunt, count = 2, spawnInterval = 1.5f, startDelay = 5f });

            WaveAnalysis.WaveSummary summary = WaveAnalysis.Summarize(wave, stage, StageDifficulty.Easy);

            // Entry 1 ends at 0 + 3 * 2 = 6s, entry 2 at 5 + 1 * 1.5 = 6.5s.
            Assert.AreEqual(6.5f, summary.SpawnDuration, 0.0001f);
        }

        [Test]
        public void Summarize_MissingSpawnPointIsFlaggedAndExcludedFromTotals()
        {
            var wave = Wave(
                new WaveEntry { enemyData = grunt, count = 2 },
                new WaveEntry { enemyData = grunt, count = 5, spawnPointIndex = 2 });

            WaveAnalysis.WaveSummary summary = WaveAnalysis.Summarize(wave, stage, StageDifficulty.Easy);

            Assert.AreEqual(2, summary.EnemyCount);
            Assert.AreEqual(1, summary.Warnings.Count);
            StringAssert.Contains("spawn point 2 does not exist", summary.Warnings[0]);
        }

        [Test]
        public void Summarize_FlagsMissingEnemyAndEmptyWave()
        {
            WaveAnalysis.WaveSummary missingEnemy = WaveAnalysis.Summarize(
                Wave(new WaveEntry { enemyData = null, count = 3 }), stage, StageDifficulty.Easy);
            WaveAnalysis.WaveSummary empty = WaveAnalysis.Summarize(Wave(), stage, StageDifficulty.Easy);

            Assert.AreEqual(0, missingEnemy.EnemyCount);
            Assert.IsTrue(missingEnemy.HasWarnings);
            Assert.IsTrue(empty.HasWarnings);
        }

        [Test]
        public void SummarizeAll_FlagsDifficultySpike()
        {
            stage.wavesEasy = new[]
            {
                Wave(new WaveEntry { enemyData = grunt, count = 2 }),
                Wave(new WaveEntry { enemyData = grunt, count = 3 }),
                Wave(new WaveEntry { enemyData = grunt, count = 10 })
            };

            WaveAnalysis.WaveSummary[] summaries = WaveAnalysis.SummarizeAll(stage, StageDifficulty.Easy);

            Assert.IsFalse(summaries[1].HasWarnings, "1.5x growth is under the spike threshold.");
            Assert.IsTrue(summaries[2].HasWarnings, "3.3x growth should be flagged.");
            StringAssert.Contains("spike", summaries[2].Warnings[0]);
        }

        [Test]
        public void CloneAll_CreatesIndependentCopies()
        {
            var original = new[] { Wave(new WaveEntry { enemyData = grunt, count = 2 }) };

            WaveData[] copy = WaveAnalysis.CloneAll(original);
            copy[0].entries[0].count = 9;
            copy[0].waveName = "Changed";

            Assert.AreEqual(2, original[0].entries[0].count);
            Assert.AreEqual("Wave", original[0].waveName);
            Assert.AreSame(grunt, copy[0].entries[0].enemyData);
        }

        private static WaveData Wave(params WaveEntry[] entries)
        {
            return new WaveData { waveName = "Wave", entries = entries };
        }
    }
}
