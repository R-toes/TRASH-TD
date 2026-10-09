using UnityEngine;
using TrashTD.Core.Grid;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Pyrolite operator — Caster.
    /// Trait: Increase damage by 3% per enemy in range (stacks up to 5 times).
    /// </summary>
    public class PyroliteOperator : CasterOperator
    {
        private const float DamageBonusPerEnemy = 0.03f;
        private const int MaxBonusStacks = 5;

        public int GetEnemiesInRangeCount()
        {
            if (EnemyManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return 0;

            var gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return 0;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            var enemies = EnemyManager.Instance.GetEnemiesInCells(rangeCells, data.position);
            if (enemies == null) return 0;

            int count = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                var enemy = enemies[i];
                if (enemy != null && !enemy.IsDead)
                {
                    count++;
                }
            }
            return count;
        }

        protected override float GetDamageMultiplier()
        {
            int enemyCount = GetEnemiesInRangeCount();
            int stacks = Mathf.Clamp(enemyCount, 0, MaxBonusStacks);
            return 1f + (stacks * DamageBonusPerEnemy);
        }
    }
}

