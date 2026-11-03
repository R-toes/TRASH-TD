using UnityEngine;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Bulkhead operator — Defender.
    /// Trait: When blocking 3 or more enemies, increase attack and def by 20%.
    /// </summary>
    public class BulkheadOperator : DefenderOperator
    {
        private const int FrenzyBlockThreshold = 3;
        private const float AtkMultiplier = 1.20f;
        private const float DefMultiplier = 1.20f;

        public bool IsFrenzyActive => blockedEnemies != null && blockedEnemies.Count >= FrenzyBlockThreshold;

        public override int CurrentATK => IsFrenzyActive ? Mathf.RoundToInt(currentATK * AtkMultiplier) : base.CurrentATK;
        public override int CurrentDEF => IsFrenzyActive ? Mathf.RoundToInt(currentDEF * DefMultiplier) : base.CurrentDEF;

        private bool wasFrenzyActive;
        private BulkheadSpriteAnimation spriteAnimation;
        private float lastAttackInterval;

        private BulkheadSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponentInChildren<BulkheadSpriteAnimation>(true);
                return spriteAnimation;
            }
        }

        public override float GetAttackInterval()
        {
            lastAttackInterval = base.GetAttackInterval();
            return lastAttackInterval;
        }

        protected override void OnRetreated()
        {
            SpriteAnimation?.CancelAttack();
            base.OnRetreated();
        }

        protected override void Update()
        {
            base.Update();
            StartAttackWindup();

            // Ability sound when the buff switches on (blocked count reaches the threshold).
            bool frenzyActive = isDeployed && IsFrenzyActive;
            if (frenzyActive && !wasFrenzyActive) PlayAbilitySound();
            wasFrenzyActive = frenzyActive;
        }

        private void StartAttackWindup()
        {
            BulkheadSpriteAnimation anim = SpriteAnimation;
            if (anim == null || !anim.HasAttackAnimation) return;
            if (!isDeployed)
            {
                anim.CancelAttack();
                return;
            }

            if (IsCombatPaused)
            {
                if (anim.IsBeforeStrike) anim.CancelAttack();
                return;
            }

            if (anim.IsBeforeStrike)
            {
                EnemyBase windupTarget = FindTarget();
                if (windupTarget == null || !CanAttackTarget(windupTarget)) anim.CancelAttack();
                return;
            }
            if (anim.IsAttacking) return;

            float timeUntilStrike = lastAttackInterval - attackTimer;
            if (timeUntilStrike <= 0f || timeUntilStrike > BulkheadSpriteAnimation.WindupTime) return;

            EnemyBase target = FindTarget();
            if (target != null && CanAttackTarget(target))
                anim.BeginAttack(timeUntilStrike);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            SpriteAnimation?.Strike();
            base.Attack(target);
        }
    }
}
