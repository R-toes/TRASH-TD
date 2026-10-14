using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>
    /// Tuning for WaveCurveBuilder. Every wave gets a strength budget (total enemy HP) from one curve:
    /// steady growth per wave, a spike every few waves, a floor right after each spike so the next
    /// wave never collapses, and an extra-hard final wave. Edit the asset, then rebalance from the
    /// Wave Planner (TRASH TD > Wave Planner).
    /// </summary>
    [CreateAssetMenu(fileName = "WaveCurveSettings", menuName = "TRASH TD/Wave Curve Settings")]
    public class WaveCurveSettings : ScriptableObject
    {
        [Header("Difficulty curve")]
        [Tooltip("Growth per wave while the player's squad is still filling up (one new operator per wave), when their power rises fastest. 1.12 = +12% per wave.")]
        [Min(1f)] public float earlyGrowthPerWave = 1.12f;

        [Tooltip("How many waves use the early growth rate. Matches the squad/deck limit of 8 operators.")]
        [Min(0)] public int earlyGrowthWaves = 8;

        [Tooltip("Growth per wave after the early phase, when the player mainly grows through rarity upgrades. 1.03 = +3% per wave.")]
        [Min(1f)] public float growthPerWave = 1.03f;

        [Tooltip("Every Nth wave is a spike wave. The last wave is always a spike.")]
        [Min(2)] public int spikeInterval = 5;

        [Tooltip("Spike waves are this much stronger than the curve at that point.")]
        [Min(1f)] public float spikeMultiplier = 1.3f;

        [Tooltip("The wave right after a spike keeps at least this fraction of the spike's strength, so it never feels like a free wave.")]
        [Range(0.5f, 1f)] public float postSpikeFloor = 0.92f;

        [Tooltip("Extra multiplier on the final wave (on top of the spike) so it is the hardest wave of the stage.")]
        [Min(1f)] public float finalWaveMultiplier = 1.25f;

        [Header("Starting strength (in basic enemies on wave 1)")]
        [Min(1f)] public float startEnemiesEasy = 3f;
        [Min(1f)] public float startEnemiesNormal = 4f;
        [Min(1f)] public float startEnemiesHard = 5f;

        [Tooltip("Extra budget per additional spawn point, since multi-lane stages split the player's defense.")]
        [Min(0f)] public float extraBudgetPerLane = 0.25f;

        [Header("Composition")]
        [Tooltip("Share of a wave's budget spent on air enemies once they are introduced.")]
        [Range(0f, 0.6f)] public float airShare = 0.2f;

        [Tooltip("Share of a spike wave's budget spent on heavy (Tank) enemies.")]
        [Range(0f, 0.8f)] public float spikeHeavyShare = 0.4f;

        [Tooltip("Share of a regular wave's budget spent on heavy enemies in the late part of the stage.")]
        [Range(0f, 0.6f)] public float lateHeavyShare = 0.15f;

        [Tooltip("Heavy enemies start appearing in regular waves after this fraction of the stage.")]
        [Range(0f, 1f)] public float lateStageStart = 0.6f;

        [Header("Pacing (seconds)")]
        [Min(0f)] public float preWaveDelay = 3f;
        [Tooltip("Spawn interval of basic enemies on the first wave; it tightens toward the minimum by the last wave.")]
        [Min(0.1f)] public float coreIntervalStart = 2f;
        [Min(0.1f)] public float coreIntervalEnd = 1f;
        [Min(0.1f)] public float airInterval = 2.5f;
        [Min(0.1f)] public float heavyInterval = 3.5f;
        [Min(0f)] public float airStartDelay = 2.5f;
        [Min(0f)] public float heavyStartDelay = 4f;

        [Header("Roster")]
        [Tooltip("Enemies that must never be placed in waves (e.g. not fully configured yet).")]
        public List<EnemyData> excludedEnemies = new List<EnemyData>();

        public float GetStartEnemies(StageDifficulty difficulty)
        {
            return difficulty switch
            {
                StageDifficulty.Easy => startEnemiesEasy,
                StageDifficulty.Hard => startEnemiesHard,
                _ => startEnemiesNormal
            };
        }
    }
}
