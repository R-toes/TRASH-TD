using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Caster class — Ranged AoE Magic (GDD 1.3).
    /// Deals Arts damage that bypasses DEF and hits RES instead.
    /// Prioritizes high-defense enemies, using nearby enemy count to break ties.
    /// </summary>
    public class CasterOperator : OperatorBase
    {
        [Header("Caster Settings")]
        [Tooltip("Splash radius in world units for AoE attacks")]
        [SerializeField] private float splashRadius = 1.5f;

        [Tooltip("Percentage of ATK dealt to secondary targets caught in the splash")]
        [Range(0.1f, 1f)]
        [SerializeField] private float splashDamageRatio = 0.75f;

        [Header("Armor Counter")]
        [Tooltip("Bonus Arts attack power per point of target DEF")]
        [SerializeField, Range(0f, 1f)] private float armorDamageBonusRatio = 0.5f;

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
            int highestDefense = -1;
            int largestCluster = -1;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || candidate.IsDead) continue;

                var splashGroup = EnemyManager.Instance.GetEnemiesInRadius(candidate.transform.position, splashRadius);
                int count = splashGroup != null ? splashGroup.Count : 1;

                if (candidate.CurrentDEF > highestDefense ||
                    (candidate.CurrentDEF == highestDefense && count > largestCluster))
                {
                    highestDefense = candidate.CurrentDEF;
                    largestCluster = count;
                    bestTarget = candidate;
                }
            }

            return bestTarget;
        }

        /// <summary>
        /// Deals Arts damage to main target and splash Arts damage to all surrounding enemies.
        /// Arts damage formula: max(ATK - RES, ATK * 0.05).
        /// </summary>
        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            if (data.chillPerHit > 0f)
            {
                CombatProjectileVisual.Fire(
                    transform.position,
                    target.transform.position,
                    new Color(0.35f, 0.8f, 1f),
                    8.5f,
                    0.2f,
                    0.14f,
                    true);

                int chillDamage = DamageCalculator.CalculateDamage(currentATK, target.CurrentRES);
                target.TakeDamage(chillDamage, DamageType.Arts);
                target.ApplyChill(
                    data.chillPerHit,
                    data.chillSlowMultiplier,
                    data.chillSlowDuration,
                    data.chillFreezeThreshold,
                    data.chillFreezeDuration);
                return;
            }

            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                new Color(0.74f, 0.38f, 1f),
                5.5f,
                0.26f,
                0.2f);

            // Primary target damage
            int primaryATK = GetArmorAdjustedAttack(currentATK, target);
            int primaryDmg = DamageCalculator.CalculateDamage(primaryATK, target.CurrentRES);
            target.TakeDamage(primaryDmg, DamageType.Arts);

            // Splash AoE to nearby enemies
            if (EnemyManager.Instance != null && splashRadius > 0f)
            {
                var splashTargets = EnemyManager.Instance.GetEnemiesInRadius(target.transform.position, splashRadius);
                int secondaryAtk = Mathf.RoundToInt(currentATK * splashDamageRatio);

                for (int i = 0; i < splashTargets.Count; i++)
                {
                    var splashTarget = splashTargets[i];
                    if (splashTarget != null && splashTarget != target && !splashTarget.IsDead)
                    {
                        int armorAdjustedSplashATK = GetArmorAdjustedAttack(secondaryAtk, splashTarget);
                        int splashDmg = DamageCalculator.CalculateDamage(armorAdjustedSplashATK, splashTarget.CurrentRES);
                        splashTarget.TakeDamage(splashDmg, DamageType.Arts);
                    }
                }
            }
        }

        private int GetArmorAdjustedAttack(int attackPower, EnemyBase target)
        {
            int armorBonus = Mathf.RoundToInt(target.CurrentDEF * armorDamageBonusRatio);
            return attackPower + armorBonus;
        }
    }
}
