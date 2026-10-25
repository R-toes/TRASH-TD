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
    /// Long-range explosive sniper. It can target only cells two to seven tiles
    /// in front of it; its rocket blasts a circle around the target, a bit bigger than a caster's splash.
    /// </summary>
    public sealed class BasurocketOperator : SniperOperator
    {
        /// <summary>Blast radius in tiles (caster splash is 1.5).</summary>
        public const float BlastRadiusTiles = 1.75f;
        private const float BlastDamageMultiplier = 1f;
        private static readonly Color BlastColor = new Color(1f, 0.62f, 0.12f); // Yellow-orange: never the Eradicator's crimson

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || EnemyManager.Instance == null)
                return;

            GridManager gridManager = FindAnyObjectByType<GridManager>();
            if (gridManager == null)
                return;

            float radius = BlastRadiusTiles * gridManager.CellSize;
            Vector3 aimPosition = target.transform.position;
            if (EnemyManager.Instance.GetEnemiesInRadius(aimPosition, radius, data.position).Count == 0)
                return;

            PlayAttackSound();
            CombatProjectileVisual.Fire(
                transform.position,
                target,
                BlastColor,
                14f,
                0.18f,
                0.1f,
                true,
                // The rocket homes on its target, so it blows up where the target is when it lands.
                () => ApplyBlastDamage(target != null && !target.IsDead ? target.transform.position : aimPosition, radius),
                ProjectileStyle.Rocket);
        }

        private void ApplyBlastDamage(Vector3 center, float radius)
        {
            if (EnemyManager.Instance == null)
                return;

            AoeBlastVisual.PlayCircle(center, radius, BlastColor, AoeStyle.JunkRocket);
            PlayImpactSound(); // the explosion, even if the target moved out of the blast

            List<EnemyBase> blastTargets = EnemyManager.Instance.GetEnemiesInRadius(center, radius, data.position);
            HashSet<EnemyBase> uniqueTargets = new HashSet<EnemyBase>();
            for (int i = 0; i < blastTargets.Count; i++)
            {
                EnemyBase blastTarget = blastTargets[i];
                if (blastTarget == null || blastTarget.IsDead || !uniqueTargets.Add(blastTarget))
                    continue;

                int damage = Mathf.RoundToInt(currentATK * BlastDamageMultiplier);
                damage = DamageCalculator.CalculateDamage(damage, blastTarget.CurrentDEF);
                blastTarget.TryTakeAttackDamage(damage, DamageType.Physical, this);
            }
        }
    }
}
