using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Enemies;
using TrashTD.Core.Grid;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Defender class — Tank (GDD 1.3).
    /// High DEF/HP, blocks 2–4 enemies, low damage.
    /// Targets the enemy with the highest remaining HP among blocked enemies (hold the biggest threat).
    /// </summary>
    public class DefenderOperator : OperatorBase
    {
        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            MeleeSwipeVisual.Play(transform.position, target.VisualPosition, new Color(0.55f, 0.9f, 1f, 1f), 1f, MeleeStyle.Bash);
            base.Attack(target);
        }

        protected override EnemyBase FindTarget()
        {
            // Priority 1: attack blocked enemies — target highest HP (chip down the toughest)
            if (blockedEnemies.Count > 0)
            {
                EnemyBase highestHP = null;
                int highestHPValue = 0;

                foreach (EnemyBase enemy in blockedEnemies)
                {
                    if (enemy != null && enemy.CurrentHP > highestHPValue)
                    {
                        highestHP = enemy;
                        highestHPValue = enemy.CurrentHP;
                    }
                }

                if (highestHP != null) return highestHP;
            }

            // Priority 2: If not blocking, search for enemies within range pattern or own tile (e.g. enemy held at boundary)
            if (EnemyManager.Instance != null && deployedCell != null)
            {
                var gridManager = Object.FindAnyObjectByType<GridManager>();
                if (gridManager != null)
                {
                    var rangeCellList = new List<GridCell>();
                    if (data != null && data.rangePattern != null)
                    {
                        var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
                        if (rangeCells != null)
                        {
                            rangeCellList.AddRange(rangeCells);
                        }
                    }
                    if (!rangeCellList.Contains(deployedCell))
                    {
                        rangeCellList.Add(deployedCell);
                    }

                    var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCellList, data.position);
                    EnemyBase highestHP = null;
                    int highestHPValue = 0;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var enemy = candidates[i];
                        if (enemy != null && CanAttackTarget(enemy) && enemy.CurrentHP > highestHPValue)
                        {
                            highestHP = enemy;
                            highestHPValue = enemy.CurrentHP;
                        }
                    }

                    return highestHP;
                }
            }

            return null;
        }

        /// <summary>
        /// Defenders have a passive damage reduction bonus beyond their DEF stat.
        /// </summary>
        public override void TakeDamage(int rawATK, Data.DamageType damageType, EnemyBase attacker = null)
        {
            // Defenders take slightly reduced damage as a class trait
            // TODO: Exact class trait bonus — not specified in GDD, using placeholder 10% reduction
            int reducedATK = Mathf.RoundToInt(rawATK * 0.9f);
            base.TakeDamage(reducedATK, damageType, attacker);
        }
    }
}
