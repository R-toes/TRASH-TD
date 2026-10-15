using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>
    /// Read-only analysis of authored waves: totals, spawn timing, and authoring mistakes that
    /// WaveManager would otherwise skip silently. Mirrors WaveManager's spawn rules so the numbers
    /// match what actually spawns. Used by the Wave Planner editor window.
    /// </summary>
    public static class WaveAnalysis
    {
        /// <summary>
        /// A wave with more than this many times the previous wave's total HP is flagged as a spike.
        /// Designed spikes (including the final wave, about 1.7x) stay under it.
        /// </summary>
        public const float SpikeRatio = 2f;

        /// <summary>A wave with less than this fraction of the previous wave's total HP is flagged as a drop.</summary>
        public const float DropRatio = 0.75f;

        public sealed class WaveSummary
        {
            public int EnemyCount;
            public int AirCount;
            public int TotalHP;
            public int MaxLeakCost;
            public float SpawnDuration;
            /// <summary>Bit i is set when the wave spawns from spawn point i.</summary>
            public int SpawnPointMask;
            public readonly List<string> Warnings = new List<string>();

            public bool HasWarnings => Warnings.Count > 0;
        }

        /// <summary>
        /// Summarizes every wave and flags spikes/drops between neighbours. With curve settings, strength is
        /// compared as threat (HP weighted by armor, speed and flight), so a themed wave of few tough elites
        /// isn't mistaken for a drop; without them, raw total HP is compared.
        /// </summary>
        public static WaveSummary[] SummarizeAll(StageData stage, StageDifficulty difficulty, WaveCurveSettings settings = null)
        {
            WaveData[] waves = stage != null ? stage.GetWaves(difficulty) : null;
            if (waves == null) return new WaveSummary[0];

            string measure = settings != null ? "threat" : "total HP";
            var summaries = new WaveSummary[waves.Length];
            float previous = 0f;
            for (int i = 0; i < waves.Length; i++)
            {
                summaries[i] = Summarize(waves[i], stage, difficulty);

                float strength = settings != null ? WaveCurveBuilder.GetWaveThreat(waves[i], settings) : summaries[i].TotalHP;
                if (previous > 0f && strength > previous * SpikeRatio)
                {
                    summaries[i].Warnings.Add(
                        $"Difficulty spike: {strength / previous:0.0}x the previous wave's {measure}.");
                }
                else if (previous > 0f && strength < previous * DropRatio)
                {
                    summaries[i].Warnings.Add(
                        $"Difficulty drop: only {strength / previous:0%} of the previous wave's {measure}.");
                }
                previous = strength;
            }

            return summaries;
        }

        public static WaveSummary Summarize(WaveData wave, StageData stage, StageDifficulty difficulty)
        {
            var summary = new WaveSummary();
            if (wave == null)
            {
                summary.Warnings.Add("Wave is missing.");
                return summary;
            }

            if (wave.entries == null || wave.entries.Length == 0)
            {
                summary.Warnings.Add("Wave has no entries, so nothing spawns.");
                return summary;
            }

            int spawnPointCount = stage != null && stage.spawnPoints != null ? stage.spawnPoints.Length : 0;

            for (int i = 0; i < wave.entries.Length; i++)
            {
                WaveEntry entry = wave.entries[i];
                if (entry == null || entry.enemyData == null)
                {
                    summary.Warnings.Add($"Entry {i + 1}: no enemy assigned (skipped at runtime).");
                    continue;
                }

                string label = $"Entry {i + 1} ({entry.enemyData.enemyName})";
                if (entry.count <= 0)
                {
                    summary.Warnings.Add($"{label}: count is {entry.count}, so nothing spawns.");
                    continue;
                }

                if (entry.spawnPointIndex < 0 || entry.spawnPointIndex >= spawnPointCount)
                {
                    summary.Warnings.Add(
                        $"{label}: spawn point {entry.spawnPointIndex} does not exist (stage has {spawnPointCount}), so these enemies never spawn.");
                    continue;
                }

                if (entry.spawnInterval < 0f || entry.startDelay < 0f)
                    summary.Warnings.Add($"{label}: negative interval or delay is treated as 0.");

                summary.EnemyCount += entry.count;
                if (entry.enemyData.movementType == EnemyMovementType.Air) summary.AirCount += entry.count;
                summary.TotalHP += entry.enemyData.baseHP * entry.count;
                summary.MaxLeakCost += entry.enemyData.lifePointCost * entry.count;
                summary.SpawnDuration = Mathf.Max(summary.SpawnDuration, GetEntryDuration(entry));
                if (entry.spawnPointIndex < 31) summary.SpawnPointMask |= 1 << entry.spawnPointIndex;
            }

            return summary;
        }

        /// <summary>Seconds from wave start until the entry's last enemy spawns.</summary>
        public static float GetEntryDuration(WaveEntry entry)
        {
            if (entry == null) return 0f;
            return Mathf.Max(0f, entry.startDelay) + Mathf.Max(0, entry.count - 1) * Mathf.Max(0f, entry.spawnInterval);
        }

        public static WaveData Clone(WaveData wave)
        {
            if (wave == null) return null;

            var copy = new WaveData
            {
                waveName = wave.waveName,
                preWaveDelay = wave.preWaveDelay,
                entries = new WaveEntry[wave.entries != null ? wave.entries.Length : 0]
            };

            for (int i = 0; i < copy.entries.Length; i++)
            {
                WaveEntry entry = wave.entries[i];
                if (entry == null) continue;
                copy.entries[i] = new WaveEntry
                {
                    enemyData = entry.enemyData,
                    count = entry.count,
                    spawnInterval = entry.spawnInterval,
                    startDelay = entry.startDelay,
                    spawnPointIndex = entry.spawnPointIndex
                };
            }

            return copy;
        }

        public static WaveData[] CloneAll(WaveData[] waves)
        {
            if (waves == null) return new WaveData[0];

            var copies = new WaveData[waves.Length];
            for (int i = 0; i < waves.Length; i++) copies[i] = Clone(waves[i]);
            return copies;
        }
    }
}
