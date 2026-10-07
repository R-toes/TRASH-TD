using UnityEngine;
using TrashTD.Combat;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Vampire-themed guard who restores a portion of the physical damage dealt by each hit.
    /// </summary>
    public class EchosquireOperator : GuardOperator
    {
        private const float LifestealRatio = 0.2f;

        public override bool CanReceiveHealing => false;

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            MeleeSwipeVisual.Play(
                transform.position,
                target.transform.position,
                new Color(0.55f, 0.08f, 0.16f, 1f),
                1.8f);
            PlayAttackSound();

            CombatProjectileVisual.Fire(
                transform.position,
                target.transform.position,
                new Color(0.9f, 0.12f, 0.24f),
                12f,
                0.1f,
                0.05f,
                false,
                () => ApplyLifestealHit(target));
        }

        private void ApplyLifestealHit(EnemyBase target)
        {
            if (target == null || target.IsDead || !isDeployed) return;

            int hpBeforeHit = target.CurrentHP;
            int damage = DamageCalculator.CalculateDamage(currentATK, target.CurrentDEF);
            if (!target.TryTakeAttackDamage(damage, Data.damageType)) return;

            int actualDamage = Mathf.Min(damage, hpBeforeHit);
            int healing = Mathf.FloorToInt(actualDamage * LifestealRatio);
            HealSelf(healing);
        }
    }
}
