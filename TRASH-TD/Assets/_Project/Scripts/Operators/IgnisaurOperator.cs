using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Ignisaur — 2-Star Guard (1 Block).
    /// Range:
    /// - Own tile: Deals Physical damage with a melee slash to enemies on his tile / blocked enemies.
    /// - Range tiles (yellow front tile & green surrounding tiles): Deals Arts damage with ranged projectile attacks.
    /// Trait / Ability (Protean):
    /// - Reduces attack interval when not blocking an enemy (faster attacks).
    /// - Increases attack power by 25% when blocking an enemy.
    /// - Capable of targeting and attacking aerial/drone enemies.
    /// </summary>
    public class IgnisaurOperator : GuardOperator
    {
        private const float UnblockedIntervalMultiplier = 0.80f; // 20% faster attacks when not blocking
        private const float BlockedAtkMultiplier = 1.25f; // +25% ATK when blocking an enemy

        private static readonly Color SlashColor = new Color(1f, 0.82f, 0.2f, 1f);
        private static readonly Color ArtsProjectileColor = new Color(1f, 0.45f, 0.12f, 1f);

        public override bool CanHitAir => true;

        public bool IsBlocking => blockedEnemies != null && blockedEnemies.Count > 0;

        public override int CurrentATK => IsBlocking
            ? Mathf.RoundToInt(base.CurrentATK * BlockedAtkMultiplier)
            : base.CurrentATK;

        public override float GetAttackInterval()
        {
            float baseInterval = base.GetAttackInterval();
            return !IsBlocking ? baseInterval * UnblockedIntervalMultiplier : baseInterval;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            bool isOwnTile = IsOwnTileTarget(target);

            if (isOwnTile)
            {
                // Physical melee slash on own tile
                PlayAttackSound(SfxId.GameplayMeleeAttack);
                MeleeSwipeVisual.Play(transform.position, target.VisualPosition, SlashColor);
                ApplyDamage(target, DamageType.Physical);
            }
            else
            {
                // Arts ranged projectile on all range tiles (yellow front tile and green tiles)
                PlayAttackSound(SfxId.GameplayMagicAttack);
                CombatProjectileVisual.Fire(
                    transform.position,
                    target,
                    ArtsProjectileColor,
                    10f,
                    0.2f,
                    0.12f,
                    burstOnImpact: true,
                    () => ApplyDamage(target, DamageType.Arts));
            }
        }

        public DamageType GetDamageTypeForTarget(EnemyBase target)
        {
            return IsOwnTileTarget(target) ? DamageType.Physical : DamageType.Arts;
        }

        public bool IsOwnTileTarget(EnemyBase target)
        {
            if (target == null) return false;

            // Blocked enemies are engaged directly on the operator's tile
            if (blockedEnemies != null && blockedEnemies.Contains(target))
            {
                return true;
            }

            var gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager != null && deployedCell != null)
            {
                Vector2Int targetPos = gridManager.WorldToGridPosition(target.transform.position);
                Vector2Int localOffset = GetLocalGridOffset(targetPos);
                return localOffset == Vector2Int.zero;
            }

            // Fallback for tests or unassigned gridManager
            Vector3 diff = target.transform.position - transform.position;
            int localX = Mathf.RoundToInt(diff.x);
            int localY = Mathf.RoundToInt(diff.y);
            return localX == 0 && localY == 0;
        }

        private Vector2Int GetLocalGridOffset(Vector2Int targetGridPos)
        {
            if (deployedCell == null) return new Vector2Int(1, 0);

            Vector2Int diff = targetGridPos - deployedCell.GridPosition;
            return Facing switch
            {
                OperatorFacing.Up => new Vector2Int(diff.y, -diff.x),
                OperatorFacing.Left => new Vector2Int(-diff.x, -diff.y),
                OperatorFacing.Down => new Vector2Int(-diff.y, diff.x),
                _ => diff // OperatorFacing.Right
            };
        }

        protected override EnemyBase FindTarget()
        {
            // Priority 1: Attack blocked enemies first
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

            // Priority 2: Enemies in range pattern (ground and aerial)
            if (EnemyManager.Instance != null && deployedCell != null && data != null && data.rangePattern != null)
            {
                var gridManager = FindAnyObjectByType<GridManager>();
                if (gridManager != null)
                {
                    var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
                    var rangeCellList = new List<GridCell>(rangeCells);
                    if (deployedCell != null && !rangeCellList.Contains(deployedCell))
                    {
                        rangeCellList.Add(deployedCell);
                    }
                    var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCellList);

                    EnemyBase lowestHP = null;
                    int lowestHPValue = int.MaxValue;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var enemy = candidates[i];
                        if (enemy != null && CanAttackTarget(enemy) && enemy.CurrentHP < lowestHPValue)
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

        private void ApplyDamage(EnemyBase target, DamageType damageType)
        {
            if (target == null || target.IsDead) return;

            int mitigation = damageType == DamageType.Physical ? target.CurrentDEF : target.CurrentRES;
            int damage = DamageCalculator.CalculateDamage(CurrentATK, mitigation);
            if (target.TryTakeAttackDamage(damage, damageType, this, canHitAir: true))
            {
                PlayImpactSound();
            }
        }
    }
}
