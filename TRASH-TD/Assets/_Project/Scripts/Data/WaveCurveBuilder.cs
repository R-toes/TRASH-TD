using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>
    /// Builds a stage's waves from one shared difficulty curve (see WaveCurveSettings). Each wave gets a
    /// strength budget in threat (enemy HP weighted by speed, armor and flight, see GetThreat), and the
    /// budget is filled with the stage's roster. Everything depends on the wave number only, except that
    /// the last wave gets the finale boost, so Easy/Normal/Hard share their opening waves and differ in
    /// length. New enemy types are introduced one at a time with a small first count; heavy enemies debut
    /// on spike waves. The result is a draft to hand-tune in the Wave Planner.
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
        /// How hard an enemy is to kill, in HP-equivalents: its HP, scaled up for DEF/RES (against a typical
        /// squad's damage), for speed (less time inside operator range) and for flight (only ranged operators
        /// and Casters can hit it).
        /// </summary>
        public static float GetThreat(EnemyData enemy, WaveCurveSettings settings)
        {
            if (enemy == null) return 0f;

            float physical = settings.referencePhysicalATK;
            float arts = settings.referenceArtsATK;
            float share = settings.physicalDamageShare;

            float unarmored = share * physical + (1f - share) * arts;
            float dealt = share * Mathf.Max(physical - enemy.baseDEF, physical * 0.05f) +
                          (1f - share) * Mathf.Max(arts - enemy.baseRES, arts * 0.05f);
            float armor = Mathf.Clamp(unarmored / Mathf.Max(0.01f, dealt), 1f, settings.maxArmorThreat);

            float speed = Mathf.Max(0.5f, 1f + settings.speedThreatWeight * (enemy.moveSpeed / settings.referenceSpeed - 1f));
            float air = GetRole(enemy) == Role.Air ? settings.airThreatMultiplier : 1f;

            return Mathf.Max(1, enemy.baseHP) * armor * speed * air;
        }

        public static float GetWaveThreat(WaveData wave, WaveCurveSettings settings)
        {
            float threat = 0f;
            if (wave?.entries == null) return threat;

            foreach (WaveEntry entry in wave.entries)
                if (entry?.enemyData != null && entry.count > 0) threat += entry.count * GetThreat(entry.enemyData, settings);
            return threat;
        }

        /// <summary>
        /// Nominal strength budget per wave (index 0 = wave 1), in threat, before enemy counts are rounded.
        /// BuildWaves follows the same rules using each wave's actual strength.
        /// </summary>
        public static float[] BuildBudgetCurve(int waveCount, float baseBudget, WaveCurveSettings settings)
        {
            var budgets = new float[Mathf.Max(0, waveCount)];
            for (int n = 1; n <= waveCount; n++)
            {
                float level = n == 1 ? baseBudget : budgets[n - 2] * GrowthFor(n, settings);
                budgets[n - 1] = level * SpikeFactor(n, waveCount, settings);
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
                return GetThreat(a, settings).CompareTo(GetThreat(b, settings));
            });
            return ordered;
        }

        private static int RoleOrder(Role role) => role == Role.Core ? 0 : role == Role.Air ? 1 : 2;

        /// <summary>
        /// Wave (1-based) on which each roster enemy first appears. Non-heavy types start on wave 1 and follow
        /// every introductionSpacing waves, skipping spike waves; heavy types debut on spike waves from
        /// firstHeavyWave. The schedule ignores stage length: types scheduled past the last wave simply don't
        /// appear on that difficulty.
        /// </summary>
        public static int[] BuildIntroductionWaves(IReadOnlyList<EnemyData> orderedRoster, WaveCurveSettings settings)
        {
            var intro = new int[orderedRoster.Count];
            int interval = Mathf.Max(2, settings.spikeInterval);

            int heavyWave = Mathf.CeilToInt(Mathf.Max(1, settings.firstHeavyWave) / (float)interval) * interval;
            int nextWave = 1;
            for (int i = 0; i < orderedRoster.Count; i++)
            {
                if (GetRole(orderedRoster[i]) == Role.Heavy)
                {
                    intro[i] = heavyWave;
                    heavyWave += interval;
                    continue;
                }

                while (nextWave > 1 && nextWave % interval == 0) nextWave++;
                intro[i] = nextWave;
                nextWave += Mathf.Max(1, settings.introductionSpacing);
            }

            return intro;
        }

        public static WaveData[] BuildWaves(StageData stage, int waveCount, IReadOnlyList<EnemyData> orderedRoster,
            WaveCurveSettings settings)
        {
            if (stage == null || settings == null || orderedRoster == null || orderedRoster.Count == 0 || waveCount <= 0)
                return new WaveData[0];

            int lanes = Mathf.Max(1, stage.spawnPoints != null ? stage.spawnPoints.Length : 1);

            EnemyData unit = FirstWithRole(orderedRoster, Role.Core) ?? orderedRoster[0];
            float baseBudget = settings.startEnemies * GetThreat(unit, settings);

            int[] intro = BuildIntroductionWaves(orderedRoster, settings);

            // The curve itself is stage-neutral (same as BuildBudgetCurve); each stage's bonus goes on top.
            float[] curve = BuildBudgetCurve(waveCount, baseBudget, settings);

            var waves = new WaveData[waveCount];
            float previousThreat = 0f;
            for (int n = 1; n <= waveCount; n++)
            {
                bool isSpike = IsSpikeWave(n, waveCount, settings.spikeInterval);
                float budget = curve[n - 1] * StageFactor(stage, n, settings);

                // Rounding enemy counts can undo the curve, so each wave gets a minimum strength.
                float minimumThreat = n == 1 ? 0f : previousThreat * (isSpike ? settings.spikeMultiplier : 1.01f);

                waves[n - 1] = BuildWave(n, waveCount, budget, minimumThreat, lanes, orderedRoster, intro,
                    settings, out previousThreat);
            }
            return waves;
        }

        /// <summary>
        /// How much stronger this stage's wave n is than the first stage's. On wave 1 it adds exactly
        /// stage.extraStartEnemies basic enemies; the bonus then fades to stageBonusLateShare of that by
        /// stageBonusFadeWaves, so later stages open harder without making their final waves unbeatable.
        /// </summary>
        public static float StageFactor(StageData stage, int waveNumber, WaveCurveSettings settings)
        {
            int extra = stage != null ? Mathf.Max(0, stage.extraStartEnemies) : 0;
            if (extra == 0) return 1f;

            float fade = Mathf.Clamp01((waveNumber - 1f) / Mathf.Max(1, settings.stageBonusFadeWaves - 1));
            float share = Mathf.Lerp(1f, settings.stageBonusLateShare, fade);
            return 1f + extra / Mathf.Max(1f, settings.startEnemies) * share;
        }

        private static float GrowthFor(int n, WaveCurveSettings settings)
        {
            return n <= settings.earlyGrowthWaves ? settings.earlyGrowthPerWave : settings.growthPerWave;
        }

        private static float SpikeFactor(int n, int waveCount, WaveCurveSettings settings)
        {
            if (!IsSpikeWave(n, waveCount, settings.spikeInterval)) return 1f;
            return settings.spikeMultiplier * (n == waveCount ? settings.finalWaveMultiplier : 1f);
        }

        private static WaveData BuildWave(int n, int waveCount, float budget, float minimumThreat, int lanes,
            IReadOnlyList<EnemyData> roster, int[] intro, WaveCurveSettings settings, out float realizedThreat)
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
                    spent += counts[i] * GetThreat(roster[i], settings);
                    newcomers.Add(roster[i].enemyName);
                    continue;
                }
                if (isNew) newcomers.Add(roster[i].enemyName);

                if (role == Role.Core) establishedCore.Add(i);
                else if (role == Role.Air) establishedAir.Add(i);
                else establishedHeavy.Add(i);
            }

            // Heavy: a big share on spike waves, a small share in late regular waves.
            float heavyShare = isSpike ? settings.spikeHeavyShare
                : (n >= settings.lateHeavyFromWave ? settings.lateHeavyShare : 0f);
            if (heavyShare > 0f && establishedHeavy.Count > 0)
            {
                float perType = budget * heavyShare / establishedHeavy.Count;
                foreach (int i in establishedHeavy)
                {
                    int count = Mathf.RoundToInt(perType / GetThreat(roster[i], settings));
                    if (isSpike) count = Mathf.Max(1, count);
                    counts[i] += count;
                    spent += count * GetThreat(roster[i], settings);
                }
            }

            // Air: a fixed share once introduced, at least one of each type.
            if (establishedAir.Count > 0)
            {
                float perType = budget * settings.airShare / establishedAir.Count;
                foreach (int i in establishedAir)
                {
                    int count = Mathf.Max(1, Mathf.RoundToInt(perType / GetThreat(roster[i], settings)));
                    counts[i] += count;
                    spent += count * GetThreat(roster[i], settings);
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
                int count = Mathf.RoundToInt(remaining * weight / GetThreat(roster[i], settings));
                counts[i] += count;
                coreSpawned += count;
            }
            if (coreSpawned == 0 && establishedCore.Count > 0 && remaining > 0f)
                counts[establishedCore[0]] += 1;

            realizedThreat = 0f;
            for (int i = 0; i < roster.Count; i++) realizedThreat += counts[i] * GetThreat(roster[i], settings);

            // Top up with the cheapest basic enemy until the wave meets its minimum strength.
            int filler = CheapestOf(establishedCore, roster, settings);
            if (filler < 0) filler = CheapestOf(establishedAir, roster, settings);
            while (filler >= 0 && realizedThreat < minimumThreat)
            {
                counts[filler]++;
                realizedThreat += GetThreat(roster[filler], settings);
            }

            return new WaveData
            {
                waveName = n == waveCount ? "Final Wave"
                    : isSpike ? $"Wave {n}: Spike"
                    : newcomers.Count > 0 && n > 1 ? $"Wave {n}: New {string.Join(", ", newcomers)}"
                    : $"Wave {n}",
                preWaveDelay = settings.preWaveDelay,
                entries = BuildEntries(n, lanes, roster, counts, settings)
            };
        }

        private static WaveEntry[] BuildEntries(int n, int lanes, IReadOnlyList<EnemyData> roster, int[] counts,
            WaveCurveSettings settings)
        {
            float progress = Mathf.Clamp01((n - 1f) / Mathf.Max(1, settings.intervalRampWaves - 1));
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

        private static int CheapestOf(List<int> indices, IReadOnlyList<EnemyData> roster, WaveCurveSettings settings)
        {
            int cheapest = -1;
            foreach (int i in indices)
                if (cheapest < 0 || GetThreat(roster[i], settings) < GetThreat(roster[cheapest], settings)) cheapest = i;
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
