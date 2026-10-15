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

            grunt = Enemy("Grunt", EnemyArchetype.Grunt, 200, def: 10, speed: 0.75f);
            rusher = Enemy("Rusher", EnemyArchetype.Rusher, 150, def: 5, speed: 1.8f);
            tank = Enemy("Tank", EnemyArchetype.Tank, 1000, def: 35, speed: 0.6f);
            flyer = Enemy("Flyer", EnemyArchetype.Flyer, 190, def: 15, speed: 1.6f);
            flyer.movementType = EnemyMovementType.Air;
            excluded = Enemy("Broken", EnemyArchetype.Caster, 110, def: 10, speed: 0.85f);
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
        public void BudgetCurve_EveryWaveIsHarder_FinalIsHardest(int waveCount)
        {
            float[] budgets = WaveCurveBuilder.BuildBudgetCurve(waveCount, 1000f, settings);

            for (int n = 2; n <= waveCount; n++)
                Assert.Greater(budgets[n - 1], budgets[n - 2], $"Wave {n} must be harder than wave {n - 1}.");

            Assert.AreEqual(Mathf.Max(budgets), budgets[waveCount - 1], 0.001f, "The last wave must be the hardest.");
        }

        [Test]
        public void Threat_GrowsWithArmorSpeedAndFlight()
        {
            float gruntThreat = WaveCurveBuilder.GetThreat(grunt, settings);

            Assert.Greater(gruntThreat, grunt.baseHP, "DEF makes an enemy tougher than its HP alone.");
            Assert.Greater(WaveCurveBuilder.GetThreat(rusher, settings) / rusher.baseHP, gruntThreat / grunt.baseHP,
                "A fast enemy spends less time in range, so each HP counts for more.");
            Assert.Greater(WaveCurveBuilder.GetThreat(flyer, settings) / flyer.baseHP, gruntThreat / grunt.baseHP * settings.airThreatMultiplier,
                "Air enemies can only be hit by part of the squad.");

            EnemyData plated = Enemy("Plated", EnemyArchetype.Grunt, 200, def: 500, speed: 0.75f);
            plated.baseRES = 500;
            Assert.AreEqual(200f * settings.maxArmorThreat, WaveCurveBuilder.GetThreat(plated, settings), 0.01f,
                "The armor factor is capped.");
        }

        [Test]
        public void OrderRoster_DropsExcludedEnemies_AndPutsCoreFirstHeavyLast()
        {
            List<EnemyData> ordered = WaveCurveBuilder.OrderRoster(new[] { tank, flyer, excluded, rusher, grunt }, settings);

            CollectionAssert.AreEqual(new[] { grunt, rusher, flyer, tank }, ordered);
        }

        [Test]
        public void Introductions_SpacedOut_AvoidSpikes_HeavyDebutsOnFirstSpikeAfterSquadIsFull()
        {
            var roster = WaveCurveBuilder.OrderRoster(new[] { grunt, rusher, flyer, tank }, settings);

            int[] intro = WaveCurveBuilder.BuildIntroductionWaves(roster, settings);

            Assert.AreEqual(1, intro[roster.IndexOf(grunt)]);
            Assert.AreEqual(1 + settings.introductionSpacing, intro[roster.IndexOf(rusher)]);
            Assert.AreEqual(settings.firstHeavyWave, intro[roster.IndexOf(tank)]);
            CollectionAssert.AllItemsAreUnique(intro);
            foreach (EnemyData enemy in new[] { grunt, rusher, flyer })
                Assert.AreNotEqual(0, intro[roster.IndexOf(enemy)] % settings.spikeInterval, $"{enemy.enemyName} debuts on a spike wave.");
        }

        [Test]
        public void StageBonus_AddsItsEnemiesOnWaveOne_SplitAcrossSpawnPoints_AndFadesLater()
        {
            var roster = WaveCurveBuilder.OrderRoster(new[] { grunt, rusher, flyer, tank }, settings);
            WaveData[] firstStage = WaveCurveBuilder.BuildWaves(stage, 30, roster, settings);

            StageData laterStage = Make<StageData>();
            laterStage.spawnPoints = stage.spawnPoints;
            laterStage.extraStartEnemies = 1;
            WaveData[] secondStage = WaveCurveBuilder.BuildWaves(laterStage, 30, roster, settings);

            // Wave 1: 3 basic enemies on the first stage, 4 on the next, split evenly over both spawn points.
            Assert.AreEqual(2, secondStage[0].entries.Length);
            foreach (WaveEntry entry in secondStage[0].entries) Assert.AreEqual(2, entry.count);

            for (int i = 0; i < 30; i++)
            {
                Assert.Greater(WaveCurveBuilder.GetWaveThreat(secondStage[i], settings),
                    WaveCurveBuilder.GetWaveThreat(firstStage[i], settings), $"Wave {i + 1} of the later stage must be harder.");
            }

            Assert.Less(WaveCurveBuilder.StageFactor(laterStage, 30, settings), WaveCurveBuilder.StageFactor(laterStage, 1, settings),
                "The stage bonus fades so final waves stay beatable.");
        }

        [Test]
        public void BuildWaves_EveryWaveIsHarder_FirstWaveFitsOneOperator()
        {
            var roster = WaveCurveBuilder.OrderRoster(new[] { grunt, rusher, flyer, tank, excluded }, settings);
            WaveData[] waves = WaveCurveBuilder.BuildWaves(stage, 30, roster, settings);
            stage.wavesHard = waves;

            WaveAnalysis.WaveSummary[] summaries = WaveAnalysis.SummarizeAll(stage, StageDifficulty.Hard);
            Assert.AreEqual(30, summaries.Length);

            float hardest = 0f;
            for (int i = 0; i < waves.Length; i++)
            {
                Assert.IsFalse(summaries[i].HasWarnings, $"Wave {i + 1}: {string.Join("; ", summaries[i].Warnings)}");

                float threat = WaveCurveBuilder.GetWaveThreat(waves[i], settings);
                if (i > 0)
                    Assert.Greater(threat, WaveCurveBuilder.GetWaveThreat(waves[i - 1], settings), $"Wave {i + 1} is not harder than wave {i}.");
                hardest = Mathf.Max(hardest, threat);

                foreach (WaveEntry entry in waves[i].entries)
                {
                    Assert.AreNotSame(excluded, entry.enemyData);
                    Assert.That(entry.spawnPointIndex, Is.InRange(0, 1));
                }
            }
            Assert.AreEqual(hardest, WaveCurveBuilder.GetWaveThreat(waves[29], settings), 0.01f, "The final wave should be the hardest.");

            // Wave 1: a few basic enemies, spaced for a single operator.
            int firstWaveCount = 0;
            foreach (WaveEntry entry in waves[0].entries)
            {
                Assert.AreSame(grunt, entry.enemyData);
                Assert.AreEqual(settings.coreIntervalStart, entry.spawnInterval, 0.001f);
                firstWaveCount += entry.count;
            }
            Assert.AreEqual(Mathf.RoundToInt(settings.startEnemies), firstWaveCount);
        }

        [Test]
        public void Difficulties_ShareTheirWaves_AndDifferOnlyInLength()
        {
            var roster = WaveCurveBuilder.OrderRoster(new[] { grunt, rusher, flyer, tank }, settings);
            WaveData[] easy = WaveCurveBuilder.BuildWaves(stage, 10, roster, settings);
            WaveData[] hard = WaveCurveBuilder.BuildWaves(stage, 30, roster, settings);

            for (int i = 0; i < 9; i++)
            {
                Assert.AreEqual(hard[i].entries.Length, easy[i].entries.Length, $"Wave {i + 1} differs.");
                for (int e = 0; e < easy[i].entries.Length; e++)
                {
                    Assert.AreSame(hard[i].entries[e].enemyData, easy[i].entries[e].enemyData, $"Wave {i + 1} differs.");
                    Assert.AreEqual(hard[i].entries[e].count, easy[i].entries[e].count, $"Wave {i + 1} differs.");
                }
            }

            Assert.Greater(WaveCurveBuilder.GetWaveThreat(easy[9], settings), WaveCurveBuilder.GetWaveThreat(hard[9], settings),
                "Easy's final wave gets the finale boost that Hard's wave 10 doesn't.");
        }

        private T Make<T>() where T : ScriptableObject
        {
            var obj = ScriptableObject.CreateInstance<T>();
            created.Add(obj);
            return obj;
        }

        private EnemyData Enemy(string name, EnemyArchetype archetype, int hp, int def, float speed)
        {
            EnemyData enemy = Make<EnemyData>();
            enemy.enemyName = name;
            enemy.archetype = archetype;
            enemy.baseHP = hp;
            enemy.baseDEF = def;
            enemy.moveSpeed = speed;
            return enemy;
        }
    }
}
