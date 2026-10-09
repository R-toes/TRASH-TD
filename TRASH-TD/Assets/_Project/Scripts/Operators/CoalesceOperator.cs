using UnityEngine;
using TrashTD.Combat;

namespace TrashTD.Operators
{
    /// <summary>
    /// Coalesce operator — Medic.
    /// Trait: Healing an operator with HP below 30% has a 20% chance to grant an extra heal.
    /// </summary>
    public class CoalesceOperator : MedicOperator
    {
        private const float LowHpThreshold = 0.30f;
        private const float ExtraHealChance = 0.20f;

        public override void Heal(OperatorBase target)
        {
            if (target == null) return;

            bool wasBelowThirtyPercent = target.MaxHP > 0 && ((float)target.CurrentHP / target.MaxHP) < LowHpThreshold;

            base.Heal(target);

            if (wasBelowThirtyPercent && Random.value < ExtraHealChance)
            {
                int extraAmount = GetHealAmount();
                target.Heal(extraAmount);
                FloatingCombatNumber.ShowText(target.transform.position + Vector3.up * 0.6f, "EXTRA HEAL!", new Color(0.25f, 1f, 0.38f));
            }
        }
    }
}

