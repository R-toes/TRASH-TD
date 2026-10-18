using System.Collections.Generic;
using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Stag-ger — 2-Star Guard (2 Block).
    /// Ability: Six-Limb Slugger — Deals damage to all blocked enemies in reach
    /// (including his tile and forward range where enemies are blocked).
    /// If no enemies are blocked, attacks a single unblocked enemy in range ahead.
    /// Has a 20% to dodge any attack, atk then increase by 50% in the next attack if the dodge is successful.
    /// Range covers one tile ahead.
    /// </summary>
    public class StaggerOperator : GuardOperator
    {
        public const float DodgeChance = 0.20f;

        private readonly List<EnemyBase> targetBuffer = new List<EnemyBase>();
        private bool hasDodgeAtkBuff;

        public bool HasDodgeAtkBuff => hasDodgeAtkBuff;

        /// <summary>
        /// Random value generator for dodge roll in [0, 1).
        /// Overridable in unit tests for deterministic testing.
        /// </summary>
        public System.Func<float> RandomValueProvider { get; set; } = () => Random.value;

        protected override void OnDeployed()
        {
            base.OnDeployed();
            hasDodgeAtkBuff = false;
        }

        protected override void OnRetreated()
        {
            hasDodgeAtkBuff = false;
            base.OnRetreated();
        }

        public bool TryRollDodge()
        {
            float roll = RandomValueProvider != null ? RandomValueProvider() : Random.value;
            if (roll < DodgeChance)
            {
                hasDodgeAtkBuff = true;
                FloatingCombatNumber.ShowText(transform.position, "DODGE!", new Color(0.2f, 0.85f, 1f));
                return true;
            }

            return false;
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (IsCombatPaused || currentHP <= 0 || rawATK <= 0) return;

            // Attacks from enemies can be dodged (20% chance).
            // Environmental damage (overheat from stage 4, acid pools, etc.) has attacker == null and cannot be dodged.
            if (attacker != null && TryRollDodge())
            {
                return;
            }

            base.TakeDamage(rawATK, damageType, attacker);
        }

        public override bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false)
        {
            if (IsCombatPaused || currentHP <= 0 || damage <= 0) return false;

            // Vehicles from Stage 5 call TakeTrueDamage with ignoreShieldNegation: true and can be dodged.
            // Acid rain and acid tile call TakeTrueDamage with ignoreShieldNegation: false and cannot be dodged.
            if (ignoreShieldNegation && TryRollDodge())
            {
                return false;
            }

            return base.TakeTrueDamage(damage, ignoreShieldNegation);
        }

        protected override EnemyBase FindTarget()
        {
            if (!isDeployed) return null;

            // 1. If any enemies are blocked (by Stag-ger or in range ahead), prioritize blocked targets
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

            int attackAtk = hasDodgeAtkBuff
                ? Mathf.RoundToInt(currentATK * 1.5f)
                : currentATK;

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
                        enemy.VisualPosition,
                        hasDodgeAtkBuff
                            ? new Color(1f, 0.92f, 0.25f, 1f)
                            : new Color(1f, 0.45f, 0.1f, 1f));

                    int damage = DamageCalculator.CalculateDamage(attackAtk, GetTargetMitigation(enemy));
                    enemy.TryTakeAttackDamage(damage, data.damageType, this);
                }

                hasDodgeAtkBuff = false;
                return;
            }

            // Single-target attack if no enemies are blocked
            PlayAttackSound();
            MeleeSwipeVisual.Play(
                transform.position,
                target.VisualPosition,
                hasDodgeAtkBuff
                    ? new Color(1f, 0.95f, 0.35f, 1f)
                    : new Color(1f, 0.78f, 0.3f, 1f));

            int singleDamage = DamageCalculator.CalculateDamage(attackAtk, GetTargetMitigation(target));
            target.TryTakeAttackDamage(singleDamage, data.damageType, this);
            hasDodgeAtkBuff = false;
        }

        /// <summary>
        /// Gathers all blocked enemies in reach:
        /// - Enemies blocked by Stag-ger directly
        /// - Enemies blocked on Stag-ger's own tile or within forward range pattern
        /// - Enemies blocked by allies deployed within his attack range
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

            // 2. Check Stag-ger's own tile and forward range pattern tiles
            if (deployedCell != null)
            {
                GridManager gridManager = FindFirstObjectByType<GridManager>();
                if (gridManager != null)
                {
                    List<GridCell> reachCells = new List<GridCell>();

                    // Stag-ger's own cell
                    reachCells.Add(deployedCell);

                    // Forward range pattern cells
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
                        List<EnemyBase> cellEnemies = EnemyManager.Instance.GetEnemiesInCells(reachCells, data.position);
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

            var candidates = EnemyManager.Instance.GetEnemiesInCells(targetCells, data.position);

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
