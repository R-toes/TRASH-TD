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
            var candidates = EnemyManager.Instance.GetEnemiesInCells(rangeCells, data.position);
            if (candidates == null || candidates.Count == 0) return null;

            EnemyBase bestTarget = null;
            int highestDefense = -1;
            int largestCluster = -1;

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null || candidate.IsDead) continue;

                var splashGroup = EnemyManager.Instance.GetEnemiesInRadius(candidate.transform.position, splashRadius, data.position);
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

            PlayAttackSound();

            if (data.chillPerHit > 0f)
            {
                CombatProjectileVisual.Fire(
                    transform.position,
                    target.transform.position,
                    new Color(0.35f, 0.8f, 1f),
                    8.5f,
                    0.2f,
                    0.14f,
                    true,
                    () => ApplyChillImpact(target));
                return;
            }

            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                new Color(0.74f, 0.38f, 1f),
                5.5f,
                0.26f,
                0.2f,
                false,
                () => ApplyArtsImpact(target));
        }

        private void ApplyChillImpact(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            int chillDamage = DamageCalculator.CalculateDamage(currentATK, target.CurrentRES);
            if (!target.TryTakeAttackDamage(chillDamage, DamageType.Arts, this)) return;
            target.ApplyChill(data.chillPerHit, data.chillSlowMultiplier, data.chillSlowDuration,
                data.chillFreezeThreshold, data.chillFreezeDuration);
        }

        private void ApplyArtsImpact(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            int primaryATK = GetArmorAdjustedAttack(currentATK, target);
            target.TryTakeAttackDamage(
                DamageCalculator.CalculateDamage(primaryATK, target.CurrentRES),
                DamageType.Arts,
                this);

            if (EnemyManager.Instance == null || splashRadius <= 0f) return;

            AoeBlastVisual.PlayCircle(target.transform.position, splashRadius, new Color(0.74f, 0.38f, 1f, 0.5f));

            var splashTargets = EnemyManager.Instance.GetEnemiesInRadius(target.transform.position, splashRadius, data.position);
            int secondaryAtk = Mathf.RoundToInt(currentATK * splashDamageRatio);
            for (int i = 0; i < splashTargets.Count; i++)
            {
                var splashTarget = splashTargets[i];
                if (splashTarget == null || splashTarget == target || splashTarget.IsDead) continue;

                int armorAdjustedSplashATK = GetArmorAdjustedAttack(secondaryAtk, splashTarget);
                int splashDmg = DamageCalculator.CalculateDamage(armorAdjustedSplashATK, splashTarget.CurrentRES);
                splashTarget.TryTakeAttackDamage(splashDmg, DamageType.Arts, this);
            }
        }

        private int GetArmorAdjustedAttack(int attackPower, EnemyBase target)
        {
            int armorBonus = Mathf.RoundToInt(target.CurrentDEF * armorDamageBonusRatio);
            return attackPower + armorBonus;
        }
    }
}
