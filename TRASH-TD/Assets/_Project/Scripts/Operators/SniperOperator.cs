using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Sniper class — Ranged DPS (GDD 1.3).
    /// High single-target damage, no blocking.
    /// Targets the enemy closest to reaching the exit (highest leak threat) within range,
    /// with preference for aerial/ranged targets (Flyers / Casters).
    /// </summary>
    public class SniperOperator : OperatorBase
    {
        protected override EnemyBase FindTarget()
        {
            if (EnemyManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            var gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern);
            var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells);
            if (candidates == null || candidates.Count == 0) return null;

            // Priority:
            // 1. Flyers/Air enemies first (anti-air specialist per GDD 1.5)
            // 2. Otherwise enemy with highest path progress (closest to exit)
            EnemyBase bestTarget = null;
            float maxScore = float.MinValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                var enemy = candidates[i];
                if (enemy == null || enemy.IsDead) continue;

                float score = 0f;
                // Anti-air priority bonus
                if (enemy.MovementType == TrashTD.Data.EnemyMovementType.Air)
                {
                    score += 1000f;
                }
                // Casters priority bonus
                else if (enemy.Data != null && enemy.Data.archetype == TrashTD.Data.EnemyArchetype.Caster)
                {
                    score += 500f;
                }

                // Distance to self (lower distance = slightly higher score tiebreak)
                float dist = Vector3.Distance(transform.position, enemy.transform.position);
                score -= dist;

                if (score > maxScore)
                {
                    maxScore = score;
                    bestTarget = enemy;
                }
            }

            return bestTarget;
        }
    }
}
