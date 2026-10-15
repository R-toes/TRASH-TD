using System.Collections.Generic;
using UnityEngine;

namespace TrashTD.Data
{
    /// <summary>
    /// Tuning for WaveCurveBuilder. Every wave gets a strength budget in threat (enemy HP weighted by how hard
    /// each enemy is to kill, see WaveCurveBuilder.GetThreat) from one curve that only depends on the wave
    /// number: fast growth while the player's squad fills up, steady growth after, a step up every few waves
    /// and an extra-hard final wave. Difficulties share the curve and differ only in length, so wave N plays
    /// the same on Easy, Normal and Hard. Each stage scales the curve by its extraStartEnemies, so later
    /// stages are a bit harder. Edit the asset, then rebalance from the Wave Planner
    /// (TRASH TD > Wave Planner).
    /// </summary>
    [CreateAssetMenu(fileName = "WaveCurveSettings", menuName = "TRASH TD/Wave Curve Settings")]
    public class WaveCurveSettings : ScriptableObject
    {
        [Header("Difficulty curve")]
        [Tooltip("Basic enemies on wave 1. One operator (the first draft pick) must be able to handle it.")]
        [Min(1f)] public float startEnemies = 3f;

        [Tooltip("Growth per wave while the player's squad is still filling up (one new operator per wave), when their power rises fastest. 1.22 = +22% per wave.")]
        [Min(1f)] public float earlyGrowthPerWave = 1.22f;

        [Tooltip("How many waves use the early growth rate. Matches the squad/deck limit of 8 operators.")]
        [Min(0)] public int earlyGrowthWaves = 8;

        [Tooltip("Growth per wave after the early phase, when the player mainly grows through rarity upgrades and better draws. 1.03 = +3% per wave.")]
        [Min(1f)] public float growthPerWave = 1.03f;

        [Tooltip("Every Nth wave is a spike wave. The last wave is always a spike.")]
        [Min(2)] public int spikeInterval = 5;

        [Tooltip("A spike wave is this much stronger than a regular wave would be. Later waves keep growing from the spike, so every wave is harder than the one before.")]
        [Min(1f)] public float spikeMultiplier = 1.12f;

        [Tooltip("Extra multiplier on the final wave (on top of the spike) so it is the hardest wave of the stage.")]
        [Min(1f)] public float finalWaveMultiplier = 1.1f;

        [Header("Stage progression")]
        [Tooltip("Share of a stage's extra wave-1 enemies (StageData.extraStartEnemies) that still applies by stageBonusFadeWaves. Below 1, later stages start harder but their final waves stay within reach.")]
        [Range(0f, 1f)] public float stageBonusLateShare = 0.5f;

        [Tooltip("Wave by which the stage bonus has faded to stageBonusLateShare.")]
        [Min(2)] public int stageBonusFadeWaves = 30;

        [Header("Threat (how hard an enemy is to kill, relative to its HP)")]
        [Tooltip("Typical physical operator ATK, used to weigh enemy DEF.")]
        [Min(1f)] public float referencePhysicalATK = 70f;

        [Tooltip("Typical Arts operator ATK, used to weigh enemy RES.")]
        [Min(1f)] public float referenceArtsATK = 60f;

        [Tooltip("Share of the squad's damage that is physical (the rest is Arts).")]
        [Range(0f, 1f)] public float physicalDamageShare = 0.65f;

        [Tooltip("Upper limit on the DEF/RES threat factor, so one armored enemy can't eat a whole wave's budget.")]
        [Min(1f)] public float maxArmorThreat = 3f;

        [Tooltip("Move speed of a basic enemy. Faster enemies spend less time inside operator range.")]
        [Min(0.01f)] public float referenceSpeed = 0.75f;

        [Tooltip("How much speed counts (0 = ignored, 1 = threat scales fully with speed). Blockers stop fast enemies, so it only partly counts.")]
        [Range(0f, 1f)] public float speedThreatWeight = 0.5f;

        [Tooltip("Air enemies can only be hit by ranged operators and Casters, and can't be blocked.")]
        [Min(1f)] public float airThreatMultiplier = 1.5f;

        [Header("Composition")]
        [Tooltip("A new non-heavy enemy type is introduced every this many waves (spike waves are skipped).")]
        [Min(1)] public int introductionSpacing = 2;

        [Tooltip("Heavy (Tank) enemies debut on spike waves starting at this wave, once the squad is full.")]
        [Min(1)] public int firstHeavyWave = 10;

        [Tooltip("Share of a wave's budget spent on air enemies once they are introduced.")]
        [Range(0f, 0.6f)] public float airShare = 0.2f;

        [Tooltip("Share of a spike wave's budget spent on heavy (Tank) enemies.")]
        [Range(0f, 0.8f)] public float spikeHeavyShare = 0.35f;

        [Tooltip("Share of a regular wave's budget spent on heavy enemies from lateHeavyFromWave on.")]
        [Range(0f, 0.6f)] public float lateHeavyShare = 0.15f;

        [Tooltip("Heavy enemies also appear in regular waves from this wave on.")]
        [Min(1)] public int lateHeavyFromWave = 16;

        [Header("Pacing (seconds)")]
        [Min(0f)] public float preWaveDelay = 3f;
        [Tooltip("Spawn interval of basic enemies on wave 1; it tightens toward the minimum by intervalRampWaves.")]
        [Min(0.1f)] public float coreIntervalStart = 3f;
        [Min(0.1f)] public float coreIntervalEnd = 1.2f;
        [Tooltip("Wave at which the basic spawn interval reaches its minimum.")]
        [Min(2)] public int intervalRampWaves = 30;
        [Min(0.1f)] public float airInterval = 2.5f;
        [Min(0.1f)] public float heavyInterval = 3.5f;
        [Min(0f)] public float airStartDelay = 2.5f;
        [Min(0f)] public float heavyStartDelay = 4f;

        [Header("Roster")]
        [Tooltip("Enemies that must never be placed in waves (e.g. not fully configured yet).")]
        public List<EnemyData> excludedEnemies = new List<EnemyData>();
    }
}
