using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Deadeye operator — Sniper.
    /// Trait: Increases damage by 20% against flying enemies, but delays attack (increases attack interval by 20%)
    /// when preparing to attack air units. Returns to original attack interval when no aerial enemies are in range.
    /// </summary>
    public class DeadeyeOperator : SniperOperator
    {
        private const float AirDamageMultiplier = 1.20f;
        private const float AirIntervalMultiplier = 1.20f;

        private GridManager gridManager;
        private GridCell[] cachedRangeCells;

        private GridManager GetGridManager()
        {
            if (gridManager == null)
            {
                gridManager = FindFirstObjectByType<GridManager>();
            }
            return gridManager;
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            RefreshRangeCells();
        }

        protected override void OnRetreated()
        {
            cachedRangeCells = null;
            base.OnRetreated();
        }

        public override void ApplyFacingVisuals()
        {
            base.ApplyFacingVisuals();
            RefreshRangeCells();
        }

        private void RefreshRangeCells()
        {
            var gm = GetGridManager();
            if (gm != null && deployedCell != null && data != null && data.rangePattern != null)
            {
                cachedRangeCells = gm.GetCellsInRange(deployedCell.GridPosition, data.rangePattern, Facing);
            }
            else
            {
                cachedRangeCells = null;
            }
        }

        public bool HasAirTargetInRange()
        {
            if (!isDeployed || EnemyManager.Instance == null || data == null)
                return false;

            if (cachedRangeCells == null || cachedRangeCells.Length == 0)
            {
                RefreshRangeCells();
                if (cachedRangeCells == null || cachedRangeCells.Length == 0)
                    return false;
            }

            var candidates = EnemyManager.Instance.GetEnemiesInCells(cachedRangeCells, data.position);
            if (candidates == null || candidates.Count == 0) return false;

            for (int i = 0; i < candidates.Count; i++)
            {
                var enemy = candidates[i];
                if (enemy != null && !enemy.IsDead && enemy.MovementType == EnemyMovementType.Air)
                {
                    return true;
                }
            }

            return false;
        }

        public override float GetAttackInterval()
        {
            float baseInterval = base.GetAttackInterval();
            return HasAirTargetInRange() ? baseInterval * AirIntervalMultiplier : baseInterval;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            bool isAir = target.MovementType == EnemyMovementType.Air;

            PlayAttackSound();

            Color projectileColor = isAir ? new Color(1f, 0.5f, 0.1f) : new Color(1f, 0.82f, 0.32f);

            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                projectileColor,
                14f,
                0.16f,
                0.09f,
                false,
                () => ApplyDeadeyeDamage(target, isAir));
        }

        private void ApplyDeadeyeDamage(EnemyBase target, bool isAir)
        {
            if (target == null || target.IsDead) return;

            int atk = isAir ? Mathf.RoundToInt(CurrentATK * AirDamageMultiplier) : CurrentATK;
            int damage = DamageCalculator.CalculateDamage(atk, GetTargetMitigation(target));
            target.TryTakeAttackDamage(damage, data.damageType, this);
        }
    }
}
