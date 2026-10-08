using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Stag-ger — 2-Star Guard (2 Block).
    /// Ability: Six-Limb Slugger — Deals damage to all blocked enemies in reach
    /// (including his tile and adjacent tiles on his sides and back where allies block enemies).
    /// If no enemies are blocked, attacks a single unblocked enemy in range ahead.
    /// Range covers one tile ahead.
    /// </summary>
    public class StaggerOperator : GuardOperator
    {
        private readonly List<EnemyBase> targetBuffer = new List<EnemyBase>();

        protected override EnemyBase FindTarget()
        {
            if (!isDeployed) return null;

            // 1. If any enemies are blocked (by Stag-ger or by allies in front/sides/back), prioritize blocked targets
            List<EnemyBase> blockedTargets = GetBlockedTargetsInRange();
            if (blockedTargets.Count > 0)
            {
                EnemyBase lowestHP = null;
                int lowestHPValue = int.MaxValue;
                for (int i = 0; i < blockedTargets.Count; i++)
                {
                    EnemyBase enemy = blockedTargets[i];
                    if (enemy != null && !enemy.IsDead && enemy.CurrentHP < lowestHPValue)
                    {
                        lowestHP = enemy;
                        lowestHPValue = enemy.CurrentHP;
                    }
                }
                return lowestHP;
            }

            // 2. If no enemies are blocked, single-target lowest HP enemy in range ahead
            return FindUnblockedTargetInRange();
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            List<EnemyBase> blockedTargets = GetBlockedTargetsInRange();

            // Ability: If any enemies are blocked, strike ALL blocked enemies simultaneously
            if (blockedTargets.Count > 0)
            {
                PlayAttackSound();

                for (int i = 0; i < blockedTargets.Count; i++)
                {
                    EnemyBase enemy = blockedTargets[i];
                    if (enemy == null || enemy.IsDead) continue;

                    MeleeSwipeVisual.Play(
                        transform.position,
                        enemy.transform.position,
                        new Color(1f, 0.45f, 0.1f, 1f));

                    int damage = DamageCalculator.CalculateDamage(currentATK, GetTargetMitigation(enemy));
                    enemy.TryTakeAttackDamage(damage, data.damageType);
                }

                return;
            }

            // Single-target attack if no enemies are blocked
            PlayAttackSound();
            MeleeSwipeVisual.Play(
                transform.position,
                target.transform.position,
                new Color(1f, 0.78f, 0.3f, 1f));

            int singleDamage = DamageCalculator.CalculateDamage(currentATK, GetTargetMitigation(target));
            target.TryTakeAttackDamage(singleDamage, data.damageType);
        }

        /// <summary>
        /// Gathers all blocked enemies in reach:
        /// - Enemies blocked by Stag-ger directly
        /// - Enemies blocked on Stag-ger's own tile
        /// - Enemies blocked on adjacent tiles (front, sides, back) by allies or within cells
        /// </summary>
        private List<EnemyBase> GetBlockedTargetsInRange()
        {
            targetBuffer.Clear();

            // 1. Enemies blocked directly by Stag-ger
            for (int i = 0; i < blockedEnemies.Count; i++)
            {
                EnemyBase enemy = blockedEnemies[i];
                if (enemy != null && !enemy.IsDead && enemy.IsBlocked)
                {
                    if (!targetBuffer.Contains(enemy))
                    {
                        targetBuffer.Add(enemy);
                    }
                }
            }

            // 2. Check Stag-ger's own tile, the tile ahead in range, and adjacent tiles (sides & back)
            if (deployedCell != null)
            {
                GridManager gridManager = FindFirstObjectByType<GridManager>();
                if (gridManager != null)
                {
                    List<GridCell> reachCells = new List<GridCell>();

                    // Stag-ger's own cell
                    reachCells.Add(deployedCell);

                    // 4-directional adjacent neighbors (front, back, left side, right side)
                    GridCell[] neighbors = gridManager.GetNeighbors(deployedCell.GridPosition.x, deployedCell.GridPosition.y);
                    if (neighbors != null)
                    {
                        for (int n = 0; n < neighbors.Length; n++)
                        {
                            GridCell neighbor = neighbors[n];
                            if (neighbor != null && !reachCells.Contains(neighbor))
                            {
                                reachCells.Add(neighbor);
                            }
                        }
                    }

                    // Forward range pattern cells (if any additional cells)
                    if (data != null && data.rangePattern != null)
                    {
                        GridCell[] rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
                        if (rangeCells != null)
                        {
                            for (int r = 0; r < rangeCells.Length; r++)
                            {
                                GridCell cell = rangeCells[r];
                                if (cell != null && !reachCells.Contains(cell))
                                {
                                    reachCells.Add(cell);
                                }
                            }
                        }
                    }

                    // Check all enemies physically inside these reach cells (must be blocked)
                    if (EnemyManager.Instance != null)
                    {
                        List<EnemyBase> cellEnemies = EnemyManager.Instance.GetEnemiesInCells(reachCells);
                        for (int e = 0; e < cellEnemies.Count; e++)
                        {
                            EnemyBase enemy = cellEnemies[e];
                            if (enemy != null && !enemy.IsDead && enemy.IsBlocked)
                            {
                                if (!targetBuffer.Contains(enemy))
                                {
                                    targetBuffer.Add(enemy);
                                }
                            }
                        }
                    }

                    // Check any deployed ally on these reach cells and include their blocked enemies
                    if (OperatorManager.Instance != null)
                    {
                        var deployedOps = OperatorManager.Instance.DeployedOperators;
                        for (int o = 0; o < deployedOps.Count; o++)
                        {
                            OperatorBase ally = deployedOps[o];
                            if (ally != null && ally != this && ally.IsDeployed && ally.DeployedCell != null)
                            {
                                if (reachCells.Contains(ally.DeployedCell))
                                {
                                    var allyBlocked = ally.BlockedEnemies;
                                    if (allyBlocked != null)
                                    {
                                        for (int b = 0; b < allyBlocked.Count; b++)
                                        {
                                            EnemyBase enemy = allyBlocked[b];
                                            if (enemy != null && !enemy.IsDead && enemy.IsBlocked)
                                            {
                                                if (!targetBuffer.Contains(enemy))
                                                {
                                                    targetBuffer.Add(enemy);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return targetBuffer;
        }

        private EnemyBase FindUnblockedTargetInRange()
        {
            if (deployedCell == null || EnemyManager.Instance == null || data == null || data.rangePattern == null)
            {
                return null;
            }

            GridManager gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return null;

            GridCell[] rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            List<GridCell> targetCells = new List<GridCell>();
            if (rangeCells != null)
            {
                targetCells.AddRange(rangeCells);
            }
            if (!targetCells.Contains(deployedCell))
            {
                targetCells.Add(deployedCell);
            }

            var candidates = EnemyManager.Instance.GetEnemiesInCells(targetCells);

            EnemyBase lowestHP = null;
            int lowestHPValue = int.MaxValue;

            for (int i = 0; i < candidates.Count; i++)
            {
                EnemyBase enemy = candidates[i];
                if (enemy != null && !enemy.IsDead && enemy.CurrentHP < lowestHPValue)
                {
                    lowestHP = enemy;
                    lowestHPValue = enemy.CurrentHP;
                }
            }

            return lowestHP;
        }
    }
}
