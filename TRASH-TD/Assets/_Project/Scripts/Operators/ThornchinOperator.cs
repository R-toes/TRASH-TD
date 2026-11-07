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
    /// Thornchin — 2-Star Defender (3 Block).
    /// Ability: Bristling Spines — Whenever Thornchin is attacked, it retaliates against
    /// the attacker with sharp urchin spines, dealing Physical damage equal to Thornchin's ATK.
    /// 1x Upgraded: Retaliate spikes deal an additional DOT of 5 Arts damage for 5 seconds (stacks up to 4 times).
    /// 2x Upgraded: Deals additional AoE damage in the adjacent tiles that can stack up to 4 times.
    /// </summary>
    public class ThornchinOperator : DefenderOperator
    {
        private static readonly Color SpineColor = new Color(0.75f, 0.2f, 0.85f, 1f);

        private GridManager cachedGridManager;
        private GridManager GridManagerCache => cachedGridManager != null ? cachedGridManager : (cachedGridManager = FindAnyObjectByType<GridManager>());

        public bool HasFirstUpgrade => currentRarity >= OperatorRarity.Star3;
        public bool HasSecondUpgrade => currentRarity >= OperatorRarity.Star4;

        public void SetRarityForTesting(OperatorRarity rarity)
        {
            currentRarity = rarity;
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (IsCombatPaused) return;

            base.TakeDamage(rawATK, damageType, attacker);

            // Retaliate against the attacker when attacked by an enemy.
            // Environmental damage (acid tiles, acid rain, etc.) has no attacker and will not trigger retaliation.
            if (attacker != null && !attacker.IsDead)
            {
                Retaliate(attacker);
            }
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            MeleeSwipeVisual.Play(transform.position, target.VisualPosition, SpineColor, 1f, MeleeStyle.Spikes);
            base.Attack(target);
        }

        private void Retaliate(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            // Direct attacker retaliation: shoot urchin spike projectile
            if (!Application.isPlaying)
            {
                ApplyRetaliationDamage(target);
            }

            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target,
                SpineColor,
                16f,
                0.09f,
                0.04f,
                false,
                () =>
                {
                    if (Application.isPlaying && target != null && !target.IsDead)
                    {
                        ApplyRetaliationDamage(target);
                    }
                },
                ProjectileStyle.ThornchinSpike);

            // 2x Upgrade: Deals additional AOE damage in adjacent tiles (spikes shoot in 4 directions: left, right, up, down)
            if (HasSecondUpgrade)
            {
                TriggerAdjacentSpikes(target);
            }
        }

        private void TriggerAdjacentSpikes(EnemyBase directAttacker)
        {
            GridManager grid = GridManagerCache;
            Vector2Int myGridPos = deployedCell != null
                ? deployedCell.GridPosition
                : (grid != null ? grid.WorldToGridPosition(transform.position) : Vector2Int.zero);

            float cellSize = grid != null ? grid.CellSize : 1f;

            Vector2Int[] adjacentDirs = new[]
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.up,
                Vector2Int.down
            };

            for (int i = 0; i < adjacentDirs.Length; i++)
            {
                Vector2Int dir = adjacentDirs[i];
                Vector2Int adjGridPos = myGridPos + dir;
                GridCell adjCell = grid != null ? grid.GetCell(adjGridPos) : null;
                Vector3 targetWorldPos = adjCell != null
                    ? adjCell.WorldPosition
                    : transform.position + (Vector3)(Vector2)dir * cellSize;

                if (!Application.isPlaying)
                {
                    ApplyAdjacentAoEDamage(adjCell, targetWorldPos, directAttacker);
                }

                CombatProjectileVisual.Fire(
                    ProjectileOrigin,
                    targetWorldPos,
                    SpineColor,
                    16f,
                    0.09f,
                    0.04f,
                    false,
                    () =>
                    {
                        if (Application.isPlaying)
                        {
                            ApplyAdjacentAoEDamage(adjCell, targetWorldPos, directAttacker);
                        }
                    },
                    ProjectileStyle.ThornchinSpike);
            }
        }

        private void ApplyAdjacentAoEDamage(GridCell cell, Vector3 worldPos, EnemyBase directAttacker)
        {
            List<EnemyBase> enemies = null;
            if (cell != null && EnemyManager.Instance != null)
            {
                enemies = EnemyManager.Instance.GetEnemiesInCells(new List<GridCell> { cell });
            }
            else if (EnemyManager.Instance != null)
            {
                enemies = EnemyManager.Instance.GetEnemiesInRadius(worldPos, 0.6f, OperatorPosition.Melee);
            }

            if (enemies == null || enemies.Count == 0) return;

            for (int i = 0; i < enemies.Count; i++)
            {
                EnemyBase enemy = enemies[i];
                if (enemy == null || enemy.IsDead) continue;
                if (enemy == directAttacker) continue; // Direct attacker already hit

                ApplyRetaliationDamage(enemy);
            }
        }

        public void ApplyRetaliationDamage(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            PlayAbilitySound(SfxId.GameplayMeleeAttack);

            int damage = DamageCalculator.CalculateDamage(currentATK, target.CurrentDEF);
            target.TryTakeAttackDamage(damage, data != null ? data.damageType : DamageType.Physical, this, canHitAir: true);

            // 1x Upgrade: Retaliate spikes deal additional DOT that deals 5 Arts damage for 5 seconds (stacks up to 4 times)
            if (HasFirstUpgrade && !target.IsDead)
            {
                ThornchinSpikeDOT.Apply(target, this);
            }
        }
    }
}
