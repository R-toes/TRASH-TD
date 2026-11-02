using System.Collections.Generic;
using UnityEngine;
using TrashTD.Audio;
using TrashTD.Combat;
using TrashTD.Systems;

namespace TrashTD.Operators
{
    /// <summary>
    /// Panacea — 3-Star Medic with global range.
    /// Every attack interval (5 s) it heals the operator with the lowest HP% anywhere on the field (itself
    /// included); ties go to the lower absolute HP. No projectile: the heal lands at once while Panacea glows
    /// white over a halo of light, and the healed operator gets its own halo and a column of light
    /// (<see cref="HolyHealVisual"/>). Heal amount is ATK, like every Medic.
    /// </summary>
    public class PanaceaOperator : MedicOperator
    {
        protected override OperatorBase FindHealTarget()
        {
            return OperatorManager.Instance != null ? ChooseHealTarget(OperatorManager.Instance.DeployedOperators) : null;
        }

        public override void Heal(OperatorBase target)
        {
            if (target == null || isStunned || isBound) return;

            UnitSfx.Play(data, UnitSfxEvent.Attack);
            target.Heal(GetHealAmount());
            HolyHealVisual.PlayOnCaster(this);
            if (!ReferenceEquals(target, this)) HolyHealVisual.PlayOnTarget(target);
        }

        /// <summary>
        /// The injured operator (below 100% HP) with the lowest HP%; ties go to the lower absolute HP.
        /// Null when nobody needs healing.
        /// </summary>
        public static OperatorBase ChooseHealTarget(IReadOnlyList<OperatorBase> operators)
        {
            if (operators == null) return null;

            OperatorBase best = null;
            float bestRatio = 1f;
            for (int i = 0; i < operators.Count; i++)
            {
                OperatorBase op = operators[i];
                if (op == null || !op.IsDeployed || !op.CanReceiveHealing || op.MaxHP <= 0) continue;
                if (op.CurrentHP <= 0 || op.CurrentHP >= op.MaxHP) continue;

                float ratio = (float)op.CurrentHP / op.MaxHP;
                if (best == null || ratio < bestRatio || (Mathf.Approximately(ratio, bestRatio) && op.CurrentHP < best.CurrentHP))
                {
                    best = op;
                    bestRatio = ratio;
                }
            }
            return best;
        }
    }
}
