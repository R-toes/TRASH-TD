using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Guard class — Melee DPS (GDD 1.3).
    /// High single-target damage, blocks 1–2 enemies.
    /// Targets the enemy with the lowest HP among blocked/nearby enemies (focus fire).
    /// </summary>
    public class GuardOperator : OperatorBase
    {
        protected override EnemyBase FindTarget()
        {
            // Priority 1: Attack blocked enemies first (focus on lowest HP among blocked)
            if (blockedEnemies.Count > 0)
            {
                EnemyBase lowestHP = null;
                int lowestHPValue = int.MaxValue;

                for (int i = 0; i < blockedEnemies.Count; i++)
                {
                    var enemy = blockedEnemies[i];
                    if (enemy != null && !enemy.IsDead && enemy.CurrentHP < lowestHPValue)
                    {
                        lowestHP = enemy;
                        lowestHPValue = enemy.CurrentHP;
                    }
                }

                if (lowestHP != null) return lowestHP;
            }

            // Priority 2: If not blocking, search for enemies within attack range pattern
            if (EnemyManager.Instance != null && deployedCell != null && data != null && data.rangePattern != null)
            {
                var gridManager = FindFirstObjectByType<GridManager>();
                if (gridManager != null)
                {
                    var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern);
                    var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells);

                    EnemyBase lowestHP = null;
                    int lowestHPValue = int.MaxValue;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var enemy = candidates[i];
                        if (enemy != null && !enemy.IsDead && enemy.CurrentHP < lowestHPValue)
                        {
                            lowestHP = enemy;
                            lowestHPValue = enemy.CurrentHP;
                        }
                    }

                    return lowestHP;
                }
            }

            return null;
        }
    }
}
