using UnityEngine;
using TrashTD.Data;

namespace TrashTD.Combat
{
    public static class StageCombatModifiers
    {
        private const float SandstormAttackIntervalMultiplier = 0.95f;
        private const float SandstormMissChance = 0.1f;

        public static bool IsSandstormActive { get; private set; }

        public static void ConfigureForStage(StageData stageData)
        {
            IsSandstormActive = stageData != null && stageData.stageId == "STAGE_03";
        }

        public static float GetAttackInterval(float baseInterval)
        {
            return IsSandstormActive ? baseInterval * SandstormAttackIntervalMultiplier : baseInterval;
        }

        public static bool TryAttackHit()
        {
            return !IsSandstormActive || Random.value >= SandstormMissChance;
        }

        public static bool TryAttackHit(Vector3 targetPosition)
        {
            if (TryAttackHit()) return true;
            FloatingCombatNumber.ShowMiss(targetPosition);
            return false;
        }
    }
}
