using UnityEngine;
using TrashTD.Combat;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Scrapper operator — Guard.
    /// Trait: When attacking, has a 20% chance to Stun the target for 2 seconds.
    /// </summary>
    public class ScrapperOperator : GuardOperator
    {
        private const float StunChance = 0.20f;
        private const float StunDuration = 2.0f;

        protected override MeleeStyle MeleeSlashStyle => MeleeStyle.ScrapSlash;
        protected override float MeleeSlashSize => 1.3f;

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            base.Attack(target);

            if (Random.value < StunChance && target != null && !target.IsDead)
            {
                target.ApplyStun(StunDuration);
                PlayAbilitySound();
            }
        }
    }
}

