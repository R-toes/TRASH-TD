using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Light-magic caster that locks onto one target and ramps its beam damage
    /// every 1.5 seconds while the target remains in range.
    /// </summary>
    public sealed class CastielOperator : CasterOperator
    {
        private const float LockStepDuration = 1.5f;
        private const float DamageTickInterval = 0.375f;

        private EnemyBase lockedTarget;
        private float lockDuration;
        private LightLaserVisual laser;

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            lockedTarget = null;
            lockDuration = 0f;
            laser = null;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            if (lockedTarget != target)
            {
                if (laser != null)
                {
                    laser.Stop();
                    laser = null;
                }

                lockedTarget = target;
                lockDuration = 0f;
            }

            if (laser == null)
            {
                PlayAttackSound();
                laser = LightLaserVisual.Fire(
                    transform,
                    target.transform,
                    new Color(1f, 0.98f, 0.65f, 1f),
                    DamageTickInterval,
                    ApplyBeamTick);
            }
        }

        protected override EnemyBase FindTarget()
        {
            if (EnemyManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            GridManager gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells);
            if (candidates == null || candidates.Count == 0)
            {
                ResetLock();
                return null;
            }

            if (lockedTarget != null && !lockedTarget.IsDead && candidates.Contains(lockedTarget))
                return lockedTarget;

            ResetLock();
            EnemyBase bestTarget = null;
            float closestToGoal = float.PositiveInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                EnemyBase candidate = candidates[i];
                if (candidate == null || candidate.IsDead || candidate.DistanceToGoal >= closestToGoal) continue;
                bestTarget = candidate;
                closestToGoal = candidate.DistanceToGoal;
            }

            return bestTarget;
        }

        private int GetCurrentBeamDamage()
        {
            if (lockDuration < LockStepDuration) return 5;
            if (lockDuration < LockStepDuration * 2f) return 15;
            if (lockDuration < LockStepDuration * 3f) return 30;
            if (lockDuration < LockStepDuration * 4f) return 80;
            if (lockDuration < LockStepDuration * 5f) return 100;
            return 150;
        }

        private void ApplyBeamTick()
        {
            if (lockedTarget == null || lockedTarget.IsDead) return;

            int damage = GetCurrentBeamDamage();
            lockedTarget.TryTakeAttackDamage(damage, DamageType.Arts);
            lockDuration += DamageTickInterval;
        }

        private void ResetLock()
        {
            if (laser != null)
            {
                laser.Stop();
                laser = null;
            }

            lockedTarget = null;
            lockDuration = 0f;
        }
    }
}
