using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// A front-line guard who channels a chi paw strike and knocks enemies back more often as he is upgraded.
    /// </summary>
    public sealed class QiFuOperator : GuardOperator
    {
        private int hitsSinceKnockback;
        private QiFuSpriteAnimation spriteAnimation;
        private float lastAttackInterval;

        public int HitsPerKnockback =>
            currentRarity >= OperatorRarity.Star3 ? 2 :
            currentRarity >= OperatorRarity.Star2 ? 3 : 4;

        private QiFuSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponentInChildren<QiFuSpriteAnimation>(true);
                return spriteAnimation;
            }
        }

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            hitsSinceKnockback = 0;
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
            UpdateAttackWindup();
        }

        private void UpdateAttackWindup()
        {
            QiFuSpriteAnimation anim = SpriteAnimation;
            if (anim == null || !anim.HasAttackAnimation) return;

            if (!isDeployed)
            {
                anim.CancelAttack();
                return;
            }

            if (IsCombatPaused || isStunned || isBound)
            {
                anim.CancelWindup();
                return;
            }

            if (anim.IsWindingUp)
            {
                EnemyBase windupTarget = FindTarget();
                if (windupTarget == null || !CanAttackTarget(windupTarget))
                    anim.CancelWindup();
                return;
            }

            if (anim.IsRecovering) return;

            float timeUntilStrike = lastAttackInterval - attackTimer;
            if (timeUntilStrike <= 0f || timeUntilStrike > QiFuSpriteAnimation.WindupDuration)
                return;

            EnemyBase target = FindTarget();
            if (target != null && CanAttackTarget(target))
                anim.BeginAttack(timeUntilStrike);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed)
                return;

            QiFuSpriteAnimation anim = SpriteAnimation;
            Vector3 attackStart = transform.position;
            if (anim != null)
            {
                anim.Strike();
                attackStart = anim.GetStrikePointWorldPosition();
            }

            PlayAttackSound();
            QiFuChiAttackVisual.Fire(
                attackStart,
                target,
                () => ApplyChiHit(target));
        }

        private bool ApplyChiHit(EnemyBase target)
        {
            if (target == null || target.IsDead || !isDeployed)
                return false;

            int damage = DamageCalculator.CalculateDamage(currentATK, GetTargetMitigation(target));
            if (!target.TryTakeAttackDamage(damage, Data.damageType, this))
                return false;

            hitsSinceKnockback++;
            if (hitsSinceKnockback < HitsPerKnockback)
                return false;

            hitsSinceKnockback = 0;
            bool knockedBack = !target.IsDead && target.PushBack(1, QiFuChiAttackVisual.PushbackDuration);
            if (knockedBack) PlayAbilitySound();
            return knockedBack;
        }
    }
}
