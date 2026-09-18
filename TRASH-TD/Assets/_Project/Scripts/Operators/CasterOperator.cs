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
    /// Targets the enemy whose cluster hits the most enemies (maximizing AoE output).
    /// </summary>
    public class CasterOperator : OperatorBase
    {
        [Header("Caster Settings")]
        [Tooltip("Splash radius in world units for AoE attacks")]
        [SerializeField] private float splashRadius = 1.5f;

        [Tooltip("Percentage of ATK dealt to secondary targets caught in the splash")]
        [Range(0.1f, 1f)]
        [SerializeField] private float splashDamageRatio = 0.75f;

        protected override EnemyBase FindTarget()
        {
            if (EnemyManager.Instance == null || deployedCell == null || data == null || data.rangePattern == null)
                return null;

            var gridManager = FindFirstObjectByType<GridManager>();
            if (gridManager == null) return null;

            var rangeCells = gridManager.GetCellsInRange(deployedCell.GridPosition, data.rangePattern);
            var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells);
            if (candidates == null || candidates.Count == 0) return null;

            EnemyBase bestTarget = null;
            int maxClusterCount = -1;

            // Find candidate that has the most nearby enemies within splashRadius
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || candidate.IsDead) continue;

                var splashGroup = EnemyManager.Instance.GetEnemiesInRadius(candidate.transform.position, splashRadius);
                int count = splashGroup != null ? splashGroup.Count : 1;

                if (count > maxClusterCount)
                {
                    maxClusterCount = count;
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

            // Primary target damage
            int primaryDmg = DamageCalculator.CalculateDamage(currentATK, target.CurrentRES);
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
                        int splashDmg = DamageCalculator.CalculateDamage(secondaryAtk, splashTarget.CurrentRES);
                        splashTarget.TakeDamage(splashDmg, DamageType.Arts);
                    }
                }
            }
        }
    }
}
