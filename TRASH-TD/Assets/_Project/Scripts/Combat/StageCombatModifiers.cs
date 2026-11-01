using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Combat
{
    public static class StageCombatModifiers
    {
        private const float SandstormAttackIntervalMultiplier = 0.95f;
        private const float SandstormMissChance = 0.1f;

        private static bool sandstormEnvironment;
        private static bool heatEnvironment;

        /// <summary>
        /// Master switch for every stage hazard (sandstorm, heat, falling items, traffic, acid rain).
        /// Always on in real stages; the QA sandbox can turn it off to test units without the environment.
        /// </summary>
        public static bool HazardsEnabled { get; set; } = true;

        public static bool IsSandstormActive => sandstormEnvironment && HazardsEnabled;
        public static bool IsHeatHazardActive => heatEnvironment && HazardsEnabled;

        public static void ConfigureForStage(StageData stageData)
        {
            ConfigureForEnvironment(stageData != null ? stageData.stageId : null);
        }

        /// <summary>Activates the combat modifiers of the given stage's environment (the sandbox passes its map's stage).</summary>
        public static void ConfigureForEnvironment(string environmentStageId)
        {
            sandstormEnvironment = environmentStageId == "STAGE_03";
            heatEnvironment = environmentStageId == "STAGE_04";
            HazardsEnabled = true;
        }

        public static float GetAttackInterval(float baseInterval)
        {
            return IsSandstormActive ? baseInterval * SandstormAttackIntervalMultiplier : baseInterval;
        }

        public static bool TryAttackHit()
        {
            return !IsSandstormActive || Random.value >= SandstormMissChance;
        }

        public static bool TryAttackHit(float missChance)
        {
            missChance = Mathf.Clamp01(missChance);
            if (missChance <= 0f) return true;
            if (missChance >= 1f) return false;
            return Random.value >= missChance;
        }

        public static bool TryAttackHit(Vector3 targetPosition)
        {
            if (TryAttackHit()) return true;
            FloatingCombatNumber.ShowMiss(targetPosition);
            Audio.AudioManager.Instance?.PlaySfx(Audio.SfxId.CombatMiss, 0.6f);
            return false;
        }
    }
}
