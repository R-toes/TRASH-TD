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
    /// on spike waves. Each wave has a theme (see BuildThemes) deciding how its budget is spent, so later
    /// waves alternate between tank pushes, air raids, rushes, elite-only strikes and standard mixes.
    /// The result is a draft to hand-tune in the Wave Planner.
    /// </summary>
    public static class WaveCurveBuilder
    {
        private const int NewcomerCoreCount = 2;
        private const float EmphasizedCoreWeight = 0.6f;

        public enum Role { Core, Air, Heavy }

        public enum WaveTheme
        {
            /// <summary>Mostly basic enemies with a few air (and late on, heavy) enemies. Introductions use it.</summary>
            Standard,
            /// <summary>Only the fastest basic enemy.</summary>
            Rush,
            /// <summary>Only air enemies.</summary>
            AirRaid,
            /// <summary>Only heavy (Tank) enemies.</summary>
            ArmoredPush,
            /// <summary>Heavy and air enemies together, no basic ones.</summary>
            AirAndArmor,
            /// <summary>The fastest basic enemy together with air enemies.</summary>
            Blitz,
            /// <summary>Heavy enemies together with the fastest basic enemy.</summary>
            Siege,
            /// <summary>The last wave: every established type, with large heavy and air shares.</summary>
            FinalAssault
        }

        // How a theme fills its budget with basic (core) enemies.
        private enum CoreMode { None, Mixed, Fastest }

        // Orders regular and spike waves cycle through from firstThemedWave; each stage starts at a different point.
        // Neither cycle's length matches the waves between spikes, so the pattern shifts from one stretch to the next.
        private static readonly WaveTheme[] ThemeCycle =
        {
            WaveTheme.ArmoredPush, WaveTheme.Blitz, WaveTheme.AirRaid, WaveTheme.Standard,
            WaveTheme.AirAndArmor, WaveTheme.Rush, WaveTheme.Siege
        };
        private static readonly WaveTheme[] SpikeThemeCycle =
        {
            WaveTheme.AirAndArmor, WaveTheme.ArmoredPush, WaveTheme.Blitz, WaveTheme.AirRaid, WaveTheme.Siege
        };

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
        /// How hard an enemy is to beat, in HP-equivalents: its HP, scaled up for DEF/RES (against a typical
        /// squad's damage), for speed (less time inside operator range), for flight (only ranged operators
        /// and Casters can hit it) and for ATK (hard hitters break blockers and outpace healers). Basic enemies
        /// are scaled down: they come in crowds that area damage (Caster splash, Basurocket, Proxishot,
        /// Stag-ger, Bubblets) and multi-blocking Defenders cut through, and Medics heal through their chip damage.
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
            float attack = Mathf.Max(0.5f, 1f + settings.attackThreatWeight * (enemy.baseATK / settings.referenceEnemyATK - 1f));
            Role role = GetRole(enemy);
            float roleFactor = role == Role.Air ? settings.airThreatMultiplier
                : role == Role.Core ? settings.crowdThreatMultiplier : 1f;

            return Mathf.Max(1, enemy.baseHP) * armor * speed * attack * roleFactor;
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
            WaveTheme[] themes = BuildThemes(stage, waveCount, orderedRoster, intro, settings);

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

                waves[n - 1] = BuildWave(n, waveCount, themes[n - 1], budget, minimumThreat, lanes, orderedRoster, intro,
                    settings, out previousThreat);
            }
            return waves;
        }

        /// <summary>
        /// Theme of each wave (index 0 = wave 1). Wave 1, introduction waves and early spikes are Standard.
        /// From firstRushWave, the other early waves alternate Rush and Standard. From firstThemedWave, every
        /// wave takes the next theme of its cycle (spikes have their own) whose enemies are available, never the
        /// same theme twice in a row; the cycles' starting point depends on the stage. The last wave is the
        /// Final Assault. Only the last wave depends on the stage length, so difficulties share their waves.
        /// </summary>
        public static WaveTheme[] BuildThemes(StageData stage, int waveCount, IReadOnlyList<EnemyData> roster, int[] intro,
            WaveCurveSettings settings)
        {
            var themes = new WaveTheme[Mathf.Max(0, waveCount)];
            int offset = StageCycleOffset(stage);
            int regularCursor = offset;
            int spikeCursor = offset;
            bool rushNext = true;
            WaveTheme previous = WaveTheme.Standard;

            for (int n = 1; n <= waveCount; n++)
            {
                WaveTheme theme = WaveTheme.Standard;
                bool isSpike = IsSpikeWave(n, waveCount, settings.spikeInterval);
                if (n == waveCount && n > 1)
                {
                    theme = WaveTheme.FinalAssault;
                }
                else if (n > 1 && !IntroducesEnemy(intro, n))
                {
                    if (n >= settings.firstThemedWave)
                    {
                        WaveTheme[] cycle = isSpike ? SpikeThemeCycle : ThemeCycle;
                        for (int tries = 0; tries < cycle.Length; tries++)
                        {
                            WaveTheme candidate = cycle[(isSpike ? spikeCursor++ : regularCursor++) % cycle.Length];
                            if (candidate == previous || !IsThemeAvailable(candidate, n, roster, intro, settings)) continue;

                            theme = candidate;
                            break;
                        }
                    }
                    else if (n >= settings.firstRushWave && !isSpike)
                    {
                        if (rushNext && IsThemeAvailable(WaveTheme.Rush, n, roster, intro, settings)) theme = WaveTheme.Rush;
                        rushNext = !rushNext;
                    }
                }

                themes[n - 1] = theme;
                previous = theme;
            }

            return themes;
        }

        private static bool IntroducesEnemy(int[] intro, int n)
        {
            foreach (int wave in intro)
                if (wave == n) return true;
            return false;
        }

        /// <summary>True if the enemies a theme is built around were introduced before wave n.</summary>
        private static bool IsThemeAvailable(WaveTheme theme, int n, IReadOnlyList<EnemyData> roster, int[] intro,
            WaveCurveSettings settings)
        {
            var core = new List<int>();
            bool hasAir = false;
            bool hasHeavy = false;
            for (int i = 0; i < roster.Count; i++)
            {
                if (intro[i] >= n) continue;
                Role role = GetRole(roster[i]);
                if (role == Role.Core) core.Add(i);
                else if (role == Role.Air) hasAir = true;
                else hasHeavy = true;
            }

            bool hasFast = FastestCore(core, roster, settings) >= 0;
            switch (theme)
            {
                case WaveTheme.Rush: return hasFast;
                case WaveTheme.AirRaid: return hasAir;
                case WaveTheme.ArmoredPush: return hasHeavy;
                case WaveTheme.AirAndArmor: return hasAir && hasHeavy;
                case WaveTheme.Blitz: return hasAir && hasFast;
                case WaveTheme.Siege: return hasHeavy && hasFast;
                default: return true;
            }
        }

        /// <summary>
        /// Per-stage starting point in the theme cycle, from the stage's place in the progression
        /// (extraStartEnemies), so consecutive stages don't play the same sequence.
        /// </summary>
        private static int StageCycleOffset(StageData stage)
        {
            int progression = stage != null ? Mathf.Max(0, stage.extraStartEnemies) : 0;
            return progression * 2 % ThemeCycle.Length;
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

        private static WaveData BuildWave(int n, int waveCount, WaveTheme theme, float budget, float minimumThreat, int lanes,
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

            GetThemeMix(theme, isSpike, n, settings, out float heavyShare, out float airShare, out CoreMode coreMode);
            int fastCore = coreMode == CoreMode.Fastest ? FastestCore(establishedCore, roster, settings) : -1;

            // Themes with a heavy share always field at least one heavy enemy; Standard waves only on spikes.
            // Themed waves round their signature enemies up, so leftover budget doesn't turn into basic enemies.
            bool themed = theme != WaveTheme.Standard && theme != WaveTheme.FinalAssault;
            bool heavyRequired = isSpike || theme != WaveTheme.Standard;
            spent += SpendShare(budget * heavyShare, establishedHeavy, heavyRequired, themed, roster, counts, settings);
            spent += SpendShare(budget * airShare, establishedAir, true, themed, roster, counts, settings);

            // Core fills the rest: only the fastest type in Rush, Blitz and Siege waves; otherwise a different
            // core type is emphasized each wave for variety.
            float remaining = Mathf.Max(0f, budget - spent);
            if (fastCore >= 0)
            {
                counts[fastCore] += Mathf.Max(1, Mathf.RoundToInt(remaining / GetThreat(roster[fastCore], settings)));
            }
            else if (coreMode == CoreMode.Mixed)
            {
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
            }

            // Past a point more basic enemies only feed area damage, so the budget above the cap buys elites
            // (the theme's own, or any established ones) instead.
            var elitePool = new List<int>();
            if (heavyShare > 0f) elitePool.AddRange(establishedHeavy);
            if (airShare > 0f) elitePool.AddRange(establishedAir);
            if (elitePool.Count == 0)
            {
                elitePool.AddRange(establishedHeavy);
                elitePool.AddRange(establishedAir);
            }
            int coreCap = settings.maxCoreEnemies + settings.maxCoreEnemiesPerExtraSpawn * Mathf.Max(0, lanes - 1);
            bool coreCapped = false;
            if (elitePool.Count > 0 && CapCore(counts, roster, coreCap, settings, out float overflow))
            {
                SpendShare(overflow, elitePool, false, true, roster, counts, settings);
                coreCapped = true;
            }

            realizedThreat = 0f;
            for (int i = 0; i < roster.Count; i++) realizedThreat += counts[i] * GetThreat(roster[i], settings);

            // Top up until the wave meets its minimum strength, with the cheapest enemy that fits the theme.
            var themePool = new List<int>();
            if (coreCapped) themePool.AddRange(elitePool);
            else
            {
                if (fastCore >= 0) themePool.Add(fastCore);
                else if (coreMode == CoreMode.Mixed) themePool.AddRange(establishedCore);
                if (heavyShare > 0f) themePool.AddRange(establishedHeavy);
                if (airShare > 0f && coreMode != CoreMode.Mixed) themePool.AddRange(establishedAir);
            }
            int filler = CheapestOf(themePool, roster, settings);
            if (filler < 0) filler = CheapestOf(establishedCore, roster, settings);
            if (filler < 0) filler = CheapestOf(establishedAir, roster, settings);
            while (filler >= 0 && realizedThreat < minimumThreat)
            {
                counts[filler]++;
                realizedThreat += GetThreat(roster[filler], settings);
            }

            return new WaveData
            {
                waveName = n == waveCount ? "Final Wave"
                    : newcomers.Count > 0 && n > 1 ? $"Wave {n}: New {string.Join(", ", newcomers)}"
                    : theme != WaveTheme.Standard ? $"Wave {n}: {(isSpike ? "Spike - " : "")}{ThemeName(theme)}"
                    : isSpike ? $"Wave {n}: Spike"
                    : $"Wave {n}",
                preWaveDelay = settings.preWaveDelay,
                entries = BuildEntries(n, lanes, theme, roster, counts, settings)
            };
        }

        /// <summary>
        /// Turns enemy counts into spawn entries, split across spawn points. Each group spawns at its normal
        /// interval but finishes within the theme's spawn window, so big waves arrive together instead of one
        /// by one. Rushers in Rush, Blitz and Siege waves come in tight packs to overwhelm blockers.
        /// </summary>
        private static WaveEntry[] BuildEntries(int n, int lanes, WaveTheme theme, IReadOnlyList<EnemyData> roster,
            int[] counts, WaveCurveSettings settings)
        {
            float progress = Mathf.Clamp01((n - 1f) / Mathf.Max(1, settings.intervalRampWaves - 1));
            float coreInterval = Mathf.Lerp(settings.coreIntervalStart, settings.coreIntervalEnd, progress);
            float window = theme != WaveTheme.Standard ? settings.themedSpawnWindow : settings.spawnWindow;
            bool swarm = theme == WaveTheme.Rush || theme == WaveTheme.Blitz || theme == WaveTheme.Siege ||
                         theme == WaveTheme.FinalAssault;

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

                    if (swarm && role == Role.Core)
                    {
                        AddPacks(entries, roster[i], laneCount, lane, startDelay, settings);
                        continue;
                    }

                    entries.Add(new WaveEntry
                    {
                        enemyData = roster[i],
                        count = laneCount,
                        spawnInterval = FitInterval(interval, laneCount, window, settings),
                        startDelay = startDelay,
                        spawnPointIndex = lane
                    });
                }
            }

            return entries.ToArray();
        }

        /// <summary>The normal interval, shortened (down to the minimum) so the group finishes within the window.</summary>
        private static float FitInterval(float interval, int count, float window, WaveCurveSettings settings)
        {
            if (count <= 1) return interval;
            return Mathf.Max(settings.minSpawnInterval, Mathf.Min(interval, window / (count - 1)));
        }

        /// <summary>Splits a group into packs that spawn almost together, one pack after another.</summary>
        private static void AddPacks(List<WaveEntry> entries, EnemyData enemy, int count, int lane, float startDelay,
            WaveCurveSettings settings)
        {
            int size = Mathf.Max(1, settings.swarmPackSize);
            int packs = Mathf.CeilToInt(count / (float)size);
            float gap = packs <= 1 ? 0f : Mathf.Min(settings.swarmPackGap, settings.swarmWindow / (packs - 1));

            for (int p = 0; p < packs; p++)
            {
                entries.Add(new WaveEntry
                {
                    enemyData = enemy,
                    count = Mathf.Min(size, count - p * size),
                    spawnInterval = settings.swarmPackInterval,
                    startDelay = startDelay + p * gap,
                    spawnPointIndex = lane
                });
            }
        }

        /// <summary>
        /// Spends an amount of threat evenly over the given enemy types (at least one of each if required)
        /// and returns the threat spent. Nothing is spent when the amount is zero.
        /// </summary>
        private static float SpendShare(float amount, List<int> types, bool atLeastOneEach, bool roundUp,
            IReadOnlyList<EnemyData> roster, int[] counts, WaveCurveSettings settings)
        {
            if (amount <= 0f || types.Count == 0) return 0f;

            float spent = 0f;
            float perType = amount / types.Count;
            foreach (int i in types)
            {
                float threat = GetThreat(roster[i], settings);
                int count = roundUp ? Mathf.CeilToInt(perType / threat) : Mathf.RoundToInt(perType / threat);
                if (atLeastOneEach) count = Mathf.Max(1, count);
                counts[i] += count;
                spent += count * threat;
            }
            return spent;
        }

        /// <summary>
        /// Scales basic (core) enemy counts down to the cap, keeping their proportions. Returns true if anything
        /// was removed, with the threat removed in overflow.
        /// </summary>
        private static bool CapCore(int[] counts, IReadOnlyList<EnemyData> roster, int cap, WaveCurveSettings settings,
            out float overflow)
        {
            overflow = 0f;
            int total = 0;
            for (int i = 0; i < roster.Count; i++)
                if (GetRole(roster[i]) == Role.Core) total += counts[i];
            if (total <= cap) return false;

            for (int i = 0; i < roster.Count; i++)
            {
                if (GetRole(roster[i]) != Role.Core || counts[i] == 0) continue;

                int kept = Mathf.Max(1, counts[i] * cap / total);
                overflow += (counts[i] - kept) * GetThreat(roster[i], settings);
                counts[i] = kept;
            }
            return true;
        }

        /// <summary>The fastest core type, if it is faster than a basic enemy; -1 if none is.</summary>
        private static int FastestCore(List<int> core, IReadOnlyList<EnemyData> roster, WaveCurveSettings settings)
        {
            int fastest = -1;
            foreach (int i in core)
            {
                if (roster[i].moveSpeed <= settings.referenceSpeed) continue;
                if (fastest < 0 || roster[i].moveSpeed > roster[fastest].moveSpeed) fastest = i;
            }
            return fastest;
        }

        /// <summary>How a theme splits its budget between heavy, air and basic (core) enemies.</summary>
        private static void GetThemeMix(WaveTheme theme, bool isSpike, int n, WaveCurveSettings settings,
            out float heavyShare, out float airShare, out CoreMode coreMode)
        {
            float paired = settings.pairedThemeEliteShare;
            heavyShare = 0f;
            airShare = 0f;
            coreMode = CoreMode.None;
            switch (theme)
            {
                case WaveTheme.Rush: coreMode = CoreMode.Fastest; break;
                case WaveTheme.AirRaid: airShare = 1f; break;
                case WaveTheme.ArmoredPush: heavyShare = 1f; break;
                case WaveTheme.AirAndArmor: heavyShare = paired; airShare = 1f - paired; break;
                case WaveTheme.Blitz: airShare = paired; coreMode = CoreMode.Fastest; break;
                case WaveTheme.Siege: heavyShare = paired; coreMode = CoreMode.Fastest; break;
                case WaveTheme.FinalAssault:
                    // Elites carry the finale; the rest is a rusher swarm, not a crowd of grunts AoE would mop up.
                    heavyShare = settings.finalHeavyShare;
                    airShare = settings.finalAirShare;
                    coreMode = CoreMode.Fastest;
                    break;
                default:
                    // Standard: a big heavy share on spike waves, a small one in late regular waves.
                    heavyShare = isSpike ? settings.spikeHeavyShare
                        : (n >= settings.lateHeavyFromWave ? settings.lateHeavyShare : 0f);
                    airShare = settings.airShare;
                    coreMode = CoreMode.Mixed;
                    break;
            }
        }

        private static string ThemeName(WaveTheme theme)
        {
            switch (theme)
            {
                case WaveTheme.Rush: return "Rush";
                case WaveTheme.AirRaid: return "Air Raid";
                case WaveTheme.ArmoredPush: return "Armored Push";
                case WaveTheme.AirAndArmor: return "Air & Armor";
                case WaveTheme.Blitz: return "Blitz";
                case WaveTheme.Siege: return "Siege";
                case WaveTheme.FinalAssault: return "Final Assault";
                default: return "Standard";
            }
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
