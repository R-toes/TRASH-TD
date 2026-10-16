using UnityEngine;
using TrashTD.Combat;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Chillpath operator — Caster.
    /// Trait: Decrease attack interval after applying freeze.
    /// Ability: Every 4th attack, attacks an additional target in range (or the current target if no additional target is in range).
    /// </summary>
    public class ChillpathOperator : CasterOperator
    {
        public const int AttacksPerBonusTarget = 4;
        private const float FreezeHasteDuration = 3.5f;
        private const float AttackIntervalMultiplier = 0.75f; // 25% faster attack rate

        private float hasteTimer;
        private int attackCount;

        public bool IsHasteActive => hasteTimer > 0f;
        public int AttackCount => attackCount;
        public int BonusAttackCount { get; private set; }
        public EnemyBase LastBonusTarget { get; private set; }

        protected override void Update()
        {
            if (hasteTimer > 0f)
            {
                hasteTimer -= Time.deltaTime;
            }
            base.Update();
        }

        public override void Attack(EnemyBase target)
        {
            if (target == null || !isDeployed) return;

            attackCount++;
            bool isBonusAttack = (attackCount >= AttacksPerBonusTarget);
            if (isBonusAttack)
            {
                attackCount = 0;
            }

            base.Attack(target);

            if (isBonusAttack)
            {
                EnemyBase additionalTarget = FindAdditionalTarget(target);
                EnemyBase extraTarget = additionalTarget != null ? additionalTarget : target;

                BonusAttackCount++;
                LastBonusTarget = extraTarget;

                Vector3 visualOffset = (extraTarget == target)
                    ? new Vector3(0.12f, 0.12f, 0f)
                    : Vector3.zero;

                FireAttackProjectile(extraTarget, visualOffset);
            }
        }

        protected override void OnFreezeApplied(EnemyBase target)
        {
            base.OnFreezeApplied(target);
            bool wasActive = IsHasteActive;
            hasteTimer = FreezeHasteDuration;
            if (!wasActive)
            {
                FloatingCombatNumber.ShowText(transform.position + Vector3.up * 0.6f, "HASTE!", Color.cyan);
            }
        }

        public override float GetAttackInterval()
        {
            float baseInterval = base.GetAttackInterval();
            return IsHasteActive ? baseInterval * AttackIntervalMultiplier : baseInterval;
        }

        protected override void OnDeployed()
        {
            base.OnDeployed();
            attackCount = 0;
            BonusAttackCount = 0;
            LastBonusTarget = null;
            hasteTimer = 0f;
        }

        protected override void OnRetreated()
        {
            hasteTimer = 0f;
            attackCount = 0;
            BonusAttackCount = 0;
            LastBonusTarget = null;
            base.OnRetreated();
        }
    }
}

