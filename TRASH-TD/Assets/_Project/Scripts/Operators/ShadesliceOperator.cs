using UnityEngine;
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

        private float timeSinceLastAttacked;
        private float regenTimer = 1.0f;

        public float TimeSinceLastAttacked => timeSinceLastAttacked;
        public bool IsRegenerating => isDeployed && !IsCombatPaused && timeSinceLastAttacked >= OutOfCombatDuration && currentHP < maxHP;

        protected override void OnDeployed()
        {
            base.OnDeployed();
            ResetNotAttackedTimer();
        }

        protected override void OnRetreated()
        {
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

        public override bool TakeTrueDamage(int damage, bool ignoreShieldNegation = false)
        {
            bool dealt = base.TakeTrueDamage(damage, ignoreShieldNegation);
            ResetNotAttackedTimer();
            return dealt;
        }

        protected override void Update()
        {
            base.Update();

            if (!isDeployed || IsCombatPaused || currentHP <= 0) return;

            UpdateOutOfCombatRegen(Time.deltaTime);
        }

        public void UpdateOutOfCombatRegen(float dt)
        {
            timeSinceLastAttacked += dt;
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

