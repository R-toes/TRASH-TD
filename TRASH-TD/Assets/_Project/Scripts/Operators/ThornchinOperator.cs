using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Thornchin — 2-Star Defender (3 Block).
    /// Ability: Bristling Spines — Whenever Thornchin is attacked, it retaliates against
    /// the attacker with sharp urchin spines, dealing Physical damage equal to Thornchin's ATK.
    /// </summary>
    public class ThornchinOperator : DefenderOperator
    {
        private static readonly Color SpineColor = new Color(0.75f, 0.2f, 0.85f, 1f);

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            int hpBefore = currentHP;
            base.TakeDamage(rawATK, damageType, attacker);
            int hpAfter = currentHP;

            // Retaliate against the attacker when attacked
            if (attacker != null && !attacker.IsDead)
            {
                Retaliate(attacker);
            }
            else if (attacker == null && hpBefore > hpAfter && blockedEnemies.Count > 0)
            {
                // Fallback for damage sources without an explicit attacker reference
                EnemyBase primaryBlocked = blockedEnemies[0];
                if (primaryBlocked != null && !primaryBlocked.IsDead)
                {
                    Retaliate(primaryBlocked);
                }
            }
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            MeleeSwipeVisual.Play(transform.position, target.transform.position, SpineColor);
            base.Attack(target);
        }

        private void Retaliate(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            Vector3 startPos = transform.position;
            Vector3 targetPos = target.transform.position;
            float distance = Vector3.Distance(startPos, targetPos);

            if (distance <= 1.5f)
            {
                // Melee attacker retaliation
                MeleeSwipeVisual.Play(startPos, targetPos, SpineColor);
                ApplyRetaliationDamage(target);
            }
            else
            {
                // Ranged attacker retaliation: shoot an urchin quill projectile
                CombatProjectileVisual.Fire(
                    startPos,
                    targetPos,
                    SpineColor,
                    14f,
                    0.09f,
                    0.04f,
                    false,
                    () =>
                    {
                        if (target != null && !target.IsDead)
                        {
                            ApplyRetaliationDamage(target);
                        }
                    });
            }
        }

        private void ApplyRetaliationDamage(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            AudioManager.Instance?.PlaySfx(SfxId.GameplayMeleeAttack);

            int damage = DamageCalculator.CalculateDamage(currentATK, target.CurrentDEF);
            target.TryTakeAttackDamage(damage, data != null ? data.damageType : DamageType.Physical, this);
        }
    }
}
