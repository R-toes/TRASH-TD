using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Pompoth operator — Sniper variant with rapid fire.
    /// Damage per shot is reduced, but attack interval is faster, keeping DPS equal to Deadeye.
    /// Passive: attack interval shortens by 10% per enemy in range, with no upper limit (10+ enemies reach 100%).
    /// </summary>
    public class PompothOperator : SniperOperator
    {
        private const float DamageMultiplier = 0.8f; // 80% of base damage per shot
        private const float IntervalMultiplier = 0.8f; // 80% of base interval (faster)

        private GridManager gridManager;
        private GridCell[] cachedRangeCells;

        private GridManager GetGridManager()
        {
            if (gridManager == null)
            {
                gridManager = FindAnyObjectByType<GridManager>();
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

        private int GetEnemyCountInRange()
        {
            if (!isDeployed || EnemyManager.Instance == null || data == null) return 0;
            if (cachedRangeCells == null || cachedRangeCells.Length == 0) RefreshRangeCells();
            if (cachedRangeCells == null) return 0;

            var candidates = EnemyManager.Instance.GetEnemiesInCells(cachedRangeCells, data.position);
            if (candidates == null) return 0;

            int count = 0;
            foreach (var enemy in candidates)
            {
                if (enemy != null && !enemy.IsDead) count++;
            }
            return count;
        }

        public override float GetAttackInterval()
        {
            // Base interval from SniperOperator (includes any global modifiers)
            float baseInterval = base.GetAttackInterval();
            float interval = baseInterval * IntervalMultiplier;

            // Passive bonus based on enemies in range: 10% per enemy, clamped only at 100%
            int enemyCount = GetEnemyCountInRange();
            float bonus = Mathf.Clamp01(enemyCount * 0.10f);
            // Reduce interval by bonus (i.e., faster attacks)
            interval *= (1f - bonus);
            return interval;
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            PlayAttackSound();
            CombatProjectileVisual.Fire(
                ProjectileOrigin,
                target,
                new Color(0.9f, 0.2f, 0.9f), // distinctive purple color for Pompoth
                14f,
                0.12f,
                0.07f,
                false,
                () => ApplyPompothDamage(target),
                ProjectileStyle.Bullet);
        }

        private void ApplyPompothDamage(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            int atk = Mathf.RoundToInt(CurrentATK * DamageMultiplier);
            int damage = DamageCalculator.CalculateDamage(atk, GetTargetMitigation(target));
            target.TryTakeAttackDamage(damage, data.damageType, this);
        }
    }
}
