using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Sniper class — Ranged DPS (GDD 1.3).
    /// High single-target damage, no blocking.
    /// Prioritizes air enemies, then the enemy closest to reaching the exit.
    /// </summary>
    public class SniperOperator : OperatorBase
    {
        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            PlayAttackSound();
            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                new Color(1f, 0.82f, 0.32f),
                12f,
                0.14f,
                0.08f,
                false,
                () => ApplySniperDamage(target));
        }

        private void ApplySniperDamage(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            int damage = Combat.DamageCalculator.CalculateDamage(currentATK, GetTargetMitigation(target));
            target.TryTakeAttackDamage(damage, data.damageType);
        }

        protected override EnemyBase FindTarget()
        {
            if (EnemyManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            var gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells);
            if (candidates == null || candidates.Count == 0) return null;

            EnemyBase bestTarget = null;
            float bestDistanceToGoal = float.PositiveInfinity;
            float bestDistanceToOperator = float.PositiveInfinity;

            for (int i = 0; i < candidates.Count; i++)
            {
                var enemy = candidates[i];
                if (enemy == null || enemy.IsDead) continue;

                bool isAir = enemy.MovementType == TrashTD.Data.EnemyMovementType.Air;
                bool bestIsAir = bestTarget != null && bestTarget.MovementType == TrashTD.Data.EnemyMovementType.Air;
                float distanceToGoal = enemy.DistanceToGoal;
                float distanceToOperator = Vector3.Distance(transform.position, enemy.transform.position);
                bool isCloserToGoal = distanceToGoal < bestDistanceToGoal && !Mathf.Approximately(distanceToGoal, bestDistanceToGoal);
                bool sameGoalDistanceButCloser = Mathf.Approximately(distanceToGoal, bestDistanceToGoal) && distanceToOperator < bestDistanceToOperator;

                if (bestTarget == null ||
                    (isAir && !bestIsAir) ||
                    (isAir == bestIsAir && (isCloserToGoal || sameGoalDistanceButCloser)))
                {
                    bestTarget = enemy;
                    bestDistanceToGoal = distanceToGoal;
                    bestDistanceToOperator = distanceToOperator;
                }
            }

            return bestTarget;
        }
    }
}
