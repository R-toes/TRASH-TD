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
    /// Specialist class — Utility, control, or mimic operators.
    /// Versatile operators with unique specialized mechanics.
    /// </summary>
    public class SpecialistOperator : OperatorBase
    {
        protected override EnemyBase FindTarget()
        {
            if (EnemyManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            if (blockedEnemies.Count > 0)
            {
                for (int i = 0; i < blockedEnemies.Count; i++)
                {
                    var enemy = blockedEnemies[i];
                    if (enemy != null && !enemy.IsDead && CanAttackTarget(enemy))
                    {
                        return enemy;
                    }
                }
            }

            var gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells, data.position);
            if (candidates == null || candidates.Count == 0) return null;

            EnemyBase bestTarget = null;
            float bestDistanceToGoal = float.PositiveInfinity;

            for (int i = 0; i < candidates.Count; i++)
            {
                var enemy = candidates[i];
                if (enemy == null || enemy.IsDead || !CanAttackTarget(enemy)) continue;

                float distToGoal = enemy.DistanceToGoal;
                if (distToGoal < bestDistanceToGoal)
                {
                    bestDistanceToGoal = distToGoal;
                    bestTarget = enemy;
                }
            }

            return bestTarget;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            PlayAttackSound();

            if (data != null && (data.position == OperatorPosition.Ranged || data.attackRange > 1))
            {
                CombatProjectileVisual.Fire(
                    transform.position,
                    target,
                    new Color(0.2f, 0.85f, 0.75f, 1f),
                    14f,
                    0.2f,
                    0.1f,
                    true,
                    () =>
                    {
                        if (target != null && !target.IsDead)
                        {
                            int damage = DamageCalculator.CalculateDamage(currentATK, data.damageType == DamageType.Arts ? target.CurrentRES : target.CurrentDEF);
                            target.TryTakeAttackDamage(damage, data.damageType, this);
                            PlayImpactSound();
                        }
                    });
            }
            else
            {
                MeleeSwipeVisual.Play(transform.position, target.VisualPosition, new Color(0.2f, 0.85f, 0.75f, 1f));
                int damage = DamageCalculator.CalculateDamage(currentATK, data != null && data.damageType == DamageType.Arts ? target.CurrentRES : target.CurrentDEF);
                target.TryTakeAttackDamage(damage, data != null ? data.damageType : DamageType.Physical, this);
                PlayImpactSound();
            }
        }
    }
}
