using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Scrapper operator — Guard.
    /// Trait: When attacking, has a 20% chance to Stun the target for 2 seconds.
    /// Uses 64x64 sledgehammer attack animation with bottom-right 32x32 pivot offset (0.75, 0.25)
    /// to keep Scrapper centered on the deployed tile while the hammer swings through the forward tiles.
    /// Direct melee damage is applied instantly on swing without any projectile.
    /// </summary>
    public class ScrapperOperator : GuardOperator
    {
        private const float StunChance = 0.20f;
        private const float StunDuration = 2.0f;

        protected override MeleeStyle MeleeSlashStyle => MeleeStyle.ScrapSlash;
        protected override float MeleeSlashSize => 1.3f;

        private ScrapperSpriteAnimation spriteAnimation;

        public ScrapperSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null)
                {
                    spriteAnimation = GetComponentInChildren<ScrapperSpriteAnimation>(true);
                    if (spriteAnimation == null)
                    {
                        spriteAnimation = gameObject.AddComponent<ScrapperSpriteAnimation>();
                    }
                }
                return spriteAnimation;
            }
        }

        private void Awake()
        {
            // If the GameObject has the generic OperatorSpriteAnimation, disable it so ScrapperSpriteAnimation controls playback
            var genericAnim = GetComponent<OperatorSpriteAnimation>();
            if (genericAnim != null && genericAnim.enabled)
            {
                genericAnim.enabled = false;
            }

            EnsureScrapperAnimation();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            EnsureScrapperAnimation();
        }

        protected override void OnRetreated()
        {
            SpriteAnimation?.CancelAttack();
            base.OnRetreated();
        }

        private void EnsureScrapperAnimation()
        {
            var anim = SpriteAnimation;
            if (anim != null && !anim.enabled)
            {
                anim.enabled = true;
            }
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound) return;

            HasFoughtInWave = true;
            DeployedDuringCurrentPreparation = false;

            // Trigger the 64x64 sledgehammer attack animation
            SpriteAnimation?.PlayAttack();

            // Direct melee strike audio and visual swipe (no projectile!)
            PlayAttackSound();
            MeleeSwipeVisual.Play(transform.position, target.VisualPosition, new Color(1f, 0.78f, 0.3f, 1f), MeleeSlashSize, MeleeSlashStyle);

            // Deal direct attack damage without firing any projectile
            int damage = Combat.DamageCalculator.CalculateDamage(CurrentATK, GetTargetMitigation(target));
            if (target.TryTakeAttackDamage(damage, data != null ? data.damageType : DamageType.Physical, this))
            {
                PlayImpactSound();
            }

            // 20% Stun trait
            if (Random.value < StunChance && target != null && !target.IsDead)
            {
                target.ApplyStun(StunDuration);
                PlayAbilitySound();
            }
        }
    }
}
