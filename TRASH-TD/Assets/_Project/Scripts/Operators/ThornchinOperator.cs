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
            if (IsCombatPaused) return;

            base.TakeDamage(rawATK, damageType, attacker);

            // Retaliate against the attacker when attacked by an enemy.
            // Environmental damage (acid tiles, acid rain, etc.) has no attacker and will not trigger retaliation.
            if (attacker != null && !attacker.IsDead)
            {
                Retaliate(attacker);
            }
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            MeleeSwipeVisual.Play(transform.position, target.VisualPosition, SpineColor);
            base.Attack(target);
        }

        private void Retaliate(EnemyBase target)
        {
            if (target == null || target.IsDead) return;

            Vector3 startPos = transform.position;
            Vector3 targetPos = target.VisualPosition;
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
                    target,
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

            PlayAbilitySound(SfxId.GameplayMeleeAttack);

            int damage = DamageCalculator.CalculateDamage(currentATK, target.CurrentDEF);
            target.TryTakeAttackDamage(damage, data != null ? data.damageType : DamageType.Physical, this, canHitAir: true);
        }
    }
}
