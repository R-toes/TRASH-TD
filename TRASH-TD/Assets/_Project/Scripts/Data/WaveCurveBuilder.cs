using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>
    /// Builds a stage's waves from one shared difficulty curve (see WaveCurveSettings):
    /// each wave gets a strength budget (total enemy HP, difficulty-scaled like WaveManager),
    /// and the budget is filled with the stage's roster. New enemy types are introduced one at a
    /// time with a small first count; heavy enemies debut on spike waves. The result is a draft
    /// to hand-tune in the Wave Planner.
    /// </summary>
    public static class WaveCurveBuilder
    {
        private const int NewcomerCoreCount = 2;
        private const float EmphasizedCoreWeight = 0.6f;

        public enum Role { Core, Air, Heavy }

        public static Role GetRole(EnemyData enemy)
        {
            if (enemy.movementType == EnemyMovementType.Air || enemy.archetype == EnemyArchetype.Flyer) return Role.Air;
            return enemy.archetype == EnemyArchetype.Tank ? Role.Heavy : Role.Core;
        }

        /// <summary>1-based wave number. The last wave is always a spike.</summary>
        public static bool IsSpikeWave(int waveNumber, int waveCount, int spikeInterval)
        {
            return waveNumber == waveCount || (spikeInterval > 0 && waveNumber % spikeInterval == 0);
        }

        /// <summary>
        /// Nominal strength budget per wave (index 0 = wave 1), in difficulty-scaled HP, before enemy
        /// counts are rounded. BuildWaves follows the same rules using each wave's actual strength.
        /// </summary>
        public static float[] BuildBudgetCurve(int waveCount, float baseBudget, WaveCurveSettings settings)
        {
            var budgets = new float[Mathf.Max(0, waveCount)];
            float curveLevel = baseBudget;
            for (int n = 1; n <= waveCount; n++)
            {
                if (n > 1) curveLevel = NextCurveLevel(curveLevel, n, waveCount, budgets[n - 2], settings);
                budgets[n - 1] = curveLevel * SpikeFactor(n, waveCount, settings);
            }

            return budgets;
        }

        /// <summary>All distinct enemies currently used in any of the stage's waves.</summary>
        public static List<EnemyData> CollectRoster(StageData stage)
        {
            var roster = new List<EnemyData>();
            if (stage == null) return roster;

            foreach (WaveData[] waves in new[] { stage.wavesEasy, stage.wavesNormal, stage.wavesHard })
            {
                if (waves == null) continue;
                foreach (WaveData wave in waves)
                {
                    if (wave?.entries == null) continue;
                    foreach (WaveEntry entry in wave.entries)
                        if (entry?.enemyData != null && !roster.Contains(entry.enemyData)) roster.Add(entry.enemyData);
                }
            }

            return roster;
        }

        /// <summary>
        /// Introduction order: core enemies (Grunt, Rusher, ...) then air enemies, weakest first;
        /// heavy enemies are listed last and debut on spike waves. Excluded enemies are dropped.
        /// </summary>
        public static List<EnemyData> OrderRoster(IEnumerable<EnemyData> enemies, WaveCurveSettings settings)
        {
            var ordered = new List<EnemyData>();
            foreach (EnemyData enemy in enemies)
            {
                if (enemy == null || ordered.Contains(enemy)) continue;
                if (settings.excludedEnemies != null && settings.excludedEnemies.Contains(enemy)) continue;
                ordered.Add(enemy);
            }

            ordered.Sort((a, b) =>
            {
                int byRole = RoleOrder(GetRole(a)).CompareTo(RoleOrder(GetRole(b)));
                if (byRole != 0) return byRole;
                int byArchetype = a.archetype.CompareTo(b.archetype);
                if (byArchetype != 0) return byArchetype;
                return GetThreat(a).CompareTo(GetThreat(b));
            });
            return ordered;
        }

        private static int RoleOrder(Role role) => role == Role.Core ? 0 : role == Role.Air ? 1 : 2;

        private static float GetThreat(EnemyData enemy) => enemy.baseHP * (1f + (enemy.baseDEF + enemy.baseRES) / 100f);

        /// <summary>
        /// Wave (1-based) on which each roster enemy first appears. At most one new type per wave
        /// where the wave count allows; non-heavy types avoid spike waves, heavy types debut on them.
        /// </summary>
        public static int[] BuildIntroductionWaves(IReadOnlyList<EnemyData> orderedRoster, int waveCount, int spikeInterval)
        {
            var intro = new int[orderedRoster.Count];
            var used = new HashSet<int>();

            var spikes = new List<int>();
            for (int n = 1; n <= waveCount; n++)
                if (IsSpikeWave(n, waveCount, spikeInterval)) spikes.Add(n);

            var nonHeavy = new List<int>();
            int heavyIndex = 0;
            for (int i = 0; i < orderedRoster.Count; i++)
            {
                if (GetRole(orderedRoster[i]) == Role.Heavy)
                {
                    intro[i] = spikes.Count > 0 ? spikes[Mathf.Min(heavyIndex, spikes.Count - 1)] : waveCount;
                    used.Add(intro[i]);
                    heavyIndex++;
                }
                else
                {
                    nonHeavy.Add(i);
                }
            }

            if (nonHeavy.Count == 0) return intro;

            int spacing = nonHeavy.Count > 1
                ? Mathf.Clamp(Mathf.RoundToInt(waveCount * 0.5f / (nonHeavy.Count - 1)), 1, 3)
                : 1;

            int previous = 1;
            intro[nonHeavy[0]] = 1;
            used.Add(1);
            for (int k = 1; k < nonHeavy.Count; k++)
            {
                int candidate = previous + spacing;
                while (candidate <= waveCount && (used.Contains(candidate) || IsSpikeWave(candidate, waveCount, spikeInterval)))
                    candidate++;

                if (candidate > waveCount)
                {
                    candidate = -1;
                    for (int n = waveCount; n > previous; n--)
                    {
                        if (!used.Contains(n) && !IsSpikeWave(n, waveCount, spikeInterval)) { candidate = n; break; }
                    }
                    if (candidate < 0) candidate = Mathf.Min(waveCount, previous + 1);
                }

                intro[nonHeavy[k]] = candidate;
                used.Add(candidate);
                previous = candidate;
            }

            return intro;
        }

        public static WaveData[] BuildWaves(StageData stage, StageDifficulty difficulty, int waveCount,
            IReadOnlyList<EnemyData> orderedRoster, WaveCurveSettings settings)
        {
            if (stage == null || settings == null || orderedRoster == null || orderedRoster.Count == 0 || waveCount <= 0)
                return new WaveData[0];

            int level = WaveAnalysis.GetDifficultyLevel(difficulty);
            int lanes = Mathf.Max(1, stage.spawnPoints != null ? stage.spawnPoints.Length : 1);

            EnemyData unit = FirstWithRole(orderedRoster, Role.Core) ?? orderedRoster[0];
            float baseBudget = settings.GetStartEnemies(difficulty) * unit.GetScaledHP(level) *
                               (1f + settings.extraBudgetPerLane * (lanes - 1));

            int[] intro = BuildIntroductionWaves(orderedRoster, waveCount, settings.spikeInterval);

            var waves = new WaveData[waveCount];
            float curveLevel = baseBudget;
            float previousHP = 0f;
            for (int n = 1; n <= waveCount; n++)
            {
                // The curve advances from what the previous wave actually contains, so rounding up a
                // wave never leaves the following waves lagging behind it.
                if (n > 1) curveLevel = NextCurveLevel(curveLevel, n, waveCount, previousHP, settings);
                float budget = curveLevel * SpikeFactor(n, waveCount, settings);

                // Rounding enemy counts (and a single big newcomer eating a spike's budget) can undo
                // the curve, so each wave gets a minimum strength relative to the previous wave.
                bool isSpike = IsSpikeWave(n, waveCount, settings.spikeInterval);
                float minimumHP = n == 1 ? 0f
                    : isSpike ? previousHP * settings.spikeMultiplier
                    : IsSpikeWave(n - 1, waveCount, settings.spikeInterval) ? previousHP * settings.postSpikeFloor
                    : previousHP + 1f;

                waves[n - 1] = BuildWave(n, waveCount, budget, minimumHP, level, lanes, orderedRoster, intro,
                    settings, out previousHP);
            }
            return waves;
        }

        /// <summary>
        /// Regular-wave strength level for wave n, given the previous wave's level and actual strength:
        /// it grows by the per-wave rate, and right after a spike it resumes near the spike.
        /// </summary>
        private static float NextCurveLevel(float previousLevel, int n, int waveCount, float previousStrength,
            WaveCurveSettings settings)
        {
            float growth = n <= settings.earlyGrowthWaves ? settings.earlyGrowthPerWave : settings.growthPerWave;
            float floor = IsSpikeWave(n - 1, waveCount, settings.spikeInterval)
                ? previousStrength * settings.postSpikeFloor
                : previousStrength * growth;
            return Mathf.Max(previousLevel * growth, floor);
        }

        private static float SpikeFactor(int n, int waveCount, WaveCurveSettings settings)
        {
            if (!IsSpikeWave(n, waveCount, settings.spikeInterval)) return 1f;
            return settings.spikeMultiplier * (n == waveCount ? settings.finalWaveMultiplier : 1f);
        }

        private static WaveData BuildWave(int n, int waveCount, float budget, float minimumHP, int level, int lanes,
            IReadOnlyList<EnemyData> roster, int[] intro, WaveCurveSettings settings, out float realizedHP)
        {
            bool isSpike = IsSpikeWave(n, waveCount, settings.spikeInterval);
            var counts = new int[roster.Count];
            float spent = 0f;

            var establishedCore = new List<int>();
            var establishedAir = new List<int>();
            var establishedHeavy = new List<int>();
            var newcomers = new List<string>();

            for (int i = 0; i < roster.Count; i++)
            {
                if (intro[i] > n) continue;
                Role role = GetRole(roster[i]);
                bool isNew = intro[i] == n;

                // A core newcomer with no other core enemy available simply fills the wave (wave 1).
                bool onlyCore = role == Role.Core && CountAvailable(roster, intro, n, Role.Core) == 1;
                if (isNew && !onlyCore)
                {
                    counts[i] = role == Role.Core ? NewcomerCoreCount : 1;
                    spent += counts[i] * HP(roster[i], level);
                    newcomers.Add(roster[i].enemyName);
                    continue;
                }
                if (isNew) newcomers.Add(roster[i].enemyName);

                if (role == Role.Core) establishedCore.Add(i);
                else if (role == Role.Air) establishedAir.Add(i);
                else establishedHeavy.Add(i);
            }

            // Heavy: a big share on spike waves, a small share in the late stage.
            float heavyShare = isSpike ? settings.spikeHeavyShare
                : (n > waveCount * settings.lateStageStart ? settings.lateHeavyShare : 0f);
            if (heavyShare > 0f && establishedHeavy.Count > 0)
            {
                float perType = budget * heavyShare / establishedHeavy.Count;
                foreach (int i in establishedHeavy)
                {
                    int count = Mathf.RoundToInt(perType / HP(roster[i], level));
                    if (isSpike) count = Mathf.Max(1, count);
                    counts[i] += count;
                    spent += count * HP(roster[i], level);
                }
            }

            // Air: a fixed share once introduced, at least one of each type.
            if (establishedAir.Count > 0)
            {
                float perType = budget * settings.airShare / establishedAir.Count;
                foreach (int i in establishedAir)
                {
                    int count = Mathf.Max(1, Mathf.RoundToInt(perType / HP(roster[i], level)));
                    counts[i] += count;
                    spent += count * HP(roster[i], level);
                }
            }

            // Core fills the rest, emphasizing a different core type each wave for variety.
            float remaining = Mathf.Max(0f, budget - spent);
            int coreSpawned = 0;
            for (int k = 0; k < establishedCore.Count; k++)
            {
                int i = establishedCore[k];
                float weight = establishedCore.Count == 1 ? 1f
                    : (k == (n - 1) % establishedCore.Count
                        ? EmphasizedCoreWeight
                        : (1f - EmphasizedCoreWeight) / (establishedCore.Count - 1));
                int count = Mathf.RoundToInt(remaining * weight / HP(roster[i], level));
                counts[i] += count;
                coreSpawned += count;
            }
            if (coreSpawned == 0 && establishedCore.Count > 0 && remaining > 0f)
                counts[establishedCore[0]] += 1;

            realizedHP = 0f;
            for (int i = 0; i < roster.Count; i++) realizedHP += counts[i] * HP(roster[i], level);

            // Top up with the cheapest basic enemy until the wave meets its minimum strength.
            int filler = CheapestOf(establishedCore, roster, level);
            if (filler < 0) filler = CheapestOf(establishedAir, roster, level);
            while (filler >= 0 && realizedHP < minimumHP)
            {
                counts[filler]++;
                realizedHP += HP(roster[filler], level);
            }

            return new WaveData
            {
                waveName = n == waveCount ? "Final Wave"
                    : isSpike ? $"Wave {n}: Spike"
                    : newcomers.Count > 0 && n > 1 ? $"Wave {n}: New {string.Join(", ", newcomers)}"
                    : $"Wave {n}",
                preWaveDelay = settings.preWaveDelay,
                entries = BuildEntries(n, waveCount, lanes, roster, counts, settings)
            };
        }

        private static WaveEntry[] BuildEntries(int n, int waveCount, int lanes, IReadOnlyList<EnemyData> roster,
            int[] counts, WaveCurveSettings settings)
        {
            float progress = waveCount > 1 ? (n - 1f) / (waveCount - 1f) : 1f;
            float coreInterval = Mathf.Lerp(settings.coreIntervalStart, settings.coreIntervalEnd, progress);

            var entries = new List<WaveEntry>();
            for (int i = 0; i < roster.Count; i++)
            {
                if (counts[i] <= 0) continue;

                Role role = GetRole(roster[i]);
                float interval = role == Role.Air ? settings.airInterval : role == Role.Heavy ? settings.heavyInterval : coreInterval;
                float startDelay = role == Role.Air ? settings.airStartDelay : role == Role.Heavy ? settings.heavyStartDelay : 0f;

                // Split across spawn points, rotating which lane gets the remainder each wave.
                for (int l = 0; l < lanes; l++)
                {
                    int lane = (l + n - 1 + i) % lanes;
                    int laneCount = counts[i] / lanes + (l < counts[i] % lanes ? 1 : 0);
                    if (laneCount <= 0) continue;

                    entries.Add(new WaveEntry
                    {
                        enemyData = roster[i],
                        count = laneCount,
                        spawnInterval = interval,
                        startDelay = startDelay,
                        spawnPointIndex = lane
                    });
                }
            }

            return entries.ToArray();
        }

        private static float HP(EnemyData enemy, int level) => Mathf.Max(1, enemy.GetScaledHP(level));

        private static int CheapestOf(List<int> indices, IReadOnlyList<EnemyData> roster, int level)
        {
            int cheapest = -1;
            foreach (int i in indices)
                if (cheapest < 0 || HP(roster[i], level) < HP(roster[cheapest], level)) cheapest = i;
            return cheapest;
        }

        private static EnemyData FirstWithRole(IReadOnlyList<EnemyData> roster, Role role)
        {
            for (int i = 0; i < roster.Count; i++)
                if (GetRole(roster[i]) == role) return roster[i];
            return null;
        }

        private static int CountAvailable(IReadOnlyList<EnemyData> roster, int[] intro, int n, Role role)
        {
            int count = 0;
            for (int i = 0; i < roster.Count; i++)
                if (intro[i] <= n && GetRole(roster[i]) == role) count++;
            return count;
        }
    }
}
