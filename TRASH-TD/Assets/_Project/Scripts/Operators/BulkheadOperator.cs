using UnityEngine;
using TrashTD.Combat;
using TrashTD.Data;
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
        public const int HitsPerShockwave = 8;
        public const float ShockwaveStunDuration = 1.5f;
        private const int UpgradedBlockBonus = 2;
        private const float ShockwaveRadius = 0.9f;
        private static readonly Color ShockwaveColor = new Color(0.65f, 0.65f, 0.65f, 0.8f);

        public bool IsFrenzyActive => blockedEnemies != null && blockedEnemies.Count >= FrenzyBlockThreshold;
        public int HitsSinceShockwave { get; private set; }
        public bool HasShockwaveUpgrade => currentRarity >= OperatorRarity.Star2;
        public bool HasIncreasedBlockUpgrade => currentRarity >= OperatorRarity.Star3;

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

        public override void Initialize(OperatorData operatorData, OperatorRarity rarity, OperatorFacing operatorFacing = OperatorFacing.Right)
        {
            base.Initialize(operatorData, rarity, operatorFacing);
            HitsSinceShockwave = 0;
        }

        public override int GetBlockCount()
        {
            int blockCount = base.GetBlockCount();
            return blockCount > 0 && HasIncreasedBlockUpgrade ? blockCount + UpgradedBlockBonus : blockCount;
        }

        public override float GetAttackInterval()
        {
            lastAttackInterval = base.GetAttackInterval();
            return lastAttackInterval;
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            bool countHit = HasShockwaveUpgrade && isDeployed && attacker != null &&
                !IsCombatPaused && currentHP > 0 && rawATK > 0;
            base.TakeDamage(rawATK, damageType, attacker);

            if (!countHit || currentHP <= 0) return;

            HitsSinceShockwave++;
            if (HitsSinceShockwave >= HitsPerShockwave)
            {
                HitsSinceShockwave = 0;
                ReleaseShockwave();
            }
        }

        private void ReleaseShockwave()
        {
            AoeBlastVisual.PlayCircle(transform.position, ShockwaveRadius, ShockwaveColor, AoeStyle.Pulse);
            PlayAbilitySound();

            for (int i = blockedEnemies.Count - 1; i >= 0; i--)
            {
                EnemyBase enemy = blockedEnemies[i];
                if (enemy != null && !enemy.IsDead && enemy.IsBlocked && enemy.BlockingOperator == this)
                    enemy.ApplyStun(ShockwaveStunDuration);
            }
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
