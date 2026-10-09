using UnityEngine;
using TrashTD.Combat;
using TrashTD.Enemies;

namespace TrashTD.Operators
{
    /// <summary>
    /// Chillpath operator — Caster.
    /// Trait: Decrease attack interval after applying freeze.
    /// </summary>
    public class ChillpathOperator : CasterOperator
    {
        private const float FreezeHasteDuration = 3.5f;
        private const float AttackIntervalMultiplier = 0.75f; // 25% faster attack rate

        private float hasteTimer;

        public bool IsHasteActive => hasteTimer > 0f;

        protected override void Update()
        {
            if (hasteTimer > 0f)
            {
                hasteTimer -= Time.deltaTime;
            }
            base.Update();
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

        protected override void OnRetreated()
        {
            hasteTimer = 0f;
            base.OnRetreated();
        }
    }
}

