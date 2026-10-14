using System.Collections.Generic;
using NUnit.Framework;
using TrashTD.Data;
using UnityEngine;

namespace TrashTD.Tests
{
    public class WaveCurveBuilderTests
    {
        private WaveCurveSettings settings;
        private StageData stage;
        private EnemyData grunt;
        private EnemyData rusher;
        private EnemyData tank;
        private EnemyData flyer;
        private EnemyData excluded;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            settings = Make<WaveCurveSettings>();
            stage = Make<StageData>();
            stage.spawnPoints = new[] { Vector2Int.zero, Vector2Int.one };

            grunt = Enemy("Grunt", EnemyArchetype.Grunt, 200);
            rusher = Enemy("Rusher", EnemyArchetype.Rusher, 150);
            tank = Enemy("Tank", EnemyArchetype.Tank, 1000);
            flyer = Enemy("Flyer", EnemyArchetype.Flyer, 190);
            flyer.movementType = EnemyMovementType.Air;
            excluded = Enemy("Broken", EnemyArchetype.Caster, 110);
            settings.excludedEnemies.Add(excluded);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        [TestCase(10)]
        [TestCase(20)]
        [TestCase(30)]
        public void BudgetCurve_RisesEveryRegularWave_SpikesEveryFifth_FinalIsHardest(int waveCount)
        {
            float[] budgets = WaveCurveBuilder.BuildBudgetCurve(waveCount, 1000f, settings);

            for (int n = 2; n <= waveCount; n++)
            {
                bool spike = WaveCurveBuilder.IsSpikeWave(n, waveCount, settings.spikeInterval);
                bool afterSpike = WaveCurveBuilder.IsSpikeWave(n - 1, waveCount, settings.spikeInterval);

                if (spike)
                    Assert.Greater(budgets[n - 1], budgets[n - 2], $"Spike wave {n} must be harder than wave {n - 1}.");
                else if (afterSpike)
                    Assert.GreaterOrEqual(budgets[n - 1], budgets[n - 2] * settings.postSpikeFloor - 0.01f,
                        $"Wave {n} drops too far after the spike.");
                else
                    Assert.Greater(budgets[n - 1], budgets[n - 2], $"Regular wave {n} must be harder than wave {n - 1}.");
            }

            float max = Mathf.Max(budgets);
            Assert.AreEqual(max, budgets[waveCount - 1], 0.001f, "The last wave must be the hardest.");
        }

        [Test]
        public void BudgetCurve_WaveAfterSpikeIsHarderThanTheWaveBeforeTheSpike()
        {
            float[] budgets = WaveCurveBuilder.BuildBudgetCurve(20, 1000f, settings);

            // Wave 6 (after the wave-5 spike) must beat wave 4, wave 11 must beat wave 9, ...
            for (int spike = 5; spike < 20; spike += 5)
                Assert.Greater(budgets[spike], budgets[spike - 2]);
        }

        [Test]
        public void OrderRoster_DropsExcludedEnemies_AndPutsCoreFirstHeavyLast()
        {
            List<EnemyData> ordered = WaveCurveBuilder.OrderRoster(new[] { tank, flyer, excluded, rusher, grunt }, settings);

            CollectionAssert.AreEqual(new[] { grunt, rusher, flyer, tank }, ordered);
        }

        [Test]
        public void Introductions_OneNewTypePerWave_HeavyDebutsOnFirstSpike()
        {
            var roster = WaveCurveBuilder.OrderRoster(new[] { grunt, rusher, flyer, tank }, settings);

            int[] intro = WaveCurveBuilder.BuildIntroductionWaves(roster, 10, settings.spikeInterval);

            Assert.AreEqual(1, intro[roster.IndexOf(grunt)]);
            Assert.AreEqual(5, intro[roster.IndexOf(tank)]);
            CollectionAssert.AllItemsAreUnique(intro);
            foreach (int wave in intro) Assert.That(wave, Is.InRange(1, 10));
        }

        [Test]
        public void BuildWaves_RealizedStrengthFollowsCurve_AndRespectsRoster()
        {
            var roster = WaveCurveBuilder.OrderRoster(new[] { grunt, rusher, flyer, tank, excluded }, settings);
            stage.wavesNormal = WaveCurveBuilder.BuildWaves(stage, StageDifficulty.Normal, 20, roster, settings);

            WaveAnalysis.WaveSummary[] summaries = WaveAnalysis.SummarizeAll(stage, StageDifficulty.Normal);
            Assert.AreEqual(20, summaries.Length);

            int hardest = 0;
            for (int i = 0; i < summaries.Length; i++)
            {
                Assert.IsFalse(summaries[i].HasWarnings, $"Wave {i + 1}: {string.Join("; ", summaries[i].Warnings)}");
                if (summaries[i].TotalHP > summaries[hardest].TotalHP) hardest = i;
                if (i == 0) continue;

                int n = i + 1;
                bool afterSpike = WaveCurveBuilder.IsSpikeWave(n - 1, 20, settings.spikeInterval) &&
                                  !WaveCurveBuilder.IsSpikeWave(n, 20, settings.spikeInterval);
                if (afterSpike)
                    Assert.GreaterOrEqual(summaries[i].TotalHP, summaries[i - 1].TotalHP * settings.postSpikeFloor,
                        $"Wave {n} collapses after the spike.");
                else
                    Assert.Greater(summaries[i].TotalHP, summaries[i - 1].TotalHP, $"Wave {n} is not harder than wave {n - 1}.");
            }
            Assert.AreEqual(19, hardest, "The final wave should have the most total HP.");

            foreach (WaveData wave in stage.wavesNormal)
            foreach (WaveEntry entry in wave.entries)
            {
                Assert.AreNotSame(excluded, entry.enemyData);
                Assert.That(entry.spawnPointIndex, Is.InRange(0, 1));
            }

            // Only the basic enemy on wave 1.
            foreach (WaveEntry entry in stage.wavesNormal[0].entries) Assert.AreSame(grunt, entry.enemyData);
        }

        private T Make<T>() where T : ScriptableObject
        {
            var obj = ScriptableObject.CreateInstance<T>();
            created.Add(obj);
            return obj;
        }

        private EnemyData Enemy(string name, EnemyArchetype archetype, int hp)
        {
            EnemyData enemy = Make<EnemyData>();
            enemy.enemyName = name;
            enemy.archetype = archetype;
            enemy.baseHP = hp;
            return enemy;
        }
    }
}
