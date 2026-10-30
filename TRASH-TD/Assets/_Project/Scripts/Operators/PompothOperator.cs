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
    /// Passive: +10% attack speed per enemy in range, capped at +100% (double speed) with 10 or more enemies.
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

            // Runs every frame (GetAttackInterval), so count without building a list
            return EnemyManager.Instance.CountEnemiesInCells(cachedRangeCells, data.position);
        }

        public override float GetAttackInterval()
        {
            // Base interval from SniperOperator (includes any global modifiers)
            float baseInterval = base.GetAttackInterval();
            float interval = baseInterval * IntervalMultiplier;

            // Passive bonus based on enemies in range: +10% attack speed per enemy, capped at +100%
            int enemyCount = GetEnemyCountInRange();
            float bonus = Mathf.Clamp01(enemyCount * 0.10f);
            // Attack speed scales by (1 + bonus), so the interval divides by it: at most half the interval
            interval /= 1f + bonus;
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
