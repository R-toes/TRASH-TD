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
        private OperatorSpriteAnimation spriteAnim;

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            lockedTarget = null;
            lockDuration = 0f;
            laser = null;
            spriteAnim = GetComponent<OperatorSpriteAnimation>();
            if (spriteAnim != null)
            {
                spriteAnim.FrameDuration = 0.10f;
            }
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
                    target,
                    LaserPalette.Holy,
                    DamageTickInterval,
                    ApplyBeamTick);
            }
        }

        protected override EnemyBase FindTarget()
        {
            if (EnemyManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells, data.position);
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

        private static readonly int[] BeamDamageByStage = { 5, 15, 30, 80, 100, 150 };

        /// <summary>Ramp stage 0-5: one step every LockStepDuration of lock.</summary>
        private int GetBeamStage() => Mathf.Min(BeamDamageByStage.Length - 1, Mathf.FloorToInt(lockDuration / LockStepDuration));

        private int GetCurrentBeamDamage() => BeamDamageByStage[GetBeamStage()];

        private void ApplyBeamTick()
        {
            if (lockedTarget == null || lockedTarget.IsDead) return;

            int damage = GetCurrentBeamDamage();
            lockedTarget.TryTakeAttackDamage(damage, DamageType.Arts, this);
            lockDuration += DamageTickInterval;

            if (laser != null)
            {
                laser.OnDamageTick();
                laser.SetStage(GetBeamStage()); // The beam shows the power of the next tick
            }

            if (spriteAnim != null)
            {
                // Smoothly accelerate the halo spin as beam power ramps up (from 0.10s down to 0.04s)
                float t = Mathf.Clamp01(lockDuration / (LockStepDuration * 5f));
                spriteAnim.FrameDuration = Mathf.Lerp(0.10f, 0.04f, t);
            }
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

            if (spriteAnim != null)
            {
                spriteAnim.FrameDuration = 0.10f;
            }
        }
    }
}
