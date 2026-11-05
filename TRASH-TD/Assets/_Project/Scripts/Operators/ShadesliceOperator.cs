using UnityEngine;
using TrashTD.Combat;
using TrashTD.Core.Grid;
using TrashTD.Data;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Shadeslice — 3-Star Guard.
    /// Ability: Deals bonus damage while isolated (no allied operator in the diamond pattern).
    /// When not attacked for 5 seconds restore 2.5% of max hp every second.
    /// </summary>
    public class ShadesliceOperator : GuardOperator
    {
        public const float OutOfCombatDuration = 5.0f;
        public const float RegenIntervalSeconds = 1.0f;
        public const float RegenPercentOfMaxHP = 0.025f;
        private const float AttackEffectYOffset = -2f / 32f;

        private float timeSinceLastAttacked;
        private float regenTimer = 1.0f;
        private ShadesliceSpriteAnimation spriteAnimation;

        public float TimeSinceLastAttacked => timeSinceLastAttacked;
        public bool IsRegenerating => isDeployed && !IsCombatPaused && timeSinceLastAttacked >= OutOfCombatDuration && currentHP < maxHP;

        private ShadesliceSpriteAnimation SpriteAnimation
        {
            get
            {
                if (spriteAnimation == null) spriteAnimation = GetComponent<ShadesliceSpriteAnimation>();
                return spriteAnimation;
            }
        }

        private void UpdateAttackWindup()
        {
            ShadesliceSpriteAnimation anim = SpriteAnimation;
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

            EnemyBase target = FindTarget();
            if (target == null || !CanAttackTarget(target)) return;

            float attackInterval = GetAttackInterval();
            float timeUntilImpact = attackInterval - attackTimer;
            if (timeUntilImpact > ShadesliceSpriteAnimation.WindupSeconds) return;

            if (timeUntilImpact <= 0f)
            {
                timeUntilImpact = ShadesliceSpriteAnimation.WindupSeconds;
                attackTimer = Mathf.Max(0f, attackInterval - timeUntilImpact);
            }

            anim.BeginAttack(timeUntilImpact);
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed || isStunned || isBound) return;

            SpriteAnimation?.BeginImpact();
            HasFoughtInWave = true;
            DeployedDuringCurrentPreparation = false;
            PlayAttackSound();

            bool isIsolated = data != null && data.bonusWhenIsolated && !HasAdjacentOperator();
            int attackPower = isIsolated
                ? Mathf.RoundToInt(currentATK * Mathf.Max(1f, data.isolatedAttackMultiplier))
                : CurrentATK;
            Vector3 projectileStart = transform.position + Vector3.up * AttackEffectYOffset;
            Vector3 projectileEnd = GetFixedAttackEffectEnd(projectileStart);

            if (isIsolated)
            {
                MeleeSwipeVisual.PlayStab(
                    projectileStart,
                    projectileEnd,
                    new Color(0.12f, 0.008f, 0.015f, 1f));
            }
            else
            {
                MeleeSwipeVisual.Play(
                    projectileStart,
                    projectileEnd,
                    new Color(1f, 0.78f, 0.3f, 1f),
                    MeleeSlashSize,
                    MeleeSlashStyle);
            }

            CombatProjectileVisual.Fire(
                projectileStart,
                projectileEnd,
                isIsolated ? new Color(1f, 0.2f, 0.15f) : new Color(0.7f, 0.35f, 0.9f),
                12f,
                0.1f,
                0.05f,
                false,
                () => ApplyAttackDamage(target, attackPower));
        }

        private Vector3 GetFixedAttackEffectEnd(Vector3 start)
        {
            Vector3 direction = facing == OperatorFacing.Right ? Vector3.right : Vector3.left;
            GridManager gridManager = FindAnyObjectByType<GridManager>();
            float range = gridManager != null ? gridManager.CellSize : 1f;
            return start + direction * range;
        }

        private void ApplyAttackDamage(EnemyBase target, int attackPower)
        {
            if (target == null || target.IsDead) return;

            int damage = DamageCalculator.CalculateDamage(attackPower, GetTargetMitigation(target));
            if (target.TryTakeAttackDamage(damage, data.damageType, this))
                PlayImpactSound();
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            SpriteAnimation?.CancelAttack();
            ResetNotAttackedTimer();
        }

        protected override void OnRetreated()
        {
            SpriteAnimation?.CancelAttack();
            base.OnRetreated();
            ResetNotAttackedTimer();
        }

        public void ResetNotAttackedTimer()
        {
            timeSinceLastAttacked = 0f;
            regenTimer = RegenIntervalSeconds;
        }

        public void SetTimeSinceLastAttackedForTesting(float time)
        {
            timeSinceLastAttacked = time;
        }

        public override void TakeDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            base.TakeDamage(rawATK, damageType, attacker);
            ResetNotAttackedTimer();
        }

        public override bool TryTakeAttackDamage(int rawATK, DamageType damageType, EnemyBase attacker = null)
        {
            if (!CanBeTargetedByEnemy(attacker)) return false;
            return base.TryTakeAttackDamage(rawATK, damageType, attacker);
        }

        public override bool CanBeTargetedByEnemy(EnemyBase attacker)
        {
            return IsTargetable && CanBeAttackedBy(attacker);
        }

        public override bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false, bool canDodge = false)
        {
            bool dealt = base.TakeTrueDamage(damage, ignoreShieldNegation, canDodge);
            ResetNotAttackedTimer();
            return dealt;
        }

        private bool CanBeAttackedBy(EnemyBase attacker)
        {
            if (attacker == null) return true;
            if (attacker.Data != null && attacker.Data.archetype == EnemyArchetype.Tank) return true;

            return blockedEnemies.Contains(attacker) &&
                   attacker.IsBlocked &&
                   attacker.BlockingOperator == this;
        }

        protected override void Update()
        {
            UpdateAttackWindup();
            base.Update();

            if (!isDeployed || IsCombatPaused || currentHP <= 0) return;

            UpdateOutOfCombatRegen(Time.deltaTime);
        }

        public void UpdateOutOfCombatRegen(float dt)
        {
            bool wasOutOfCombat = timeSinceLastAttacked >= OutOfCombatDuration;
            timeSinceLastAttacked += dt;
            if (!wasOutOfCombat && timeSinceLastAttacked >= OutOfCombatDuration && currentHP < maxHP)
                PlayAbilitySound("Regen");
            if (timeSinceLastAttacked >= OutOfCombatDuration)
            {
                regenTimer += dt;
                while (regenTimer >= RegenIntervalSeconds)
                {
                    regenTimer -= RegenIntervalSeconds;
                    if (currentHP > 0 && currentHP < maxHP)
                    {
                        int healAmount = Mathf.Max(1, Mathf.RoundToInt(maxHP * RegenPercentOfMaxHP));
                        HealSelf(healAmount);
                    }
                }
            }
            else
            {
                regenTimer = RegenIntervalSeconds;
            }
        }

        public bool IsInIsolationPattern(Vector2Int offset) => IsOperatorInIsolationRange(offset);

        public override Vector2Int[] GetTalentRangePattern() => OperatorData.ShadesliceTalentPattern;

        protected override bool IsOperatorInIsolationRange(Vector2Int offset)
        {
            int manhattanDistance = Mathf.Abs(offset.x) + Mathf.Abs(offset.y);
            return manhattanDistance <= 2 && (offset.x != 0 || offset.y != 0);
        }
    }
}
