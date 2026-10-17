using UnityEngine;

namespace TrashTD.Operators
{
    /// <summary>
    /// Bulkhead operator — Defender.
    /// Trait: When blocking 3 or more enemies, increase attack and def by 20%.
    /// </summary>
    public class BulkheadOperator : DefenderOperator
    {
        private const int FrenzyBlockThreshold = 3;
        private const float AtkMultiplier = 1.20f;
        private const float DefMultiplier = 1.20f;

        public bool IsFrenzyActive => blockedEnemies != null && blockedEnemies.Count >= FrenzyBlockThreshold;

        public override int CurrentATK => IsFrenzyActive ? Mathf.RoundToInt(currentATK * AtkMultiplier) : base.CurrentATK;
        public override int CurrentDEF => IsFrenzyActive ? Mathf.RoundToInt(currentDEF * DefMultiplier) : base.CurrentDEF;
    }
}

